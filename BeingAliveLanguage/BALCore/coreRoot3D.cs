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
    int mPhase = 0;
    int mDivN = 1;
    bool mToggleExplorer = false;
    bool mSimplifiedMode = false;  // true when no SoilMap3d provided
    private Random mRnd;           // Seed-based random generator for this root system

    List<RootBranch> mRootMaster = new List<RootBranch>();
    List<RootBranch> mRootTap = new List<RootBranch>();
    List<RootBranch> mRootExplorer = new List<RootBranch>();
    public Point3d debugPt;

    /// <summary>
    /// Explorer Root Lifespan Table:
    /// Explorer roots appear 1 phase AFTER the master roots they're attached to,
    /// and have a lifespan of 2 phases.
    ///
    /// | Master Root Phase | Explorer Start | Explorer End | Visible At      |
    /// |-------------------|----------------|--------------|-----------------|
    /// | 3                 | 4              | 6            | Phases 4, 5, 6  |
    /// | 4                 | 5              | 7            | Phases 5, 6, 7  |
    /// | 5                 | 6              | 8            | Phases 6, 7, 8  |
    /// | 6                 | 7              | 9            | Phases 7, 8, 9  |
    /// | 7                 | 8              | 10           | Phases 8, 9, 10 |
    /// | 8                 | 9              | 11           | Phases 9, 10, 11|
    ///
    /// This ensures explorer roots appear after the master roots grow,
    /// and die off progressively from center to outside.
    ///
    /// Root Span Control:
    /// The root system is scaled post-generation to fit within targetRootRadius,
    /// which is typically 1.5x the tree's canopy radius. This ensures biological
    /// accuracy regardless of soil point density.
    /// </summary>

    public RootTree3D() {
      mRnd = new Random();
    }

    /// <summary>
    /// Constructor for RootTree3D.
    /// </summary>
    /// <param name="map3d">Soil map for root growth direction guidance. If null, simplified mode is
    /// used.</param>
    /// <param name="basePln">Base plane for root orientation.</param>
    /// <param name="anchor">Anchor point where roots start.</param>
    /// <param name="unitLen">Unit length for root sizing (from tree). All growth distances are
    /// proportional to this.</param>
    /// <param name="phase">Current growth phase.</param>
    /// <param name="divN">Number of radial divisions for root directions.</param>
    /// <param name="toggleExplorer">Whether to generate explorer roots.</param>
    /// <param name="seed">Random seed for root variation. Same seed produces same root pattern.</param>
    public RootTree3D(in SoilMap3d map3d, in Plane basePln, in Point3d anchor, double unitLen,
                      int phase, int divN, bool toggleExplorer = false, int seed = 0) {
      this.mMap3d = map3d;
      this.mBasePln = basePln;
      this.mAnchor = anchor;
      this.mUnitLen = unitLen;
      this.mPhase = phase;
      this.mDivN = divN;
      this.mToggleExplorer = toggleExplorer;
      this.mSimplifiedMode = (map3d == null);
      this.mRnd = new Random(seed);  // Use seed for reproducible random patterns

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

      var tapRoot_2 = GrowAlongVec(tapRoot_1.PointAtEnd, tapRootLen * 0.4, -basePln.ZAxis).ToNurbsCurve();
      mRootTap.Add(new RootBranch(tapRoot_1, new Interval(1, 11)));
      mRootTap.Add(new RootBranch(tapRoot_2, new Interval(2, 11)));

      // Only check soil density in non-simplified mode
      if (!mSimplifiedMode && tapRoot_1.GetLength() + tapRoot_2.GetLength() > tapRootLen * 1.5) {
        return "Soil context doesn't have enough points (density too low). Please increase the point number.";
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
      lv1HorizontalCore.ForEach(x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(2, 12))));

      // Additional side branch lv1 roots
      var sideRoots = new List<RootBranch>();  // special treatment
      foreach (var root in lv1HorizontalCore) {
        List<Polyline> sideBranches = BranchOnSide(root, mUnitLen * 0.1, false);
        sideBranches.ForEach(x => sideRoots.Add(new RootBranch(x.ToNurbsCurve(), new Interval(2, 12))));
      }
      mRootMaster.AddRange(sideRoots);

      // Iteratively grow the Master Roots in Level 1 by bi-branching for max 3 times (Phase 3 - 5)
      List<double> lv1LengthParam = new List<double> { 0.1, 0.2, 0.3 };
      List<Polyline> frontEndRoots = new List<Polyline>(lv1HorizontalCore);
      var maxBranchLevel = Math.Min(mPhase - 2, 3);
      for (int branchLv = 0; branchLv < maxBranchLevel; branchLv++) {
        // startPhase increments with branchLv (phase 3, 4, 5)
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

          // exploiter: generate on the NEW branched roots
          // Explorer roots appear 1 phase AFTER the master roots they're attached to
          if (mToggleExplorer) {
            foreach (var branchedRoot in branchedRoots) {
              var rootExplorer = GenerateExplorationalRoots(branchedRoot, 4);
              explorerRoots.AddRange(rootExplorer);
            }
          }
        }

        //! collect the newly growed roots with phase interval
        // master roots lives till end
        nextLevelRoots.ForEach(x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 12))));

        // tap roots have lifespan = 5
        surroundTapRoots.ForEach(x => mRootTap.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, startPhase + 4))));

        // explorer roots: start 1 phase AFTER master roots, with fixed lifespan of 2
        if (mToggleExplorer) {
          int explorerStartPhase = startPhase + 1;  // Explorers appear 1 phase after master roots
          int explorerLifespan = 2;
          explorerRoots.ForEach(x => mRootExplorer.Add(new RootBranch(x.ToNurbsCurve(), new Interval(explorerStartPhase, Math.Min(11, explorerStartPhase + explorerLifespan)))));
        }

        // update currentLevel for the next iteration
        frontEndRoots = nextLevelRoots;
      }

      // Phase 6-8: more steps growth of explorer without branching
      maxBranchLevel = Math.Min(3, mPhase - 5);
      double lenParam = 0.4;
      for (int branchLv = 0; branchLv < maxBranchLevel; branchLv++) {
        // startPhase increments with branchLv (phase 6, 7, 8)
        var startPhase = 6 + branchLv;
        var masterCollection = new List<Polyline>();
        var frontEndCollection = new List<Polyline>();  // Only the last segment of each chain
        var exploiterCollection = new List<Polyline>();
        foreach (var root in frontEndRoots) {
          var newSegments = GrowAlongVecInSeg(root.ToNurbsCurve().PointAtEnd, mUnitLen * lenParam, root.ToNurbsCurve().TangentAtEnd, 4);
          masterCollection.AddRange(newSegments);

          // Only keep the last segment as the new front end for next iteration
          if (newSegments.Count > 0) {
            frontEndCollection.Add(newSegments.Last());
          }

          if (mToggleExplorer) {
            // Generate explorers on ALL new segments
            foreach (var seg in newSegments) {
              var newExploiter = GenerateExplorationalRoots(seg, 2);
              exploiterCollection.AddRange(newExploiter);
            }
          }
        }

        masterCollection.ForEach(x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 12))));

        // explorer roots: start 1 phase AFTER master roots, with fixed lifespan of 2
        if (mToggleExplorer) {
          int explorerStartPhase = startPhase + 1;  // Explorers appear 1 phase after master roots
          int explorerLifespan = 2;
          exploiterCollection.ForEach(x => mRootExplorer.Add(new RootBranch(x.ToNurbsCurve(), new Interval(explorerStartPhase, Math.Min(11, explorerStartPhase + explorerLifespan)))));
        }

        // Only use the last segment of each chain as the front end for next iteration
        frontEndRoots = frontEndCollection;
      }

      // ---------------------------------------------
      // LEVEL 2
      // ---------------------------------------------
      // horizontal roots
      Point3d lv2RootAnchor = tapRoot.PointAt(0.4);
      vecLst = GenerateVecLst(basePln, mDivN - 1, false);
      List<Polyline> lv2HorizontalCore = new List<Polyline>();
      GrowAlongDirections(lv2RootAnchor, mUnitLen * 0.15, vecLst, out lv2HorizontalCore);
      lv2HorizontalCore.ForEach(x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(4, 11))));

      // Iteratively grow the Master Roots in Level 2 by bi-branching for max 2 times (Phase 5 - 6)
      List<double> lv2LengthParam = new List<double> { 0.1, 0.13 };
      frontEndRoots = new List<Polyline>(lv2HorizontalCore);
      maxBranchLevel = Math.Min(mPhase - 4, lv2LengthParam.Count);
      for (int branchLv = 0; branchLv < maxBranchLevel; branchLv++) {
        var startPhase = 5 + branchLv;
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

          // exploiter: generate on the NEW branched roots
          if (mToggleExplorer) {
            foreach (var branchedRoot in branchedRoots) {
              var rootExplorer = GenerateExplorationalRoots(branchedRoot, 3);
              explorerRoots.AddRange(rootExplorer);
            }
          }
        }

        // collect the newly growed roots with phase interval
        nextLevelRoots.ForEach(x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 10))));
        surroundTapRoots.ForEach(x => mRootTap.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, startPhase + 3))));

        // explorer roots: start 1 phase AFTER master roots, with fixed lifespan of 2
        if (mToggleExplorer) {
          int explorerStartPhase = startPhase + 1;  // Explorers appear 1 phase after master roots
          int explorerLifespan = 2;
          explorerRoots.ForEach(x => mRootExplorer.Add(new RootBranch(x.ToNurbsCurve(), new Interval(explorerStartPhase, Math.Min(10, explorerStartPhase + explorerLifespan)))));
        }

        // update currentLevel for the next iteration
        frontEndRoots = nextLevelRoots;
      }

      // Phase 7: more steps growth without branching
      maxBranchLevel = Math.Min(1, mPhase - 6);
      lenParam = 0.5;
      for (int branchLv = 0; branchLv < maxBranchLevel; branchLv++) {
        var startPhase = 7 + branchLv;
        var masterCollection = new List<Polyline>();
        var frontEndCollection = new List<Polyline>();  // Only the last segment of each chain
        var exploiterCollection = new List<Polyline>();
        foreach (var root in frontEndRoots) {
          var newSegments = GrowAlongVecInSeg(root.ToNurbsCurve().PointAtEnd, mUnitLen * lenParam, root.ToNurbsCurve().TangentAtEnd, 4);
          masterCollection.AddRange(newSegments);

          // Only keep the last segment as the new front end for next iteration
          if (newSegments.Count > 0) {
            frontEndCollection.Add(newSegments.Last());
          }

          if (mToggleExplorer) {
            // Generate explorers on ALL new segments
            foreach (var seg in newSegments) {
              var newExploiter = GenerateExplorationalRoots(seg, 2);
              exploiterCollection.AddRange(newExploiter);
            }
          }
        }

        masterCollection.ForEach(x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 10))));

        // explorer roots: start 1 phase AFTER master roots, with fixed lifespan of 2
        if (mToggleExplorer) {
          int explorerStartPhase = startPhase + 1;  // Explorers appear 1 phase after master roots
          int explorerLifespan = 2;
          exploiterCollection.ForEach(x => mRootExplorer.Add(new RootBranch(x.ToNurbsCurve(), new Interval(explorerStartPhase, Math.Min(10, explorerStartPhase + explorerLifespan)))));
        }

        // Only use the last segment of each chain as the front end for next iteration
        frontEndRoots = frontEndCollection;
      }

      // ---------------------------------------------
      // LEVEL 3
      // ---------------------------------------------
      // horizontal roots
      Point3d lv3RootAnchor = tapRoot.PointAt(0.9);
      vecLst = GenerateVecLst(basePln, mDivN - 2, false);
      List<Polyline> lv3HorizontalCore = new List<Polyline>();
      GrowAlongDirections(lv3RootAnchor, mUnitLen * 0.1, vecLst, out lv3HorizontalCore);
      lv3HorizontalCore.ForEach(x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(6, 10))));

      // Phase 7-8: more steps growth without branching
      maxBranchLevel = Math.Min(1, mPhase - 5);
      lenParam = 0.4;
      for (int branchLv = 0; branchLv < maxBranchLevel; branchLv++) {
        var startPhase = 7 + branchLv;
        var masterCollection = new List<Polyline>();
        var frontEndCollection = new List<Polyline>();  // Only the last segment of each chain
        var exploiterCollection = new List<Polyline>();
        foreach (var root in frontEndRoots) {
          var newSegments = GrowAlongVecInSeg(root.ToNurbsCurve().PointAtEnd, mUnitLen * lenParam, root.ToNurbsCurve().TangentAtEnd, 4);
          masterCollection.AddRange(newSegments);

          // Only keep the last segment as the new front end for next iteration
          if (newSegments.Count > 0) {
            frontEndCollection.Add(newSegments.Last());
          }

          if (mToggleExplorer) {
            // Generate explorers on ALL new segments
            foreach (var seg in newSegments) {
              var newExploiter = GenerateExplorationalRoots(seg, 2);
              exploiterCollection.AddRange(newExploiter);
            }
          }
        }

        masterCollection.ForEach(x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 9))));

        // explorer roots: start 1 phase AFTER master roots, with fixed lifespan of 2
        if (mToggleExplorer) {
          int explorerStartPhase = startPhase + 1;  // Explorers appear 1 phase after master roots
          int explorerLifespan = 2;
          exploiterCollection.ForEach(x => mRootExplorer.Add(new RootBranch(x.ToNurbsCurve(), new Interval(explorerStartPhase, Math.Min(9, explorerStartPhase + explorerLifespan)))));
        }

        // Only use the last segment of each chain as the front end for next iteration
        frontEndRoots = frontEndCollection;
      }

      return "Success";
    }

    public List<Vector3d> GenerateVecLst(Plane basePln, int totalVectors, bool randomizeStart = false) {
      var vecLst = new List<Vector3d>();
      double angleIncrement = Math.PI * 2 / totalVectors;
      double startAngle = 0.0;

      // Randomize start angle if requested, using seed-based random
      if (randomizeStart) {
        startAngle = mRnd.NextDouble() * Math.PI * 2;
      }

      for (int i = 0; i < totalVectors; i++) {
        double theta = startAngle + (i * angleIncrement);
        Vector3d baseVec = basePln.XAxis * Math.Cos(theta) + basePln.YAxis * Math.Sin(theta);
        baseVec.Unitize();
        vecLst.Add(baseVec);
      }

      return vecLst;
    }

    /// <summary>
    /// Growing a segment along given vector.
    /// Soil points are used for DIRECTION guidance only - actual length is always maxLength.
    /// For horizontal roots, direction is constrained to stay roughly horizontal.
    /// </summary>
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

      // Determine if this is a primarily horizontal root (small vertical component)
      Vector3d initialDir = dir;
      initialDir.Unitize();
      double verticalComponent = Math.Abs(initialDir * mBasePln.ZAxis);
      bool isHorizontalRoot = verticalComponent < 0.7;  // Less than ~45 degrees from horizontal

      // Full mode: use soil map points for direction guidance
      // but control the actual distance traveled
      int numSteps = 5;  // Number of steps to reach maxLength
      double stepLen = maxLength / numSteps;

      Point3d curPt = cen;
      Vector3d curDir = dir;
      curDir.Unitize();

      for (int step = 0; step < numSteps; step++) {
        // Get candidate points to determine best direction
        List<Point3d> candidates = mMap3d.GetNearestPoints(curPt, 20);

        // Find best direction based on candidates
        Vector3d bestDir = FindBestDirection(curPt, candidates, curDir);

        // For horizontal roots, constrain direction to stay roughly horizontal
        // Allow vertical variation for natural look, but prevent gradual downward drift
        if (isHorizontalRoot) {
          bestDir = ConstrainToHorizontal(bestDir, curDir);
        }

        // Move exactly stepLen in the best direction
        Point3d nextPt = curPt + bestDir * stepLen;
        rootBranch.Add(nextPt);

        // Update for next iteration
        curPt = nextPt;
        curDir = bestDir;
      }

      return rootBranch;
    }

    /// <summary>
    /// Constrain a direction vector to stay roughly horizontal.
    /// Allows vertical variation for natural look, but prevents excessive downward drift.
    /// </summary>
    private Vector3d ConstrainToHorizontal(Vector3d proposedDir, Vector3d originalDir) {
      // Get the horizontal component of the proposed direction
      double verticalComponent = proposedDir * mBasePln.ZAxis;
      Vector3d horizontalDir = proposedDir - verticalComponent * mBasePln.ZAxis;

      if (horizontalDir.Length < 0.001) {
        // If proposed direction is nearly vertical, use original horizontal direction
        double origVertical = originalDir * mBasePln.ZAxis;
        horizontalDir = originalDir - origVertical * mBasePln.ZAxis;
      }

      horizontalDir.Unitize();

      // Allow moderate vertical variation (up to 35%) for natural undulation
      // but clamp to prevent excessive downward drift
      double maxVerticalRatio = 0.3;
      double clampedVertical = Math.Max(-maxVerticalRatio, Math.Min(maxVerticalRatio, verticalComponent));

      Vector3d constrainedDir = horizontalDir + clampedVertical * mBasePln.ZAxis;
      constrainedDir.Unitize();

      return constrainedDir;
    }

    /// <summary>
    /// Find the best direction to grow based on nearby soil points.
    /// Returns a unit vector that blends candidate direction with preferred direction for smoothness.
    /// </summary>
    private Vector3d FindBestDirection(Point3d currentPoint, List<Point3d> candidates, Vector3d preferredDir) {
      preferredDir.Unitize();

      // Collect good candidate directions (alignment > 0.5)
      var goodCandidates = new List<(Vector3d dir, double alignment)>();

      foreach (Point3d pt in candidates) {
        Vector3d toCandidate = pt - currentPoint;
        if (toCandidate.Length < mUnitLen * 0.001)
          continue;

        toCandidate.Unitize();
        double alignment = Vector3d.Multiply(preferredDir, toCandidate);

        if (alignment > 0.5) {
          goodCandidates.Add((toCandidate, alignment));
        }
      }

      // If no good candidates, return preferred direction
      if (goodCandidates.Count == 0) {
        return preferredDir;
      }

      // Sort by alignment (best first)
      goodCandidates.Sort((a, b) => b.alignment.CompareTo(a.alignment));

      // Always pick the best aligned candidate for smoother paths
      Vector3d bestCandidateDir = goodCandidates[0].dir;

      // Blend candidate direction with preferred direction for smooth curves
      // Higher blend ratio = smoother but less responsive to soil
      // Lower blend ratio = more responsive to soil but potentially jagged
      double blendRatio = 0.4;  // 40% preferred direction, 60% candidate direction
      Vector3d blendedDir = preferredDir * blendRatio + bestCandidateDir * (1.0 - blendRatio);
      blendedDir.Unitize();

      // Add very slight random perturbation for natural variation (±5 degrees max)
      double perturbAngle = MathUtils.remap(mRnd.NextDouble(), 0.0, 1.0, -0.087, 0.087);  // ~5 degrees in radians
      Vector3d perpVec = Vector3d.CrossProduct(blendedDir, mBasePln.ZAxis);
      if (perpVec.Length > 0.001) {
        perpVec.Unitize();
        blendedDir.Rotate(perturbAngle, perpVec);
      }

      return blendedDir;
    }

    /// <summary>
    /// Growing a set of segments along a vector, used for growing multiple segments in a single step.
    /// </summary>
    private List<Polyline> GrowAlongVecInSeg(in Point3d cen, in double maxLength, in Vector3d dir, in int segNum) {
      List<Polyline> res = new List<Polyline>();
      var segLen = maxLength / segNum;

      Point3d startPt = cen;
      for (int i = 0; i < segNum; i++) {
        var newSeg = GrowAlongVec(startPt, segLen, dir);
        res.Add(newSeg);

        // Update startPt to the end of the current segment for the next iteration
        if (newSeg.Count > 0) {
          startPt = newSeg.Last;
        }
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

    /// <summary>
    /// Growing 1-2 side roots as the perennial roots.
    /// </summary>
    private List<Polyline> BranchOnSide(Polyline root, double length, bool rnd = false) {
      List<Polyline> res = new List<Polyline>();
      NurbsCurve rootCurve = root.ToNurbsCurve();
      rootCurve.Domain = new Interval(0, 1);

      int branchNum = 2;
      if (rnd)
        branchNum = mRnd.Next() % 2 == 0 ? 1 : 2;  // Use seed-based random

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

    /// <summary>
    /// Grow a single explorational root from the given start point.
    /// Explorer roots grow perpendicular to parent with progressive downward curve.
    /// Uses more steps for smoother appearance and curves downward more aggressively.
    /// </summary>
    private Polyline GrowSingleExplorationalRoot(Point3d startPt, Vector3d parentRootDir, double length, bool isReverse) {
      const int totalSteps = 8;       // Increased from 5 for smoother curves
      const int horizontalSteps = 2;  // Stay mostly horizontal for first 2 steps, then curve down
      double stepLength = length / totalSteps;
      parentRootDir.Unitize();

      Polyline explorationRoot = new Polyline();
      explorationRoot.Add(startPt);
      Vector3d horizontalDir = Vector3d.CrossProduct(parentRootDir, mBasePln.ZAxis);
      horizontalDir.Unitize();
      horizontalDir *= isReverse ? -1 : 1;

      Point3d curPt = startPt;

      // Use seed-based random for reproducible patterns
      // Explorer grows perpendicular to parent root with slight forward component
      double randRatio = mSimplifiedMode ? 0.25 : MathUtils.remap(mRnd.NextDouble(), 0.0, 1.0, 0.15, 0.35);

      // Start with stronger downward bias from the beginning
      double initialDownward = mSimplifiedMode ? 0.2 : MathUtils.remap(mRnd.NextDouble(), 0.0, 1.0, 0.15, 0.25);
      Vector3d curDir = 0.7 * horizontalDir + randRatio * parentRootDir - initialDownward * mBasePln.ZAxis;
      curDir.Unitize();

      for (int step = 0; step < totalSteps; step++) {
        // Progressive downward curve - starts earlier and increases more gradually
        if (step >= horizontalSteps) {
          // Smoother progression over more steps: 0.15, 0.22, 0.29, 0.36, 0.43, 0.50
          double downwardStrength = 0.15 + 0.07 * (step - horizontalSteps);
          curDir = curDir - downwardStrength * mBasePln.ZAxis;
          curDir.Unitize();
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

    /// <summary>
    /// Generate explorational roots along a main root segment.
    /// </summary>
    private List<Polyline> GenerateExplorationalRoots(Polyline mainRoot, int pointCount) {
      List<Polyline> explorationalRoots = new List<Polyline>();

      NurbsCurve mainRootCurve = mainRoot.ToNurbsCurve();
      mainRootCurve.Domain = new Interval(0, 1);

      // Generate evenly spaced points along the segment
      var ptParams = new List<double>();

      if (pointCount <= 1) {
        ptParams.Add(0.0);
      } else {
        for (int i = 0; i < pointCount; i++) {
          double param = (double)i / pointCount;
          ptParams.Add(param);
        }
      }

      // Base length for explorer roots - increased by 50% for longer explorers
      double baseLength = mSimplifiedMode ? mUnitLen * 0.2 : mainRoot.Length * 1.5;

      foreach (var param in ptParams) {
        var pt = mainRootCurve.PointAt(param);
        Vector3d mainRootDirection = mainRootCurve.TangentAt(param);
        mainRootDirection.Unitize();

        double explorationDist;
        if (mSimplifiedMode) {
          // In simplified mode, use fixed distance - longer explorers (1.5x increase)
          explorationDist = baseLength * 1.5;
        } else {
          // In full mode, use seed-based random ratio - longer range (2.5-3.5x parent length)
          explorationDist = baseLength * MathUtils.remap(mRnd.NextDouble(), 0.0, 1.0, 2.0, 2.8);
        }

        // Grow two explorational roots in opposite directions
        explorationalRoots.Add(GrowSingleExplorationalRoot(pt, mainRootDirection, explorationDist, false));
        explorationalRoots.Add(GrowSingleExplorationalRoot(pt, mainRootDirection, explorationDist, true));
      }

      return explorationalRoots;
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
  }
}
