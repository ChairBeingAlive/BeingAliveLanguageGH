using BeingAliveLanguage.BalCore;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BeingAliveLanguage {
struct RootBranch {
  public NurbsCurve crv;
  public Interval phaseRange;

  public RootBranch(in NurbsCurve crv, in Interval phaseRange) {
    this.crv = crv;
    this.phaseRange = phaseRange;
  }
}

class RootTree3D {
  private SoilMap3d mMap3d = null;
  private Plane mBasePln = Plane.WorldXY;
  private Point3d mAnchor = new Point3d();
  double mUnitLen = 0.0;
  double mTargetRootRadius = 0.0;  // Target horizontal span based on tree canopy
  int mPhase = 0;
  int mDivN = 1;
  bool mToggleExplorer = false;
  bool mSimplifiedMode = false;  // true when no SoilMap3d provided
  bool mTrueScale = false;  // true to scale roots to match tree canopy

  List<RootBranch> mRootMaster = new List<RootBranch>();
  List<RootBranch> mRootTap = new List<RootBranch>();
  List<RootBranch> mRootExplorer = new List<RootBranch>();
  public Point3d debugPt;

  /// <summary>
  /// Explorer Root Lifespan Table:
  /// All explorer roots have a lifespan of 2 phases.
  /// 
  /// | Start Phase | End Phase | Visible At      |
  /// |-------------|-----------|-----------------|
  /// | 3           | 5         | Phases 3, 4, 5  |
  /// | 4           | 6         | Phases 4, 5, 6  |
  /// | 5           | 7         | Phases 5, 6, 7  |
  /// | 6           | 8         | Phases 6, 7, 8  |
  /// | 7           | 9         | Phases 7, 8, 9  |
  /// | 8           | 10        | Phases 8, 9, 10 |
  /// 
  /// This ensures explorer roots die off progressively from center to outside,
  /// preventing dense accumulation around phases 7-10.
  /// 
  /// Root Span Control:
  /// The root system is scaled post-generation to fit within targetRootRadius,
  /// which is typically 1.5x the tree's canopy radius. This ensures biological
  /// accuracy regardless of soil point density.
  /// </summary>

  public RootTree3D() {}

  public RootTree3D(in SoilMap3d map3d,
                    in Plane basePln,
                    in Point3d anchor,
                    double unitLen,
                    int phase,
                    int divN,
                    bool toggleExplorer = false,
                    double targetRootRadius = 0.0,
                    bool trueScale = false) {
    this.mMap3d = map3d;
    this.mBasePln = basePln;
    this.mAnchor = anchor;
    this.mUnitLen = unitLen;
    this.mPhase = phase;
    this.mDivN = divN;
    this.mToggleExplorer = toggleExplorer;
    this.mSimplifiedMode = (map3d == null);
    this.mTargetRootRadius = targetRootRadius;
    this.mTrueScale = trueScale;

    // If map3d is provided, use its plane
    if (map3d != null) {
      this.mBasePln = map3d.mPln;
    }
  }

  /// <summary>
  /// This function grows different part of the whole root structure gradually,
  /// assign them different phase range [start, end]
  /// when rootbranch in "start" phase, it falls into the "new" branch category
  /// when rootbranch in "end" phase, it falls into the "dead" branch category
  ///
  /// The logic applies to Level 1-3, where the level represent the layer in depth.
  ///
  /// </summary>
  public String GrowRoot() {
    // Get the directional vector based on divN
    Plane basePln = mBasePln;
    var vecLst = new List<Vector3d>();

    // ---------------------------------------------
    // Main TAP root
    // ---------------------------------------------
    var tapRootLen = mUnitLen * 0.3;
    var tapRoot_1 = GrowAlongVec(mAnchor, tapRootLen * 0.6, -basePln.ZAxis).ToNurbsCurve();
    tapRoot_1.Domain = new Interval(0, 1);

    var tapRoot_2 =
        GrowAlongVec(tapRoot_1.PointAtEnd, tapRootLen * 0.4, -basePln.ZAxis).ToNurbsCurve();
    mRootTap.Add(new RootBranch(tapRoot_1, new Interval(1, 11)));
    mRootTap.Add(new RootBranch(tapRoot_2, new Interval(2, 11)));

    // Only check soil density in non-simplified mode
    if (!mSimplifiedMode && tapRoot_1.GetLength() + tapRoot_2.GetLength() > tapRootLen * 1.5) {
      return String.Format("Soil context doesn't have enough points (density too low). Please " +
                           "increase the point number.");
    }

    // join two segments for later usage
    var tapRoot = NurbsCurve.JoinCurves(new List<NurbsCurve> { tapRoot_1, tapRoot_2 }).First();
    tapRoot.Domain = new Interval(0, 1);

    // ---------------------------------------------
    // LEVEL 1
    // ---------------------------------------------
    // horizontal roots
    Point3d lv1RootAnchor = tapRoot.PointAt(0.05);
    vecLst = GenerateVecLst(basePln, mDivN, false);
    List<Polyline> lv1HorizontalCore = new List<Polyline>();
    GrowAlongDirections(lv1RootAnchor, mUnitLen * 0.2, vecLst, out lv1HorizontalCore);
    lv1HorizontalCore.ForEach(
        x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(2, 12))));

    // Additional side branch lv1 roots
    var sideRoots = new List<RootBranch>();  // special treatment
    foreach (var root in lv1HorizontalCore) {
      List<Polyline> sideBranches = BranchOnSide(root, mUnitLen * 0.1, false);
      sideBranches.ForEach(
          x => sideRoots.Add(new RootBranch(x.ToNurbsCurve(), new Interval(2, 12))));
    }
    mRootMaster.AddRange(sideRoots);

    // Iteratively grow the Master Roots in Level 1 by bi-branching for max 3 times (Phase 3 - 5)
    List<double> lv1LengthParam = new List<double> { 0.1, 0.2, 0.3 };
    List<Polyline> frontEndRoots = new List<Polyline>(lv1HorizontalCore);
    var maxBranchLevel = Math.Min(mPhase - 2, 3);
    for (int branchLv = 0; branchLv < maxBranchLevel; branchLv++) {
      // FIX: startPhase should increment with branchLv (phase 3, 4, 5)
      var startPhase = 3 + branchLv;
      var nextLevelRoots = new List<Polyline>();
      var surroundTapRoots = new List<Polyline>();
      var explorerRoots = new List<Polyline>();

      // branch out the master roots and generate tap roots
      foreach (var root in frontEndRoots) {
        // master
        List<Polyline> branchedRoots = BranchRoot(root, mUnitLen * lv1LengthParam[branchLv], 1);
        nextLevelRoots.AddRange(branchedRoots);

        // tap
        Polyline newTapRoot = GenerateTapRoot(root.ToNurbsCurve().PointAtEnd, tapRootLen * 0.7);
        surroundTapRoots.Add(newTapRoot);

        // exploiter (only when toggle is on)
        if (mToggleExplorer) {
          var rootExplorer = GenerateExplorationalRoots(root, 4);
          explorerRoots.AddRange(rootExplorer);
        }
      }

      //! collect the newly growed roots with phase interval
      // master roots lives till end
      nextLevelRoots.ForEach(x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 12))));

      // tap roots have lifespan = 5
      surroundTapRoots.ForEach(x => mRootTap.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, startPhase + 4))));

      // explorer roots: fixed lifespan relative to startPhase, not mPhase
      if (mToggleExplorer) {
        int explorerLifespan = 2;  // shorter fixed lifespan: dies after 2 phases
        explorerRoots.ForEach(x => mRootExplorer.Add(new RootBranch(
            x.ToNurbsCurve(), new Interval(startPhase, Math.Min(11, startPhase + explorerLifespan)))));
      }

      // update currentLevel for the next iteration
      frontEndRoots = nextLevelRoots;
    }

    // Phase 6-8: more steps growth of explorer without branching
    maxBranchLevel = Math.Min(3, mPhase - 5);
    double lenParam = 0.4;
    for (int branchLv = 0; branchLv < maxBranchLevel; branchLv++) {
      // FIX: startPhase should increment with branchLv (phase 6, 7, 8)
      var startPhase = 6 + branchLv;
      var masterCollection = new List<Polyline>();
      var exploiterCollection = new List<Polyline>();
      foreach (var root in frontEndRoots) {
        var newSegments = GrowAlongVecInSeg(root.ToNurbsCurve().PointAtEnd, mUnitLen * lenParam, root.ToNurbsCurve().TangentAtEnd, 4);
        masterCollection.AddRange(newSegments);

        if (mToggleExplorer) {
          var newExploiter = GenerateExplorationalRoots(root, 4);
          exploiterCollection.AddRange(newExploiter);
        }
      }

      masterCollection.ForEach(x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 12))));
      if (mToggleExplorer) {
        int explorerLifespan = 2;  // shorter fixed lifespan: dies after 2 phases
        exploiterCollection.ForEach(x => mRootExplorer.Add(new RootBranch(
            x.ToNurbsCurve(), new Interval(startPhase, Math.Min(11, startPhase + explorerLifespan)))));
      }

      frontEndRoots = masterCollection;
    }
    
    // additional explorer of the last generate seg - only runs once when mPhase > 6
    if (mPhase > 6 && mToggleExplorer) {
      // FIX: Use current phase as startPhase, not fixed 6
      var startPhase = mPhase;
      var exploiterCollection = new List<Polyline>();
      foreach (var root in frontEndRoots) {
        var newExploiter = GenerateExplorationalRoots(root, 5);
        exploiterCollection.AddRange(newExploiter);
      }

      int explorerLifespan = 2;  // dies after 2 phases
      exploiterCollection.ForEach(x => mRootExplorer.Add(new RootBranch(
          x.ToNurbsCurve(), new Interval(startPhase, Math.Min(12, startPhase + explorerLifespan)))));
    }

    // ---------------------------------------------
    // LEVEL 2
    // ---------------------------------------------
    // horizontal roots
    Point3d lv2RootAnchor = tapRoot.PointAt(0.4);
    vecLst = GenerateVecLst(basePln, mDivN - 1, false);
    List<Polyline> lv2HorizontalCore = new List<Polyline>();
    GrowAlongDirections(lv2RootAnchor, mUnitLen * 0.15, vecLst, out lv2HorizontalCore);
    lv2HorizontalCore.ForEach(
        x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(4, 11))));

    // Iteratively grow the Master Roots in Level 1 by bi-branching for max 3 times (Phase 5 - 6)
    List<double> lv2LengthParam = new List<double> { 0.1, 0.13 };
    frontEndRoots = new List<Polyline>(lv2HorizontalCore);
    maxBranchLevel = Math.Min(mPhase - 4, lv2LengthParam.Count);
    for (int branchLv = 0; branchLv < maxBranchLevel; branchLv++) {
      var startPhase = 5 + branchLv;  // This was already correct
      var nextLevelRoots = new List<Polyline>();
      var surroundTapRoots = new List<Polyline>();
      var explorerRoots = new List<Polyline>();

      // branch out the master roots and generate tap roots
      foreach (var root in frontEndRoots) {
        // master
        List<Polyline> branchedRoots = BranchRoot(root, mUnitLen * lv2LengthParam[branchLv], 1);
        nextLevelRoots.AddRange(branchedRoots);

        // tap
        Polyline newTapRoot = GenerateTapRoot(root.ToNurbsCurve().PointAtEnd, tapRootLen * 0.3);
        surroundTapRoots.Add(newTapRoot);

        // exploiter (only when toggle is on)
        if (mToggleExplorer) {
          var rootExplorer = GenerateExplorationalRoots(root, 3);
          explorerRoots.AddRange(rootExplorer);
        }
      }

      // collect the newly growed roots with phase interval
      nextLevelRoots.ForEach(x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 10))));
      surroundTapRoots.ForEach(x => mRootTap.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, startPhase + 3))));
      if (mToggleExplorer) {
        int explorerLifespan = 2;  // shorter for Level 2
        explorerRoots.ForEach(x => mRootExplorer.Add(new RootBranch(
            x.ToNurbsCurve(), new Interval(startPhase, Math.Min(10, startPhase + explorerLifespan)))));
      }

      // update currentLevel for the next iteration
      frontEndRoots = nextLevelRoots;
    }

    // Phase 7: more steps growth without branching
    maxBranchLevel = Math.Min(1, mPhase - 6);
    lenParam = 0.5;
    for (int branchLv = 0; branchLv < maxBranchLevel; branchLv++) {
      // FIX: startPhase should be 7 + branchLv
      var startPhase = 7 + branchLv;
      var masterCollection = new List<Polyline>();
      var exploiterCollection = new List<Polyline>();
      foreach (var root in frontEndRoots) {
        var newSegments = GrowAlongVecInSeg(root.ToNurbsCurve().PointAtEnd, mUnitLen * lenParam, root.ToNurbsCurve().TangentAtEnd, 4);
        masterCollection.AddRange(newSegments);

        if (mToggleExplorer) {
          var newExploiter = GenerateExplorationalRoots(root, 5);
          exploiterCollection.AddRange(newExploiter);
        }
      }

      masterCollection.ForEach(x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 10))));
      if (mToggleExplorer) {
        int explorerLifespan = 2;
        exploiterCollection.ForEach(x => mRootExplorer.Add(new RootBranch(
            x.ToNurbsCurve(), new Interval(startPhase, Math.Min(10, startPhase + explorerLifespan)))));
      }

      frontEndRoots = masterCollection;
    }

    // ---------------------------------------------
    // LEVEL 3
    // ---------------------------------------------
    // horizontal roots
    Point3d lv3RootAnchor = tapRoot.PointAt(0.9);
    vecLst = GenerateVecLst(basePln, mDivN - 2, false);
    List<Polyline> lv3HorizontalCore = new List<Polyline>();
    GrowAlongDirections(lv3RootAnchor, mUnitLen * 0.1, vecLst, out lv3HorizontalCore);
    lv3HorizontalCore.ForEach(
        x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(6, 10))));

    // Phase 7-8: more steps growth of without branching
    maxBranchLevel = Math.Min(1, mPhase - 5);
    lenParam = 0.5;
    for (int branchLv = 0; branchLv < maxBranchLevel; branchLv++) {
      // FIX: startPhase should be 7 + branchLv (though loop only runs once)
      var startPhase = 7 + branchLv;
      var masterCollection = new List<Polyline>();
      var exploiterCollection = new List<Polyline>();
      foreach (var root in frontEndRoots) {
        var newSegments = GrowAlongVecInSeg(root.ToNurbsCurve().PointAtEnd, mUnitLen * lenParam, root.ToNurbsCurve().TangentAtEnd, 4);
        masterCollection.AddRange(newSegments);

        if (mToggleExplorer) {
          var newExploiter = GenerateExplorationalRoots(root, 5);
          exploiterCollection.AddRange(newExploiter);
        }
      }

      masterCollection.ForEach(x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 9))));
      if (mToggleExplorer) {
        int explorerLifespan = 2;
        exploiterCollection.ForEach(x => mRootExplorer.Add(new RootBranch(
            x.ToNurbsCurve(), new Interval(startPhase, Math.Min(9, startPhase + explorerLifespan)))));
      }

      frontEndRoots = masterCollection;
    }

    // Scale all roots to fit within the target root radius (only if trueScale is enabled)
    if (mTrueScale) {
      ScaleToTargetRadius();
    }

    return "Success";
  }

  public List<Vector3d> GenerateVecLst(Plane basePln, int totalVectors, bool randomizeStart = false) {
    var vecLst = new List<Vector3d>();
    double angleIncrement = Math.PI * 2 / totalVectors;
    double startAngle = 0.0;

    // Randomize start angle if requested
    if (randomizeStart) {
      Random rand = new Random();
      startAngle = rand.NextDouble() * Math.PI * 2;
    }

    for (int i = 0; i < totalVectors; i++) {
      double theta = startAngle + (i * angleIncrement);
      Vector3d baseVec = basePln.XAxis * Math.Cos(theta) + basePln.YAxis * Math.Sin(theta);
      baseVec.Unitize();
      vecLst.Add(baseVec);
    }

    return vecLst;
  }

  // growing a segment along given vector
  private Polyline GrowAlongVec(in Point3d cen, in double maxLength, in Vector3d dir) {
    var rootBranch = new Polyline();
    rootBranch.Add(cen);

    // Simplified mode: just grow straight lines
    if (mSimplifiedMode) {
      Vector3d unitDir = dir;
      unitDir.Unitize();
      Point3d endPt = cen + unitDir * maxLength;
      rootBranch.Add(endPt);
      return rootBranch;
    }

    // Full mode: use soil map points
    int selectNum = 20;  // Number of candidate points to consider at each step
    Point3d curPt = cen;
    Vector3d curDir = dir;

    while (rootBranch.Length < maxLength) {
      // Get candidate points
      List<Point3d> candidates = mMap3d.GetNearestPoints(curPt, selectNum);

      // Select the best candidate based on direction similarity
      Point3d nextPt = SelectBestCandidate(curPt, candidates, curDir);

      // Move to the next point
      rootBranch.Add(nextPt);
      curPt = nextPt;
    }

    return rootBranch;
  }

  // growing a set of segments along a vector, used for growing multiple segments in a single step
  private List<Polyline> GrowAlongVecInSeg(in Point3d cen, in double maxLength, in Vector3d dir, in int segNum) {
    List<Polyline> res = new List<Polyline>();
    var segLen = maxLength / segNum;

    Point3d startPt = cen;
    for (int i = 0; i < segNum; i++) {
      var newSeg = GrowAlongVec(startPt, segLen, dir);
      res.Add(newSeg);
    }

    return res;
  }

  private void GrowAlongDirections(in Point3d cen, in double maxLength, in List<Vector3d> vecLst, out List<Polyline> res) {
    res = new List<Polyline>();

    // Grow roots along each direction
    foreach (Vector3d direction in vecLst) {
      res.Add(GrowAlongVec(cen, maxLength, direction));
    }
  }

  // growing 1-2 side root as the perenial roots
  private List<Polyline> BranchOnSide(Polyline root, double length, bool rnd = false) {
    List<Polyline> res = new List<Polyline>();
    NurbsCurve rootCurve = root.ToNurbsCurve();
    rootCurve.Domain = new Interval(0, 1);

    int branchNum = 2;
    if (rnd)
      branchNum = Utils.balRnd.Next() % 2 == 0 ? 1 : 2;

    // generate branch points
    List<Point3d> branchPoints = new List<Point3d>();
    if (branchNum == 1) {
      branchPoints.Add(rootCurve.PointAt(0.5));
    } else if (branchNum == 2) {
      branchPoints.Add(rootCurve.PointAt(0.3));
      branchPoints.Add(rootCurve.PointAt(0.6));
    }

    // generate directions and branch out
    for (int i = 0; i < branchPoints.Count; i++) {
      rootCurve.ClosestPoint(branchPoints[i], out double t);
      var tanVec = rootCurve.TangentAt(t);

      var sign = Math.Pow(-1, i);
      var perVec = sign * Vector3d.CrossProduct(tanVec, mBasePln.ZAxis);
      var branchDir = perVec * 0.5 + tanVec * 0.5;
      branchDir.Unitize();

      Polyline branch = GrowAlongVec(branchPoints[i], length, branchDir);
      res.Add(branch);
    }

    return res;
  }

  private List<Polyline> BranchRoot(Polyline root, double remainingLength, int branchCount) {
    List<Polyline> branches = new List<Polyline>();
    NurbsCurve rootCurve = root.ToNurbsCurve();
    rootCurve.Domain = new Interval(0, 1);

    for (int i = 0; i < branchCount; i++) {
      Point3d branchPoint = rootCurve.PointAtEnd;
      Vector3d tangent = rootCurve.TangentAtEnd;

      // Create two branch directions in the horizontal plane
      Vector3d horizontalPerp = Vector3d.CrossProduct(tangent, mBasePln.ZAxis);
      horizontalPerp.Unitize();

      // project the tangent vector to the horizontal plane
      tangent = Vector3d.CrossProduct(mBasePln.ZAxis, horizontalPerp);
      tangent.Unitize();

      Vector3d branchDir1 = (tangent + 0.5 * horizontalPerp);
      Vector3d branchDir2 = (tangent - 0.5 * horizontalPerp);
      branchDir1.Unitize();
      branchDir2.Unitize();

      // Calculate remaining length for each branch
      double branchLength = remainingLength / (branchCount - i);

      // Grow two new branches
      Polyline branch1 = GrowAlongVec(branchPoint, branchLength, branchDir1);
      Polyline branch2 = GrowAlongVec(branchPoint, branchLength, branchDir2);

      branches.Add(branch1);
      branches.Add(branch2);
    }
    return branches;
  }

  private Polyline GenerateTapRoot(Point3d startPoint, double length) {
    Vector3d downwardDirection = -mBasePln.ZAxis;
    return GrowAlongVec(startPoint, length, downwardDirection);
  }

  private Point3d SelectBestCandidate(Point3d currentPoint, List<Point3d> candidates, Vector3d currentDirection) {
    double bestAlignment = -1;
    Point3d bestCandidate = currentPoint;

    foreach (Point3d pt in candidates) {
      Vector3d toCandidate = pt - currentPoint;
      if (toCandidate.Length < mUnitLen * 0.01)
        continue;

      toCandidate.Unitize();
      currentDirection.Unitize();

      double alignment = Vector3d.Multiply(currentDirection, toCandidate);

      // roughly alignment is fine, early return
      if (alignment > 0.95) {
        bestCandidate = pt;
        return bestCandidate;
      }

      // if not clase, then find the best one
      if (alignment > bestAlignment) {
        bestAlignment = alignment;
        bestCandidate = pt;
      }
    }

    return bestCandidate;
  }

  private Polyline GrowSingleExplorationalRoot(Point3d startPt,
                                               Vector3d parentRootDir,
                                               double length,
                                               bool isReverse) {
    const int totalSteps = 4;
    const int horizontalSteps = 1;
    double stepLength = length / totalSteps;
    parentRootDir.Unitize();

    Polyline explorationRoot = new Polyline();
    explorationRoot.Add(startPt);
    Vector3d horizontalDir = Vector3d.CrossProduct(parentRootDir, mBasePln.ZAxis);
    horizontalDir *= isReverse ? -1 : 1;

    Point3d curPt = startPt;
    var randRatio = MathUtils.remap(MathUtils.balRnd.NextDouble(), 0.0, 1.0, 0.3, 0.7);
    Vector3d curDir = 0.7 * horizontalDir + randRatio * parentRootDir;

    for (int step = 0; step < totalSteps; step++) {
      if (step >= horizontalSteps) {
        curDir -= 0.5 * mBasePln.ZAxis;
      }

      Polyline segment = GrowAlongVec(curPt, stepLength, curDir);

      if (segment.Count > 1) {
        explorationRoot.AddRange(segment.GetRange(1, segment.Count - 1));
        curPt = segment.Last;
      } else {
        break;
      }
    }

    return explorationRoot;
  }

  private List<Polyline> GenerateExplorationalRoots(Polyline mainRoot, int pointCount) {
    List<Polyline> explorationalRoots = new List<Polyline>();

    NurbsCurve mainRootCurve = mainRoot.ToNurbsCurve();
    mainRootCurve.Domain = new Interval(0, 1);
    var ptParam = mainRootCurve.DivideByCount(pointCount, false).ToList();

    double explorationDist;
    for (int i = 0; i < ptParam.Count; i++) {
      var pt = mainRootCurve.PointAt(ptParam[i]);
      Vector3d mainRootDirection = mainRootCurve.TangentAt(ptParam[i]);
      mainRootDirection.Unitize();

      // Grow two explorational roots in opposite directions
      explorationDist =
          mainRoot.Length * MathUtils.remap(MathUtils.balRnd.NextDouble(), 0.0, 1.0, 0.03, 0.08);
      explorationalRoots.Add(
          GrowSingleExplorationalRoot(pt, mainRootDirection, explorationDist, false));

      explorationDist =
          mainRoot.Length * MathUtils.remap(MathUtils.balRnd.NextDouble(), 0.0, 1.0, 0.03, 0.08);
      explorationalRoots.Add(
          GrowSingleExplorationalRoot(pt, mainRootDirection, explorationDist, true));
    }

    return explorationalRoots;
  }

  public List<NurbsCurve> GetRootTap() {
    var res = new List<NurbsCurve>();
    foreach (var root in mRootTap) {
      var crv = root.crv;
      var phaseRange = root.phaseRange;
      if (phaseRange.IncludesParameter(mPhase))
        res.Add(crv);
    }
    return res;
  }

  public List<NurbsCurve> GetRootMaster() {
    var res = new List<NurbsCurve>();
    foreach (var root in mRootMaster) {
      var crv = root.crv;
      var phaseRange = root.phaseRange;
      if (phaseRange.IncludesParameter(mPhase))
        res.Add(crv);
    }
    return res;
  }

  public List<NurbsCurve> GetRootExplorer() {
    var res = new List<NurbsCurve>();
    foreach (var root in mRootExplorer) {
      var crv = root.crv;
      var phaseRange = root.phaseRange;
      if (phaseRange.IncludesParameter(mPhase))
        res.Add(crv);
    }
    return res;
  }

  public List<NurbsCurve> GetRootDead() {
    var res = new List<NurbsCurve>();
    foreach (var root in mRootExplorer) {
      var crv = root.crv;
      var phaseRange = root.phaseRange;
      if (mPhase > phaseRange.T1)
        res.Add(crv);
    }
    return res;
  }

  /// <summary>
  /// Get tap roots organized by their start phase for debugging.
  /// Returns a dictionary where key is the start phase and value is the list of curves.
  /// </summary>
  public Dictionary<int, List<NurbsCurve>> GetRootTapByPhase() {
    var res = new Dictionary<int, List<NurbsCurve>>();
    foreach (var root in mRootTap) {
      var crv = root.crv;
      var phaseRange = root.phaseRange;
      if (phaseRange.IncludesParameter(mPhase)) {
        int startPhase = (int)phaseRange.T0;
        if (!res.ContainsKey(startPhase))
          res[startPhase] = new List<NurbsCurve>();
        res[startPhase].Add(crv);
      }
    }
    return res;
  }

  /// <summary>
  /// Get master roots organized by their start phase for debugging.
  /// Returns a dictionary where key is the start phase and value is the list of curves.
  /// </summary>
  public Dictionary<int, List<NurbsCurve>> GetRootMasterByPhase() {
    var res = new Dictionary<int, List<NurbsCurve>>();
    foreach (var root in mRootMaster) {
      var crv = root.crv;
      var phaseRange = root.phaseRange;
      if (phaseRange.IncludesParameter(mPhase)) {
        int startPhase = (int)phaseRange.T0;
        if (!res.ContainsKey(startPhase))
          res[startPhase] = new List<NurbsCurve>();
        res[startPhase].Add(crv);
      }
    }
    return res;
  }

  /// <summary>
  /// Get explorer roots organized by their start phase for debugging.
  /// Returns a dictionary where key is the start phase and value is the list of curves.
  /// </summary>
  public Dictionary<int, List<NurbsCurve>> GetRootExplorerByPhase() {
    var res = new Dictionary<int, List<NurbsCurve>>();
    foreach (var root in mRootExplorer) {
      var crv = root.crv;
      var phaseRange = root.phaseRange;
      if (phaseRange.IncludesParameter(mPhase)) {
        int startPhase = (int)phaseRange.T0;
        if (!res.ContainsKey(startPhase))
          res[startPhase] = new List<NurbsCurve>();
        res[startPhase].Add(crv);
      }
    }
    return res;
  }

  /// <summary>
  /// Get dead roots organized by their original start phase for debugging.
  /// Returns a dictionary where key is the start phase and value is the list of curves.
  /// </summary>
  public Dictionary<int, List<NurbsCurve>> GetRootDeadByPhase() {
    var res = new Dictionary<int, List<NurbsCurve>>();
    foreach (var root in mRootExplorer) {
      var crv = root.crv;
      var phaseRange = root.phaseRange;
      if (mPhase > phaseRange.T1) {
        int startPhase = (int)phaseRange.T0;
        if (!res.ContainsKey(startPhase))
          res[startPhase] = new List<NurbsCurve>();
        res[startPhase].Add(crv);
      }
    }
    return res;
  }

  /// <summary>
  /// Scale all roots uniformly in 3D to fit within the target root radius.
  /// This is called after GrowRoot() to ensure roots span approximately 1.5x the tree canopy.
  /// Uniform 3D scaling preserves the natural proportions of the root system.
  /// </summary>
  public void ScaleToTargetRadius() {
    if (mTargetRootRadius <= 0) return;

    // Calculate current maximum horizontal extent from anchor
    double maxHorizontalDist = 0;
    
    // Check all root types for maximum horizontal distance
    foreach (var root in mRootMaster) {
      maxHorizontalDist = Math.Max(maxHorizontalDist, GetMaxHorizontalDistance(root.crv));
    }
    foreach (var root in mRootTap) {
      maxHorizontalDist = Math.Max(maxHorizontalDist, GetMaxHorizontalDistance(root.crv));
    }
    foreach (var root in mRootExplorer) {
      maxHorizontalDist = Math.Max(maxHorizontalDist, GetMaxHorizontalDistance(root.crv));
    }

    // If roots are already within target or no roots exist, skip scaling
    if (maxHorizontalDist <= 0 || maxHorizontalDist <= mTargetRootRadius) return;

    // Calculate scale factor
    double scaleFactor = mTargetRootRadius / maxHorizontalDist;

    // Apply uniform 3D scaling from the anchor point
    ScaleRootsUniformly(scaleFactor);
  }

  /// <summary>
  /// Get the maximum horizontal (XY plane) distance from anchor for any point on the curve.
  /// </summary>
  private double GetMaxHorizontalDistance(NurbsCurve crv) {
    double maxDist = 0;
    
    // Sample points along the curve
    int sampleCount = Math.Max(10, (int)(crv.GetLength() / (mUnitLen * 0.1)));
    crv.Domain = new Interval(0, 1);
    
    for (int i = 0; i <= sampleCount; i++) {
      double t = (double)i / sampleCount;
      Point3d pt = crv.PointAt(t);
      
      // Project to horizontal plane and calculate distance from anchor
      Vector3d toPoint = pt - mAnchor;
      // Remove vertical component (along plane's Z axis)
      double verticalComponent = toPoint * mBasePln.ZAxis;
      Vector3d horizontalVec = toPoint - verticalComponent * mBasePln.ZAxis;
      
      maxDist = Math.Max(maxDist, horizontalVec.Length);
    }
    
    return maxDist;
  }

  /// <summary>
  /// Scale all root curves uniformly in 3D from the anchor point.
  /// This preserves the natural proportions of the root system.
  /// </summary>
  private void ScaleRootsUniformly(double scaleFactor) {
    // Create uniform 3D scale transform centered at anchor
    Transform scaleXform = Transform.Scale(mAnchor, scaleFactor);
    
    // Scale master roots
    for (int i = 0; i < mRootMaster.Count; i++) {
      var crv = mRootMaster[i].crv.DuplicateCurve() as NurbsCurve;
      crv.Transform(scaleXform);
      crv.Domain = new Interval(0, 1);
      mRootMaster[i] = new RootBranch(crv, mRootMaster[i].phaseRange);
    }
    
    // Scale tap roots
    for (int i = 0; i < mRootTap.Count; i++) {
      var crv = mRootTap[i].crv.DuplicateCurve() as NurbsCurve;
      crv.Transform(scaleXform);
      crv.Domain = new Interval(0, 1);
      mRootTap[i] = new RootBranch(crv, mRootTap[i].phaseRange);
    }
    
    // Scale explorer roots
    for (int i = 0; i < mRootExplorer.Count; i++) {
      var crv = mRootExplorer[i].crv.DuplicateCurve() as NurbsCurve;
      crv.Transform(scaleXform);
      crv.Domain = new Interval(0, 1);
      mRootExplorer[i] = new RootBranch(crv, mRootExplorer[i].phaseRange);
    }
  }
}
}
