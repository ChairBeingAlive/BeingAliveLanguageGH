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
}
