using Rhino.Geometry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Net;

namespace BeingAliveLanguage
{
  class RootSectional
  {
    // for sectional root, we use a "TREE" structure, and BFS for constructing the "radius" of each branch
    public RootSectional()
    {

    }

    public RootSectional(in SoilMap map, in Point3d anchor,
        string rootType, in int steps = 1, in int branchNum = 2, in int seed = -1,
        in bool envToggle = false, in double envRange = 0.0,
        in List<Curve> envAtt = null, in List<Curve> envRep = null)
    {
      mSoilMap = map;
      mAnchor = anchor;
      mRootType = rootType;
      mSeed = seed;
      mSteps = steps;
      mBranchNum = branchNum;
      mRootNode = new RootNode(anchor);

      if (rootType == "none")
        mMaxBranchLevel = 0;
      else if (rootType == "single")
        mMaxBranchLevel = 1;
      else if (rootType == "multi")
        mMaxBranchLevel = 2;

      mRnd = mSeed >= 0 ? new Random(mSeed) : Utils.balRnd;
      mDownDir = -mSoilMap.mPln.YAxis;

      // init scoreMap
      Parallel.ForEach(mSoilMap.kdMap, pt => { scoreMap.TryAdd(pt.Value, 0); });

      // env param
      this.envToggle = envToggle;
      this.envDist = envRange;
      this.envAtt = envAtt;
      this.envRep = envRep;

#if DEBUG
      // debug
      DebugStore.Clear();
#endif

    }

    private bool RootDensityCheck(Point3d pt)
    {
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
    /// <param name="rSteps">
    /// growing steps
    /// </param>
    /// <param name="branchNum">
    /// Density Control
    /// </param>
    public void Grow(int rSteps, int branchNum = 2)
    {
      var anchorOnMap = mSoilMap.GetNearestPoint(mAnchor);
      if (anchorOnMap != null)
      {
        mRootNode = new RootNode(anchorOnMap);
      }
      mRootNode.dir = -mSoilMap.mPln.YAxis;

      //! prepare initial root and init BFS queue
      bfsQ.Clear();
      mSoilEnv.Clear();

      #region initial RootCrv strategy: average angle division
      var initVecLst = new List<Vector3d>();
      var unitAng = Math.PI / (branchNum);
      rootCrv.Clear();

      // use halfUnitAngle on the two side near soil surface. For construction, rotate 0.5 *unitAng up first for convenience
      var initVec = mSoilMap.mPln.XAxis;
      initVec.Rotate(0.5 * unitAng, mSoilMap.mPln.ZAxis);
      for (int i = 0; i < branchNum; i++)
      {
        var tV = initVec;
        tV.Rotate(-unitAng * (i + 1), mSoilMap.mPln.ZAxis);
        initVecLst.Add(tV);
      }

      // add the first rDen points and create initial root branches
      // for the first Iteration, no need to repect soil separates rule.
      for (int i = 0; i < branchNum; i++)
      {
        var x = new RootNode(anchorOnMap + 2 * initVecLst[i] * mSoilMap.unitLen);
        mRootNode.addChildNode(x);
        UpdateScoreMapFrom(x);
        bfsQ.Enqueue(x);

        //  collecting initial crv
        rootCrv.Add(new Line(mRootNode.pos, x.pos));
      }
      #endregion

      // ! BFS starts and recursively grow roots
      while (bfsQ.Count > 0)
      {
        var curNode = bfsQ.Dequeue();

        //  ! stopping criteria
        if (curNode.curStep >= rSteps || curNode.lifeSpan == 0)
          continue; // skip this node, start new item in the queue

        // if touch the top surface, reverse Y direction
        if (mSoilMap.IsOnBound(curNode.pos))
          curNode.dir.Y *= -1;

        if (curNode.dir * mSoilMap.mPln.YAxis > 0.5 * curNode.dir.Length)
          curNode.dir.Y *= -1;

        // extend roots
        var nodes = GetExtendingNode(curNode);
        nodes.ForEach(x =>
        {
          UpdateScoreMapFrom(x);

          UpdateDistMapToNode(x);
          UpdateGravityToNode(x);
          UpdatePerturbationToNode(x);

          x.dir.Unitize();

          // add to BFS queue
          bfsQ.Enqueue(x);

          // debug
#if DEBUG
          //DebugStore.pt.Add(x.pos);
          //DebugStore.vec.Add(x.dir);
#endif

          //  collecting crv with actual drawings
          mSoilEnv[Utils.PtString(x.pos)] += 1; // record # the location is used
          rootCrv.Add(new Line(curNode.pos, x.pos));
        });
      }
    }

    public List<RootNode> GetExtendingNode(in RootNode curNode)
    {
      var resLst = new List<RootNode>();

      var nextDir = curNode.dir;
      //! both stem and side root node can grow along current dir
      /// for side node, node has the properties:
      ///  - a life span 
      ///  - no perterbation

      nextDir.Unitize();
      nextDir *= mSoilMap.unitLen * (curNode.nType == RootNodeType.Stem ? mRnd.Next(80, 170) / 100.0 : 1);
      //nextDir *= mSoilMap.unitLen * mRnd.Next(80, 120) / 100.0;

      // grow stem-like roots: stem/side
      GrowRoot(curNode, ref resLst, curNode.nType, false);

      int branchingStepInterval = 4;

      // if rType == "multi", or the current is stem node for "signle" type, do the following
      if (curNode.mBranchLevel < mMaxBranchLevel
        && curNode.curStep >= branchingStepInterval
        && curNode.lifeSpan != 0) // only stem rootNode can add side rootNode
      {
        nextDir.Unitize();
        nextDir *= mSoilMap.unitLen; // for side node, no perterbation

        if (curNode.curStep % branchingStepInterval != 0)
          return resLst;

        if ((curNode.curStep / branchingStepInterval) % 2 == 0)
        {
          nextDir.Rotate(Utils.ToRadian(30), mSoilMap.mPln.Normal);
          GrowRoot(curNode, ref resLst, RootNodeType.Side, true);
        }
        else if ((curNode.curStep / branchingStepInterval) % 2 == 1)
        {
          nextDir.Rotate(Utils.ToRadian(-30), mSoilMap.mPln.Normal);
          GrowRoot(curNode, ref resLst, RootNodeType.Side, true);
        }
      }

      #region old multi
      //}
      //else if (rType == "multi")
      //{
      //  // direction variation + small turbulation
      //  var initDir = curNode.dir;
      //  var turbDir = Vector3d.Zero;

      //  var nextDir = curNode.dir;
      //  var nextDir2 = curNode.dir;

      //  var rnd = new Random((int)Math.Round(curNode.pos.DistanceToSquared(mSoilMap.mPln.Origin)));

      //  nextDir.Unitize();
      //  if (curNode.curStep < mSteps * 0.5) // gravity effect at the initial phases
      //    initDir += mDownDir * 0.4;
      //  else
      //    initDir += mDownDir * 0.2;

      //  initDir.Unitize();
      //  if (curNode.curStep > 2) // turbulation
      //  {
      //    turbDir = (mRnd.Next(-50, 50) / 100.0) * mSoilMap.mPln.XAxis;
      //    nextDir = initDir + turbDir;
      //  }

      //  // direction scale and extension
      //  GrowSingleRoot(curNode, nextDir, ref resLst);

      //  // ! density control: percentage control based on density param
      //  //double denParam = curNode.steps < mSteps * 0.2 ? 0.1 : 0.03;
      //  //if (mRnd.NextDouble() < mDensity * denParam)
      //  //{
      //  //  nextDir2 = initDir - turbDir;

      //  //  // direction scale and extension
      //  //  GrowSingleRoot(curNode, nextDir2, ref resLst);
      //  //}
      //}
      #endregion

      return resLst;
    }

    private void GrowRoot(in RootNode curNode, ref List<RootNode> resLst,
       RootNodeType nType = RootNodeType.Stem, bool isBranching = false)
    {
      var endPt = Utils.ExtendDirByAffector(
          curNode.pos, curNode.dir, mSoilMap,
          envToggle, envDist, envAtt, envRep);

      //tricks to add randomness
      var tmpPos = mSoilMap.GetNearestPoint(endPt);
      var newNode = new RootNode(tmpPos);

      if (!(curNode.pos.DistanceToSquared(newNode.pos) >= 0.01 && RootDensityCheck(newNode.pos)))
        return;

      curNode.addChildNode(newNode, -1, nType);

      int sideNodeLifeSpan = 6; // need to be smaller than branching interval, otherwise, roots will multiply
      if (isBranching)
      {
        newNode.mBranchLevel += 1;
        newNode.lifeSpan = sideNodeLifeSpan;
      }
      else
      {
        newNode.lifeSpan -= 1;
      }

      resLst.Add(newNode);
    }

    protected void UpdateScoreMapFrom(in RootNode node)
    {
      //return;
      if (node.curStep <= 2)
        return;

      // find the nearest N pts and update score
      var scoreWeightPtStr = mSoilMap.GetNearestPointsStr(node.pos, 25);
      foreach (var pt in scoreWeightPtStr)
      {
        var dis = node.pos.DistanceTo(mSoilMap.ptMap[pt]);
        var score = 1 / (1 + dis);
        scoreMap[pt] += score;
      }
    }

    protected void UpdateDistMapToNode(in RootNode node)
    {
      //return;
      if (node.curStep <= 2)
        return;

      // ! density dir: find the nearest 1.5N pts and get weighted score for vector summation
      var nearPtStr = mSoilMap.GetNearestPointsStr(node.pos, 25);

      var sumVec = Vector3d.Zero;
      foreach (var pt in nearPtStr)
      {
        sumVec += scoreMap[pt] * (mSoilMap.ptMap[pt] - node.pos) / mSoilMap.unitLen; // weighted normalized vector 
      }
      sumVec.Unitize();
      node.dir += -sumVec * 1.1;

    }

    protected void UpdateGravityToNode(in RootNode node)
    {
      // ! gravity dir: add factor to the growth, the more it grows, the more it is affected by gravity
      double curGrowStepRatio = node.curStep / (double)mSteps;
      //double gravityFactor = Utils.remap(curGrowStepRatio, 0.0, 1.0, 0.1, 0.2);
      double gravityFactor = 0.05731 * Math.Exp(3.22225 * curGrowStepRatio);

      node.dir += mDownDir * gravityFactor * 0.7;
    }

    protected void UpdatePerturbationToNode(in RootNode node)
    {
      //! small disturbation
      double turbSign = node.dir * mSoilMap.mPln.XAxis >= 0 ? -1 : 1;
      node.dir += turbSign * (mRnd.NextDouble() * 0.5) * mSoilMap.mPln.XAxis;
    }

    // public variables
    public List<Line> rootCrv = new List<Line>();

    // internal variables
    HashSet<string> frontKey = new HashSet<string>();
    HashSet<string> nextFrontKey = new HashSet<string>();
    ConcurrentDictionary<string, double> disMap = new ConcurrentDictionary<string, double>();
    ConcurrentDictionary<string, double> scoreMap = new ConcurrentDictionary<string, double>();
    Point3d mAnchor = new Point3d();
    RootNode mRootNode = null;
    SoilMap mSoilMap = new SoilMap();
    Queue<RootNode> bfsQ = new Queue<RootNode>();
    Dictionary<string, int> mSoilEnv = new Dictionary<string, int>();

    int mSeed = -1;
    int mSteps = 0;
    int mBranchNum = 1;
    Random mRnd;
    Vector3d mDownDir;
    int mMaxBranchLevel = 1;

    string mRootType = "single";
    bool envToggle = false;
    double envDist = 0.0;
    List<Curve> envAtt = null;
    List<Curve> envRep = null;

  }

  class RootPlanar
  {
    public RootPlanar() { }

    public RootPlanar(in SoilMap soilmap, in Point3d anchor, double scale, int phase, int divN,
        in List<Curve> envA = null, in List<Curve> envR = null, double envRange = 0.0, bool envToggle = false)
    {
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

      for (int i = 0; i < 6; i++)
      {
        rCrv.Add(new List<Line>());
        frontId.Add(new List<string>());
        frontDir.Add(new List<Vector3d>());
      }
    }

    public (List<List<Line>>, List<Line>) GrowRoot()
    {
      for (int i = 1; i < phase + 1; i++)
      {
        switch (i)
        {
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

      foreach (var rLst in rCrv)
      {
        CreateAbsorbent(rLst);
      }

      return (rCrv, rAbs);
    }

    public void CreateAbsorbent(in List<Line> roots, int N = 5)
    {
      var rotAng = 40;

      var rtDir = roots.Select(x => x.Direction).ToList();

      foreach (var (ln, i) in roots.Select((ln, i) => (ln, i)))
      {
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

        foreach (var p in basePt)
        {
          rAbs.Add(new Line(p, p + dir0 * segL));
          rAbs.Add(new Line(p, p + dir1 * segL));
        }
      }
    }

    protected void DrawPhaseCentre(int phaseId)
    {
      var ang = Math.PI * 2 / divN;
      var curLen = sMap.unitLen * scale * scaleFactor[0];

      for (int i = 0; i < divN; i++)
      {
        var dir = sMap.mPln.PointAt(Math.Cos(ang * i), Math.Sin(ang * i), 0) - sMap.mPln.Origin;
        BranchExtend(phaseId, anchor, dir, curLen);
      }
    }

    protected void DrawPhaseBranch(int phaseId)
    {
      var preId = phaseId - 1;
      var curLen = sMap.unitLen * scale * scaleFactor[phaseId];

      // for each node, divide two branches
      foreach (var (pid, i) in frontId[preId].Select((pid, i) => (pid, i)))
      {
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

    protected void DrawPhaseExtend(int phaseId)
    {
      var preId = phaseId - 1;

      foreach (var (pid, i) in frontId[preId].Select((pid, i) => (pid, i)))
      {
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

    protected void BranchExtend(int lvId, in Point3d startP, in Vector3d dir, double L)
    {
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
    protected Point3d GrowPointWithEnvEffect(in Point3d pt, in Vector3d scaledDir)
    {
      return Utils.ExtendDirByAffector(pt, scaledDir, sMap, envT, envDetectingDist, envA, envR);
    }

    protected SoilMap sMap = new SoilMap();
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