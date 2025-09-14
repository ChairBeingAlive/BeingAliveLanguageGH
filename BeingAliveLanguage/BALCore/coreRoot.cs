using BeingAliveLanguage.BalCore;
using Rhino.Geometry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BeingAliveLanguage {
class RootSectional {
  // for sectional root, we use a "TREE" structure, and BFS for constructing the "radius" of each
  // branch
  public RootSectional() {}

  public RootSectional(in SoilMap2d map,
                       in RootProp rProps,
                       in EnvProp eProps = null,
                       in int seed = -1)

  // in Point3d anchor,
  //   string rootType, in int steps = 1, in int branchNum = 2, in int seed = -1,
  //   in EnvProp envP = null)
  // in bool envToggle = false, in double envRange = 0.0,
  // in List<Curve> envAtt = null, in List<Curve> envRep = null)
  {
    mSoilMap = map;

    mRootProps = rProps;
    mEnvProps = eProps;

    // mAnchor = anchor;
    // mSteps = steps;
    // mBranchNum = branchNum;
    mRootNode = new RootNode(mRootProps.anchor);

    if (mRootProps.rootType == "none")
      mMaxBranchLevel = 0;
    else if (mRootProps.rootType == "single")
      mMaxBranchLevel = 1;
    else if (mRootProps.rootType == "multi")
      mMaxBranchLevel = 2;

    // env param
    this.mEnvProps = eProps;

    // init random class and down vec
    mSeed = seed;
    mRnd = mSeed >= 0 ? new Random(mSeed) : Utils.balRnd;
    mDownDir = -mSoilMap.mPln.YAxis;

    // init scoreMap
    Parallel.ForEach(mSoilMap.kdMap, pt => { scoreMap.TryAdd(pt.Value, 0); });

    // this.envToggle = envToggle;
    // this.envDist = envRange;
    // this.envAtt = envAtt;
    // this.envRep = envRep;

#if DEBUG
    // debug
    DebugStore.Clear();
#endif
  }

  private bool RootDensityCheck(Point3d pt) {
    var k = Utils.PtString(pt);
    if (!mSoilEnv.ContainsKey(k))
      mSoilEnv[k] = 0;
    else if (mSoilEnv[k] > 20)
      return false;

    return true;
  }

  private static readonly Func<double, double> RootGrowFit = x => 0.05731 * Math.Exp(3.22225 * x);

  /// <summary>
  /// Main function to grow sectional roots.
  /// </summary>
  public void Grow() {
    var multiplierD = 0.8;  // distance control
    var multiplierG = 0.5;  // gravity
    var multiplierP = 0.3;  // perturbation

    var anchorOnMap = mSoilMap.GetNearestPoint(mRootProps.anchor);
    if (anchorOnMap != null) {
      mRootNode = new RootNode(anchorOnMap);
    }
    mRootNode.dir = -mSoilMap.mPln.YAxis;

    //! prepare initial root and init BFS queue
    bfsQ.Clear();
    mSoilEnv.Clear();

    // container
    rootCrvMain.Clear();
    rootCrvRest.Clear();

#region initial RootCrv strategy : average angle division
    var initVecLst = new List<Vector3d>();
    var unitAng = Math.PI / (mRootProps.branchN);

    // use halfUnitAngle on the two side near soil surface. For construction, rotate 0.5 *unitAng up
    // first for convenience
    var initVec = mSoilMap.mPln.XAxis;
    initVec.Rotate(0.5 * unitAng, mSoilMap.mPln.ZAxis);
    for (int i = 0; i < mRootProps.branchN; i++) {
      var tV = initVec;
      tV.Rotate(-unitAng * (i + 1), mSoilMap.mPln.ZAxis);
      initVecLst.Add(tV);
    }

    // add the first rDen points and create initial root branches
    // for the first Iteration, no need to repect soil separates rule.
    for (int i = 0; i < mRootProps.branchN; i++) {
      var x = new RootNode(anchorOnMap + 2 * initVecLst[i] * mSoilMap.unitLen);
      mRootNode.AddChildNode(x);

      UpdateScoreMapFrom(x);
      bfsQ.Enqueue(x);

      // ! collecting initial root crv
      rootCrvMain.Add(new Line(mRootNode.pos, x.pos));
    }
#endregion

    // ! BFS starts and recursively grow roots
    while (bfsQ.Count > 0) {
      var curNode = bfsQ.Dequeue();

      //  ! stopping criteria
      if (curNode.curStep >= mRootProps.totalSteps || curNode.lifeSpan == 0)
        continue;  // skip this node, start new item in the queue

      // if touch the top surface, reverse Y direction
      if (mSoilMap.IsOnBound(curNode.pos))
        curNode.dir.Y *= -1;

      if (curNode.dir * mSoilMap.mPln.YAxis > 0.5 * curNode.dir.Length)
        curNode.dir.Y *= -1;

      // extend roots
      var nodes = GetExtendingNode(curNode);
      nodes.ForEach(x => {
        UpdateScoreMapFrom(x);

        UpdateDistMapToNode(x, multiplierD);
        UpdateGravityToNode(x, multiplierG);
        UpdatePerturbationToNode(x, multiplierP);

        x.dir.Unitize();

        // add to BFS queue
        bfsQ.Enqueue(x);

        // debug
#if DEBUG
        DebugStore.pt.Add(x.pos);
        DebugStore.vec.Add(x.dir);
#endif

        //  collecting crv with actual drawings
        mSoilEnv[Utils.PtString(x.pos)] += 1;  // record # the location is used

        // ! collecting root crv, separate main and non-main roots
        if (x.mBranchLevel > 0)
          rootCrvRest.Add(new Line(curNode.pos, x.pos));
        else
          rootCrvMain.Add(new Line(curNode.pos, x.pos));
      });
    }
  }

  public List<RootNode> GetExtendingNode(in RootNode curNode) {
    var resLst = new List<RootNode>();
    int branchingStepInterval = 3;
    double branchRad = Utils.ToRadian(45);

    // grow stem-like roots: stem/side
    curNode.dir *=
        mSoilMap.unitLen * (curNode.nType == RootNodeType.Stem ? mRnd.Next(80, 170) / 100.0 : 1);
    GrowRoot(curNode, ref resLst, curNode.nType, false);

    // grow side roots, with rotated direction
    // if (curNode.mBranchLevel >= 1)
    //{
    //  var x = 1;
    //}
    if (curNode.mBranchLevel < mMaxBranchLevel &&
        curNode.stepCounting % branchingStepInterval ==
            0)  // only stem rootNode can add side rootNode
    {
      if ((curNode.stepCounting / branchingStepInterval) % 2 == 1) {
        curNode.dir.Rotate(branchRad, mSoilMap.mPln.Normal);
        GrowRoot(curNode, ref resLst, RootNodeType.Side, true);
      } else if ((curNode.stepCounting / branchingStepInterval) % 2 == 0) {
        curNode.dir.Rotate(-branchRad, mSoilMap.mPln.Normal);
        GrowRoot(curNode, ref resLst, RootNodeType.Side, true);
      }
    }

    return resLst;
  }

  private void GrowRoot(in RootNode curNode,
                        ref List<RootNode> resLst,
                        RootNodeType nType = RootNodeType.Stem,
                        bool isBranching = false) {
    var endPt = Utils.ExtendDirByAffector(curNode.pos,
                                          curNode.dir,
                                          mSoilMap,
                                          mEnvProps.envToggle,
                                          mEnvProps.envRange,
                                          mEnvProps.envAttractor,
                                          mEnvProps.envRepeller);
    // envToggle, envDist, envAtt, envRep);

    // tricks to add randomness
    var tmpPos = mSoilMap.GetNearestPoint(endPt);
    var newNode = new RootNode(tmpPos);

    if (!(curNode.pos.DistanceToSquared(newNode.pos) >= 1e-4 && RootDensityCheck(newNode.pos)))
      return;

    curNode.AddChildNode(newNode, nType);

    int sideNodeLifeSpan = 4;
    if (isBranching) {
      newNode.mBranchLevel = curNode.mBranchLevel + 1;
      newNode.lifeSpan = sideNodeLifeSpan;
      newNode.stepCounting = 1;
    } else {
      newNode.lifeSpan = curNode.lifeSpan - 1;
    }

    resLst.Add(newNode);
  }

  protected void UpdateScoreMapFrom(in RootNode node) {
    // return;
    if (node.curStep <= 2)
      return;

    // find the nearest N pts and update score
    var scoreWeightPtStr = mSoilMap.GetNearestPointsStr(node.pos, 25);
    foreach (var pt in scoreWeightPtStr) {
      var dis = node.pos.DistanceTo(mSoilMap.ptMap[pt]);
      var score = 1 / (1 + dis);
      scoreMap[pt] += score;
    }
  }

  protected void UpdateDistMapToNode(in RootNode node, double multiplierD) {
    // return;
    if (node.curStep <= 2)
      return;

    // ! density dir: find the nearest 1.5N pts and get weighted score for vector summation
    var nearPtStr = mSoilMap.GetNearestPointsStr(node.pos, 25);

    var sumVec = Vector3d.Zero;
    foreach (var pt in nearPtStr) {
      sumVec += scoreMap[pt] * (mSoilMap.ptMap[pt] - node.pos) /
                mSoilMap.unitLen;  // weighted normalized vector
    }
    sumVec.Unitize();
    node.dir += -sumVec * multiplierD;
  }

  protected void UpdateGravityToNode(in RootNode node, double multiplierG) {
    // ! gravity dir: add factor to the growth, the more it grows, the more it is affected by
    // gravity
    double curGrowStepRatio = node.curStep / (double)mRootProps.totalSteps;
    // double gravityFactor = Utils.remap(curGrowStepRatio, 0.0, 1.0, 0.1, 0.2);
    double gravityFactor = 0.05731 * Math.Exp(3.22225 * curGrowStepRatio);

    node.dir += mDownDir * gravityFactor * multiplierG;
  }

  protected void UpdatePerturbationToNode(in RootNode node, double multiplierP) {
    //! small disturbation
    Vector3d perpDir = Vector3d.CrossProduct(mSoilMap.mPln.ZAxis, node.dir);
    double turbSign = node.dir * mSoilMap.mPln.XAxis >= 0 ? -1 : 1;
    node.dir += turbSign * multiplierP * mRnd.NextDouble() * perpDir;
  }

  // public variables
  public List<Line> rootCrvMain = new List<Line>();
  public List<Line> rootCrvRest = new List<Line>();

  // internal variables
  ConcurrentDictionary<string, double> scoreMap = new ConcurrentDictionary<string, double>();
  SoilMap2d mSoilMap = new SoilMap2d();
  RootNode mRootNode = null;
  Queue<RootNode> bfsQ = new Queue<RootNode>();

  Dictionary<string, int> mSoilEnv = new Dictionary<string, int>();
  // Point3d mAnchor = new Point3d();

  // int mSteps = 0;
  // int mBranchNum = 2;

  int mSeed = -1;
  Random mRnd;
  Vector3d mDownDir;
  int mMaxBranchLevel = 1;

  // bool envToggle = false;
  // double envDist = 0.0;
  // List<Curve> envAtt = null;
  // List<Curve> envRep = null;

  EnvProp mEnvProps = new EnvProp();
  RootProp mRootProps = new RootProp();
}

class RootPlanar {
  public RootPlanar() {}

  public RootPlanar(in SoilMap2d soilmap,
                    in Point3d anchor,
                    double scale,
                    int phase,
                    int divN,
                    in List<Curve> envA = null,
                    in List<Curve> envR = null,
                    double envRange = 0.0,
                    bool envToggle = false) {
    this.sMap = soilmap;
    this.anchor = anchor;
    this.scale = scale;
    this.phase = phase;
    this.divN = divN;

    this.envA = envA;
    this.envR = envR;
    this.envDetectingDist = envRange * sMap.unitLen;
    this.envT = envToggle;

    this.rCrv.Clear();
    this.rAbs.Clear();

    for (int i = 0; i < 6; i++) {
      rCrv.Add(new List<Line>());
      frontId.Add(new List<string>());
      frontDir.Add(new List<Vector3d>());
    }
  }

  public (List<List<Line>>, List<Line>) GrowRoot() {
    for (int i = 1; i < phase + 1; i++) {
      switch (i) {
        case 1:
          DrawPhaseCentre(0);
          break;
        case 2:
          DrawPhaseBranch(1);
          break;
        case 3:
          DrawPhaseBranch(2);
          break;
        case 4:
          DrawPhaseBranch(3);
          break;
        case 5:
          DrawPhaseExtend(4);
          break;
        default:
          break;
      }
    }

    foreach (var rLst in rCrv) {
      CreateAbsorbent(rLst);
    }

    return (rCrv, rAbs);
  }

  public void CreateAbsorbent(in List<Line> roots, int N = 5) {
    var rotAng = 40;

    var rtDir = roots.Select(x => x.Direction).ToList();

    foreach (var (ln, i) in roots.Select((ln, i) => (ln, i))) {
      if (ln.Length == 0)
        continue;

      var segL = ln.Length * 0.2;
      ln.ToNurbsCurve().DivideByCount(N, false, out Point3d[] basePt);

      var dir0 = rtDir[i];
      var dir1 = rtDir[i];

      dir0.Unitize();
      dir1.Unitize();

      dir0.Rotate(Utils.ToRadian(rotAng), sMap.mPln.Normal);
      dir1.Rotate(Utils.ToRadian(-rotAng), sMap.mPln.Normal);

      foreach (var p in basePt) {
        rAbs.Add(new Line(p, p + dir0 * segL));
        rAbs.Add(new Line(p, p + dir1 * segL));
      }
    }
  }

  protected void DrawPhaseCentre(int phaseId) {
    var ang = Math.PI * 2 / divN;
    var curLen = sMap.unitLen * scale * scaleFactor[0];

    for (int i = 0; i < divN; i++) {
      var dir = sMap.mPln.PointAt(Math.Cos(ang * i), Math.Sin(ang * i), 0) - sMap.mPln.Origin;
      BranchExtend(phaseId, anchor, dir, curLen);
    }
  }

  protected void DrawPhaseBranch(int phaseId) {
    var preId = phaseId - 1;
    var curLen = sMap.unitLen * scale * scaleFactor[phaseId];

    // for each node, divide two branches
    foreach (var (pid, i) in frontId[preId].Select((pid, i) => (pid, i))) {
      var curVec = frontDir[preId][i];
      var curPt = sMap.ptMap[pid];

      // v0, v1 are utilized
      var v0 = curVec;
      var v1 = curVec;
      v0.Rotate(Utils.ToRadian(30), sMap.mPln.Normal);
      v1.Rotate(Utils.ToRadian(-30), sMap.mPln.Normal);

      BranchExtend(phaseId, curPt, v0, curLen);
      BranchExtend(phaseId, curPt, v1, curLen);
    }
  }

  protected void DrawPhaseExtend(int phaseId) {
    var preId = phaseId - 1;

    foreach (var (pid, i) in frontId[preId].Select((pid, i) => (pid, i))) {
      // no branching, just extending
      var preVec = frontDir[preId - 1][(int)(i / 2)];
      var curVec = frontDir[preId][i];
      var curLen = sMap.unitLen * scale * scaleFactor[phaseId];
      var curPt = sMap.ptMap[pid];

      // v0, v1 are unitized
      var tmpVec = Vector3d.CrossProduct(curVec, preVec);
      var sign = tmpVec * sMap.mPln.Normal;
      var ang = (sign >= 0 ? 15 : -15);

      curVec.Rotate(Utils.ToRadian(ang), sMap.mPln.Normal);
      BranchExtend(phaseId, curPt, curVec, curLen);
    }
  }

  protected void BranchExtend(int lvId, in Point3d startP, in Vector3d dir, double L) {
    var endPtOffGrid = GrowPointWithEnvEffect(startP, dir * L);

    // record
    var ptKey2 = sMap.GetNearestPointsStr(endPtOffGrid, 2);
    var endPkey = Utils.PtString(endPtOffGrid) == ptKey2[0] ? ptKey2[1] : ptKey2[0];
    var endP = sMap.ptMap[endPkey];

    var branchLn = new Line(startP, endP);
    var unitDir = branchLn.Direction;
    unitDir.Unitize();

    // draw
    rCrv[lvId].Add(branchLn);
    frontId[lvId].Add(endPkey);
    frontDir[lvId].Add(unitDir);
  }

  /// <summary>
  /// Use environment to affect the EndPoint.
  /// If the startPt is inside any attractor / repeller area, that area will dominant the effect;
  /// Otherwise, we accumulate weighted (dist-based) effect of all the attractor/repeller area.
  /// </summary>
  protected Point3d GrowPointWithEnvEffect(in Point3d pt, in Vector3d scaledDir) {
    return Utils.ExtendDirByAffector(pt, scaledDir, sMap, envT, envDetectingDist, envA, envR);
  }

  protected SoilMap2d sMap = new SoilMap2d();
  protected Point3d anchor = new Point3d();
  readonly double scale = 1.0;
  readonly int phase = 0;
  readonly int divN = 4;

  List<Curve> envA = null;
  List<Curve> envR = null;
  double envDetectingDist = 0;
  bool envT = false;

  readonly private List<double> scaleFactor = new List<double> { 1, 1.2, 1.5, 2, 2.5 };

  List<List<Line>> rCrv = new List<List<Line>>();
  List<Line> rAbs = new List<Line>();
  List<List<string>> frontId = new List<List<string>>();
  List<List<Vector3d>> frontDir = new List<List<Vector3d>>();
}

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
  private Point3d mAnchor = new Point3d();
  double mUnitLen = 0.0;
  int mPhase = 0;
  int mDivN = 1;

  List<RootBranch> mRootMaster = new List<RootBranch>();
  List<RootBranch> mRootTap = new List<RootBranch>();
  List<RootBranch> mRootExplorer = new List<RootBranch>();
  public Point3d debugPt;

  public RootTree3D() {}

  public RootTree3D(in SoilMap3d map3d, in Point3d anchor, double unitLen, int phase, int divN) {
    this.mMap3d = map3d;
    this.mAnchor = anchor;
    this.mUnitLen = unitLen;
    this.mPhase = phase;
    this.mDivN = divN;
  }

  /// <summary>
  /// This function grows different part of the whole root structure gradually,
  /// assign them different phase range [start, end]
  /// when rootbranch in "start" phase, it falls into the "new" branch category
  /// when rootbranch in "end" phase, it falls into the "dead" branch category
  /// </summary>
  public String GrowRoot() {
    // Get the directional vector based on divN
    Plane basePln = mMap3d.mPln;
    var vecLst = new List<Vector3d>();

    // Define growth parameters
    // double maxLength = mTreeHeight * 3; // Maximum length of each root branch

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

    if (tapRoot_1.GetLength() + tapRoot_2.GetLength() > tapRootLen * 1.5) {
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

    // Iteratively grow the Master Rootsin Level 1 by bi-branching for max 3 times (Phase 3 - 5)
    List<double> lv1LengthParam = new List<double> { 0.1, 0.2, 0.3 };
    List<Polyline> frontEndRoots = new List<Polyline>(lv1HorizontalCore);
    var maxBranchLevel = Math.Min(mPhase - 2, 3);
    for (int branchLv = 0; branchLv < maxBranchLevel; branchLv++) {
      var startPhase = 3;
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

        // exploiter
        var rootExplorer = GenerateExplorationalRoots(root, 5);
        explorerRoots.AddRange(rootExplorer);
      }

      // collect the newly growed roots with phase interval
      nextLevelRoots.ForEach(
          x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 12))));
      surroundTapRoots.ForEach(
          x => mRootTap.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 11))));
      explorerRoots.ForEach(
          x => mRootExplorer.Add(new RootBranch(
              x.ToNurbsCurve(), new Interval(startPhase, Math.Min(11, mPhase + 4)))));

      // update currentLevel for the next iteration
      frontEndRoots = nextLevelRoots;
    }

    // Phase 6-8: more steps growth of explorer without branching
    maxBranchLevel = Math.Min(3, mPhase - 5);
    double lenParam = 0.4;
    for (int branchLv = 0; branchLv < maxBranchLevel; branchLv++) {
      var startPhase = 6;
      var masterColletion = new List<Polyline>();
      var exploiterCollection = new List<Polyline>();
      foreach (var root in frontEndRoots) {
        var newSegments = GrowAlongVecInSeg(root.ToNurbsCurve().PointAtEnd,
                                            mUnitLen * lenParam,
                                            root.ToNurbsCurve().TangentAtEnd,
                                            4);
        masterColletion.AddRange(newSegments);

        var newExploiter = GenerateExplorationalRoots(root, 5);
        exploiterCollection.AddRange(newExploiter);
      }

      masterColletion.ForEach(
          x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 12))));
      exploiterCollection.ForEach(
          x => mRootExplorer.Add(new RootBranch(
              x.ToNurbsCurve(), new Interval(startPhase, Math.Min(11, mPhase + 3)))));

      frontEndRoots = masterColletion;
    }
    // additional explorer of the last generate seg
    if (mPhase > 5) {
      var startPhase = 6;
      var exploiterCollection = new List<Polyline>();
      foreach (var root in frontEndRoots) {
        var newExploiter = GenerateExplorationalRoots(root, 5);
        exploiterCollection.AddRange(newExploiter);
      }

      exploiterCollection.ForEach(
          x => mRootExplorer.Add(new RootBranch(
              x.ToNurbsCurve(), new Interval(startPhase, Math.Min(12, mPhase + 4)))));
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

    // Iteratively grow the Master Rootsin Level 1 by bi-branching for max 3 times (Phase 5 - 6)
    List<double> lv2LengthParam = new List<double> { 0.1, 0.13 };
    frontEndRoots = new List<Polyline>(lv2HorizontalCore);
    maxBranchLevel = Math.Min(mPhase - 4, lv2LengthParam.Count);
    for (int branchLv = 0; branchLv < maxBranchLevel; branchLv++) {
      var startPhase = branchLv + 5;
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

        // exploiter
        var rootExplorer = GenerateExplorationalRoots(root, 3);
        explorerRoots.AddRange(rootExplorer);
      }

      // collect the newly growed roots with phase interval
      nextLevelRoots.ForEach(
          x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 10))));
      surroundTapRoots.ForEach(
          x => mRootTap.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 10))));
      explorerRoots.ForEach(
          x => mRootExplorer.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 10))));

      // update currentLevel for the next iteration
      frontEndRoots = nextLevelRoots;
    }

    // Phase 7: more steps growth of without branching
    maxBranchLevel = Math.Min(1, mPhase - 6);
    lenParam = 0.5;
    for (int branchLv = 0; branchLv < maxBranchLevel; branchLv++) {
      var startPhase = 7;
      var masterColletion = new List<Polyline>();
      var exploiterCollection = new List<Polyline>();
      foreach (var root in frontEndRoots) {
        var newSegments = GrowAlongVecInSeg(root.ToNurbsCurve().PointAtEnd,
                                            mUnitLen * lenParam,
                                            root.ToNurbsCurve().TangentAtEnd,
                                            4);
        masterColletion.AddRange(newSegments);

        var newExploiter = GenerateExplorationalRoots(root, 5);
        exploiterCollection.AddRange(newExploiter);
      }

      masterColletion.ForEach(
          x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 10))));
      exploiterCollection.ForEach(
          x => mRootExplorer.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 10))));

      frontEndRoots = masterColletion;
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
      var startPhase = 7;
      var masterColletion = new List<Polyline>();
      var exploiterCollection = new List<Polyline>();
      foreach (var root in frontEndRoots) {
        var newSegments = GrowAlongVecInSeg(root.ToNurbsCurve().PointAtEnd,
                                            mUnitLen * lenParam,
                                            root.ToNurbsCurve().TangentAtEnd,
                                            4);
        masterColletion.AddRange(newSegments);

        var newExploiter = GenerateExplorationalRoots(root, 5);
        exploiterCollection.AddRange(newExploiter);
      }

      masterColletion.ForEach(
          x => mRootMaster.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 9))));
      exploiterCollection.ForEach(
          x => mRootExplorer.Add(new RootBranch(x.ToNurbsCurve(), new Interval(startPhase, 9))));

      frontEndRoots = masterColletion;
    }

    // debug
    // debugPt = lv1RootAnchor;

    return "Success";
  }

  public List<Vector3d>
  GenerateVecLst(Plane basePln, int totalVectors, bool randomizeStart = false) {
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

  // grwoing a segment along given vector
  private Polyline GrowAlongVec(in Point3d cen, in double maxLength, in Vector3d dir) {
    int selectNum = 20;  // Number of candidate points to consider at each step
    var rootBranch = new Polyline();

    Point3d curPt = cen;
    Vector3d curDir = dir;

    rootBranch.Add(cen);
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
  private List<Polyline>
  GrowAlongVecInSeg(in Point3d cen, in double maxLength, in Vector3d dir, in int segNum) {
    List<Polyline> res = new List<Polyline>();
    var segLen = maxLength / segNum;

    Point3d startPt = cen;
    for (int i = 0; i < segNum; i++) {
      var newSeg = GrowAlongVec(startPt, segLen, dir);
      res.Add(newSeg);
    }

    return res;
  }

  private void GrowAlongDirections(in Point3d cen,
                                   in double maxLength,
                                   in List<Vector3d> vecLst,
                                   out List<Polyline> res) {
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
      // generate a single branch
      branchPoints.Add(rootCurve.PointAt(0.5));
    } else if (branchNum == 2) {
      branchPoints.Add(rootCurve.PointAt(0.3));
      branchPoints.Add(rootCurve.PointAt(0.6));
    }

    // generate directions and branch out
    // foreach (Point3d pt in branchPoints)
    for (int i = 0; i < branchPoints.Count; i++) {
      rootCurve.ClosestPoint(branchPoints[i], out double t);
      var tanVec = rootCurve.TangentAt(t);

      var sign = Math.Pow(-1, i);
      var perVec = sign * Vector3d.CrossProduct(tanVec, mMap3d.mPln.ZAxis);
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
      Vector3d horizontalPerp = Vector3d.CrossProduct(tangent, mMap3d.mPln.ZAxis);
      horizontalPerp.Unitize();

      // project the tangent vector to the horizontal plane
      tangent = Vector3d.CrossProduct(mMap3d.mPln.ZAxis, horizontalPerp);
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
    Vector3d downwardDirection = -mMap3d.mPln.ZAxis;
    return GrowAlongVec(startPoint, length, downwardDirection);
  }

  private Point3d
  SelectBestCandidate(Point3d currentPoint, List<Point3d> candidates, Vector3d currentDirection) {
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
      if (alignment > bestAlignment)  // dot product means the alignment of two vectors
      {
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
    explorationRoot.Add(
        startPt);  // needed as later we only add segments by segments excluding the first pt
    Vector3d horizontalDir = Vector3d.CrossProduct(parentRootDir, mMap3d.mPln.ZAxis);
    horizontalDir *= isReverse ? -1 : 1;

    Point3d curPt = startPt;
    var randRatio = MathUtils.remap(MathUtils.balRnd.NextDouble(), 0.0, 1.0, 0.3, 0.7);
    Vector3d curDir = 0.7 * horizontalDir + randRatio * parentRootDir;

    for (int step = 0; step < totalSteps; step++) {
      if (step >= horizontalSteps) {
        // Transition to a more downward direction
        curDir -= 0.5 * mMap3d.mPln.ZAxis;
        // curDir.Unitize();
      }

      // Grow the next segment
      Polyline segment = GrowAlongVec(curPt, stepLength, curDir);

      if (segment.Count > 1) {
        // add the new segments
        explorationRoot.AddRange(segment.GetRange(1, segment.Count - 1));
        curPt = segment.Last;
      } else {
        // If growth failed, stop the process
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
}
}
