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
using Rhino.FileIO;
using System.Xml.Schema;
using Rhino.DocObjects;

namespace BeingAliveLanguage
{
  class RootSectional
  {
    // for sectional root, we use a "TREE" structure, and BFS for constructing the "radius" of each branch
    public RootSectional()
    {

    }

    public RootSectional(in SoilMap map,
      in RootProp rProps, in EnvProp eProps = null, in int seed = -1)

    //in Point3d anchor,
    //  string rootType, in int steps = 1, in int branchNum = 2, in int seed = -1,
    //  in EnvProp envP = null)
    //in bool envToggle = false, in double envRange = 0.0,
    //in List<Curve> envAtt = null, in List<Curve> envRep = null)
    {
      mSoilMap = map;

      mRootProps = rProps;
      mEnvProps = eProps;

      //mAnchor = anchor;
      //mSteps = steps;
      //mBranchNum = branchNum;
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

      //this.envToggle = envToggle;
      //this.envDist = envRange;
      //this.envAtt = envAtt;
      //this.envRep = envRep;

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
    public void Grow()
    {
      var multiplierD = 0.8; // distance control
      var multiplierG = 0.5; // gravity
      var multiplierP = 0.3; // perturbation

      var anchorOnMap = mSoilMap.GetNearestPoint(mRootProps.anchor);
      if (anchorOnMap != null)
      {
        mRootNode = new RootNode(anchorOnMap);
      }
      mRootNode.dir = -mSoilMap.mPln.YAxis;

      //! prepare initial root and init BFS queue
      bfsQ.Clear();
      mSoilEnv.Clear();

      // container
      rootCrvMain.Clear();
      rootCrvRest.Clear();

      #region initial RootCrv strategy: average angle division
      var initVecLst = new List<Vector3d>();
      var unitAng = Math.PI / (mRootProps.branchN);

      // use halfUnitAngle on the two side near soil surface. For construction, rotate 0.5 *unitAng up first for convenience
      var initVec = mSoilMap.mPln.XAxis;
      initVec.Rotate(0.5 * unitAng, mSoilMap.mPln.ZAxis);
      for (int i = 0; i < mRootProps.branchN; i++)
      {
        var tV = initVec;
        tV.Rotate(-unitAng * (i + 1), mSoilMap.mPln.ZAxis);
        initVecLst.Add(tV);
      }

      // add the first rDen points and create initial root branches
      // for the first Iteration, no need to repect soil separates rule.
      for (int i = 0; i < mRootProps.branchN; i++)
      {
        var x = new RootNode(anchorOnMap + 2 * initVecLst[i] * mSoilMap.unitLen);
        mRootNode.AddChildNode(x);

        UpdateScoreMapFrom(x);
        bfsQ.Enqueue(x);

        // ! collecting initial root crv
        rootCrvMain.Add(new Line(mRootNode.pos, x.pos));
      }
      #endregion

      // ! BFS starts and recursively grow roots
      while (bfsQ.Count > 0)
      {
        var curNode = bfsQ.Dequeue();

        //  ! stopping criteria
        if (curNode.curStep >= mRootProps.totalSteps || curNode.lifeSpan == 0)
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
          mSoilEnv[Utils.PtString(x.pos)] += 1; // record # the location is used

          // ! collecting root crv, separate main and non-main roots
          if (x.mBranchLevel > 0)
            rootCrvRest.Add(new Line(curNode.pos, x.pos));
          else
            rootCrvMain.Add(new Line(curNode.pos, x.pos));
        });
      }
    }

    public List<RootNode> GetExtendingNode(in RootNode curNode)
    {
      var resLst = new List<RootNode>();
      int branchingStepInterval = 3;
      double branchRad = Utils.ToRadian(45);

      // grow stem-like roots: stem/side
      curNode.dir *= mSoilMap.unitLen * (curNode.nType == RootNodeType.Stem ? mRnd.Next(80, 170) / 100.0 : 1);
      GrowRoot(curNode, ref resLst, curNode.nType, false);

      // grow side roots, with rotated direction 
      if (curNode.mBranchLevel >= 1)
      {
        var x = 1;
      }
      if (curNode.mBranchLevel < mMaxBranchLevel
        && curNode.stepCounting % branchingStepInterval == 0) // only stem rootNode can add side rootNode
      {
        if ((curNode.stepCounting / branchingStepInterval) % 2 == 1)
        {
          curNode.dir.Rotate(branchRad, mSoilMap.mPln.Normal);
          GrowRoot(curNode, ref resLst, RootNodeType.Side, true);
        }
        else if ((curNode.stepCounting / branchingStepInterval) % 2 == 0)
        {
          curNode.dir.Rotate(-branchRad, mSoilMap.mPln.Normal);
          GrowRoot(curNode, ref resLst, RootNodeType.Side, true);
        }
      }

      return resLst;
    }

    private void GrowRoot(in RootNode curNode, ref List<RootNode> resLst,
       RootNodeType nType = RootNodeType.Stem, bool isBranching = false)
    {
      var endPt = Utils.ExtendDirByAffector(
          curNode.pos, curNode.dir, mSoilMap,
          mEnvProps.envToggle, mEnvProps.envRange, mEnvProps.envAttractor, mEnvProps.envRepeller);
      //envToggle, envDist, envAtt, envRep);

      //tricks to add randomness
      var tmpPos = mSoilMap.GetNearestPoint(endPt);
      var newNode = new RootNode(tmpPos);

      if (!(curNode.pos.DistanceToSquared(newNode.pos) >= 1e-4 && RootDensityCheck(newNode.pos)))
        return;

      curNode.AddChildNode(newNode, nType);

      int sideNodeLifeSpan = 4;
      if (isBranching)
      {
        newNode.mBranchLevel = curNode.mBranchLevel + 1;
        newNode.lifeSpan = sideNodeLifeSpan;
        newNode.stepCounting = 1;
      }
      else
      {
        newNode.lifeSpan = curNode.lifeSpan - 1;
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

    protected void UpdateDistMapToNode(in RootNode node, double multiplierD)
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
      node.dir += -sumVec * multiplierD;

    }

    protected void UpdateGravityToNode(in RootNode node, double multiplierG)
    {
      // ! gravity dir: add factor to the growth, the more it grows, the more it is affected by gravity
      double curGrowStepRatio = node.curStep / (double)mRootProps.totalSteps;
      //double gravityFactor = Utils.remap(curGrowStepRatio, 0.0, 1.0, 0.1, 0.2);
      double gravityFactor = 0.05731 * Math.Exp(3.22225 * curGrowStepRatio);

      node.dir += mDownDir * gravityFactor * multiplierG;
    }

    protected void UpdatePerturbationToNode(in RootNode node, double multiplierP)
    {
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
    SoilMap mSoilMap = new SoilMap();
    RootNode mRootNode = null;
    Queue<RootNode> bfsQ = new Queue<RootNode>();

    Dictionary<string, int> mSoilEnv = new Dictionary<string, int>();
    //Point3d mAnchor = new Point3d();

    //int mSteps = 0;
    //int mBranchNum = 2;

    int mSeed = -1;
    Random mRnd;
    Vector3d mDownDir;
    int mMaxBranchLevel = 1;

    //bool envToggle = false;
    //double envDist = 0.0;
    //List<Curve> envAtt = null;
    //List<Curve> envRep = null;

    EnvProp mEnvProps = new EnvProp();
    RootProp mRootProps = new RootProp();

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

  class RootTree3D
  {
    private SoilMap3d mMap3d = null;
    private Point3d mAnchor = new Point3d();
    double mTreeHeight = 0.0;
    int mPhase = 0;
    int mDivN = 1;

    List<Polyline> mRootMain = new List<Polyline>();
    List<Polyline> mRootExplore = new List<Polyline>();
    List<Polyline> mRootNew = new List<Polyline>();
    List<Polyline> mRootDead = new List<Polyline>();

    public Point3d debugPt;

    public RootTree3D() { }

    public RootTree3D(in SoilMap3d map3d, in Point3d anchor, double treeHeight, int phase, int divN)
    {
      this.mMap3d = map3d;
      this.mAnchor = anchor;
      this.mTreeHeight = treeHeight;
      this.mPhase = phase;
      this.mDivN = divN;
    }

    public void GrowRoot()
    {
      // Get the directional vector based on divN
      Plane basePln = mMap3d.mPln;
      var vecLst = new List<Vector3d>();

      // Define growth parameters
      double maxLength = mTreeHeight * 3; // Maximum length of each root branch
      List<Polyline> res = new List<Polyline>();

      // tap root
      var tapRootLen = mTreeHeight * 0.4;
      var tapRoot = GrowAlongVec(mAnchor, tapRootLen, -basePln.ZAxis);
      var tapRootNrb = tapRoot.ToNurbsCurve();
      tapRootNrb.Domain = new Interval(0, 1);
      mRootMain.Add(tapRoot);


      // lv1 horizontal roots
      var lv1HorizontalRoot = new List<Polyline>();
      var lv1TapRoots = new List<Polyline>();
      Point3d lv1RootAnchor = tapRootNrb.PointAt(0.1);
      vecLst = GenerateVecLst(basePln, mDivN, true);
      GrowAlongDirections(lv1RootAnchor, mTreeHeight * 0.2, vecLst, out lv1HorizontalRoot);

      // Branch the lv1 horizontal roots
      List<Polyline> currentLevelRoots = new List<Polyline>(lv1HorizontalRoot);
      double remainingLength = mTreeHeight * 0.35; // Adjust this factor as needed

      List<double> lv1LengthParam = new List<double> { 0.2, 0.3, 0.5 };
      for (int branchLevel = 0; branchLevel < 3; branchLevel++)
      {
        List<Polyline> nextLevelRoots = new List<Polyline>();
        List<Polyline> surroundTapRoots = new List<Polyline>();
        foreach (var root in currentLevelRoots)
        {
          List<Polyline> branchedRoots = BranchRoot(root, mTreeHeight * lv1LengthParam[branchLevel], 1);
          nextLevelRoots.AddRange(branchedRoots);

          Polyline newTapRoot = GenerateTapRoot(root.ToNurbsCurve().PointAtEnd, remainingLength * 0.8);
          surroundTapRoots.Add(newTapRoot);
        }

        lv1HorizontalRoot.AddRange(nextLevelRoots);
        lv1TapRoots.AddRange(surroundTapRoots);

        currentLevelRoots = nextLevelRoots;
      }

      mRootMain.AddRange(lv1HorizontalRoot);
      mRootMain.AddRange(lv1TapRoots);

      // one more steup growth without branching
      List<Polyline> lv1HorizontalAdditional = new List<Polyline>();
      foreach (var root in currentLevelRoots)
      {
        var curSeg = GrowAlongVecInSeg(root.ToNurbsCurve().PointAtEnd, mTreeHeight * 2, root.ToNurbsCurve().TangentAtEnd, 4);
        lv1HorizontalAdditional.AddRange(curSeg);
      }
      mRootMain.AddRange(lv1HorizontalAdditional);

      // lv2 horizontal roots
      var lv2HorizontalRoot = new List<Polyline>();
      Point3d lv2RootAnchor = tapRootNrb.PointAt(0.5);
      vecLst = GenerateVecLst(basePln, mDivN - 1, true);
      GrowAlongDirections(lv2RootAnchor, maxLength * 0.2, vecLst, out lv2HorizontalRoot);
      mRootMain.AddRange(lv2HorizontalRoot);


      // lv3 horizontal roots
      var lv3HorizontalRoot = new List<Polyline>();
      Point3d lv3RootAnchor = tapRootNrb.PointAt(0.9);
      vecLst = GenerateVecLst(basePln, mDivN - 2, true);
      GrowAlongDirections(lv3RootAnchor, maxLength * 0.1, vecLst, out lv3HorizontalRoot);
      mRootMain.AddRange(lv3HorizontalRoot);


      // exploration roots
      List<Polyline> allExplorationalRoots = new List<Polyline>();

      mRootExplore.Clear();
      // Generate explorational roots for lv1HorizontalRoot
      foreach (Polyline root in lv1HorizontalRoot)
      {
        mRootExplore.AddRange(GenerateExplorationalRoots(root, 10));
      }

      // Generate explorational roots for lv2HorizontalRoot
      foreach (Polyline root in lv2HorizontalRoot)
      {
        mRootExplore.AddRange(GenerateExplorationalRoots(root, 7));
      }

      // Generate explorational roots for lv3HorizontalRoot
      foreach (Polyline root in lv3HorizontalRoot)
      {
        mRootExplore.AddRange(GenerateExplorationalRoots(root, 5));
      }


      // debug
      debugPt = lv1RootAnchor;

    }

    public List<Vector3d> GenerateVecLst(Plane basePln, int totalVectors, bool randomizeStart = false)
    {
      var vecLst = new List<Vector3d>();
      double angleIncrement = Math.PI * 2 / totalVectors;
      double startAngle = 0.0;

      // Randomize start angle if requested
      if (randomizeStart)
      {
        Random rand = new Random();
        startAngle = rand.NextDouble() * Math.PI * 2;
      }

      for (int i = 0; i < totalVectors; i++)
      {
        double theta = startAngle + (i * angleIncrement);
        Vector3d baseVec = basePln.XAxis * Math.Cos(theta) + basePln.YAxis * Math.Sin(theta);
        vecLst.Add(baseVec);
      }

      return vecLst;
    }

    private Polyline GrowAlongVec(in Point3d cen, in double maxLength, in Vector3d dir)
    {
      int candidatePtNum = 20; // Number of candidate points to consider at each step
      Polyline rootBranch = new Polyline();

      Point3d currentPoint = cen;
      Vector3d currentDirection = dir;
      string currentPointStr = mMap3d.GetNearestPointStr(currentPoint);

      rootBranch.Add(cen);
      while (rootBranch.Length < maxLength)
      {
        // Get candidate points
        List<Point3d> candidates = mMap3d.GetNearestPoints(currentPoint, candidatePtNum);

        // Select the best candidate based on direction similarity
        Point3d nextPt = SelectBestCandidate(currentPoint, candidates, currentDirection);

        // Calculate the direction to the best candidate
        Vector3d dirToNextPt = nextPt - currentPoint;
        dirToNextPt.Unitize();

        // Move to the next point
        currentPoint = nextPt;
        rootBranch.Add(nextPt);
      }

      return rootBranch;
    }

    private List<Polyline> GrowAlongVecInSeg(in Point3d cen, in double maxLength, in Vector3d dir, in int segNum)
    {
      List<Polyline> res = new List<Polyline>();
      var segLen = maxLength / segNum;

      Point3d startPt = cen;
      for (int i = 0; i < segNum; i++)
      {
        var newSeg = GrowAlongVec(startPt, segLen, dir);
        res.Add(newSeg);
      }

      return res;
    }

    private void GrowAlongDirections(in Point3d cen, in double maxLength, in List<Vector3d> vecLst, out List<Polyline> res)
    {
      res = new List<Polyline>();

      // Grow roots along each direction
      foreach (Vector3d direction in vecLst)
      {
        res.Add(GrowAlongVec(cen, maxLength, direction));
      }
    }

    private List<Polyline> BranchRoot(Polyline root, double remainingLength, int branchCount)
    {
      List<Polyline> branches = new List<Polyline>();
      NurbsCurve rootCurve = root.ToNurbsCurve();
      rootCurve.Domain = new Interval(0, 1);

      for (int i = 0; i < branchCount; i++)
      {
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

    private Polyline GenerateTapRoot(Point3d startPoint, double length)
    {
      Vector3d downwardDirection = -mMap3d.mPln.ZAxis;
      return GrowAlongVec(startPoint, length, downwardDirection);
    }

    private Point3d SelectBestCandidate(Point3d currentPoint, List<Point3d> candidates, Vector3d currentDirection)
    {
      double bestAlignment = -1;
      Point3d bestCandidate = currentPoint;

      foreach (Point3d pt in candidates)
      {
        Vector3d toCandidate = pt - currentPoint;
        if (toCandidate.Length < 1e-3)
          continue;

        toCandidate.Unitize();

        double alignment = Vector3d.Multiply(currentDirection, toCandidate);
        if (alignment > bestAlignment) // dot product means the alignment of two vectors
        {
          bestAlignment = alignment;
          bestCandidate = pt;
        }
      }

      return bestCandidate;
    }

    private List<Point3d> GeneratePointsAlongPolyline(Polyline polyline, int pointCount)
    {
      List<Point3d> points = new List<Point3d>();
      NurbsCurve curve = polyline.ToNurbsCurve();
      curve.Domain = new Interval(0, 1);

      for (int i = 0; i < pointCount; i++)
      {
        double t = (double)i / (pointCount - 1);
        points.Add(curve.PointAt(t));
      }

      return points;
    }

    private Polyline GrowSingleExplorationalRoot(Point3d startPoint, Vector3d mainRootDirection, double length, bool isReverse)
    {
      const int totalSteps = 3;
      const int horizontalSteps = 1;
      double stepLength = length / totalSteps;

      Polyline explorationRoot = new Polyline();
      explorationRoot.Add(startPoint);

      Vector3d horizontalDir = Vector3d.CrossProduct(mainRootDirection, mMap3d.mPln.ZAxis);
      if (isReverse)
        horizontalDir = -horizontalDir;
      horizontalDir.Unitize();

      Vector3d currentDirection = horizontalDir;
      Point3d currentPoint = startPoint;

      for (int step = 0; step < totalSteps; step++)
      {
        if (step == horizontalSteps)
        {
          // Transition to a more downward direction
          currentDirection = (horizontalDir + mMap3d.mPln.ZAxis * -2) / 3;
          currentDirection.Unitize();
        }

        // Add some randomness to the direction
        Vector3d randomVector = Utils.GenerateRandomVector3d();
        Vector3d growthDirection = currentDirection * 0.8 + randomVector * 0.2;
        growthDirection.Unitize();

        // Grow the next segment
        Polyline segment = GrowAlongVec(currentPoint, stepLength, growthDirection);

        if (segment.Count > 1)
        {
          explorationRoot.AddRange(segment.GetRange(1, segment.Count - 1));
          currentPoint = segment.Last;
        }
        else
        {
          // If growth failed, stop the process
          break;
        }
      }

      return explorationRoot;
    }

    private List<Polyline> GenerateExplorationalRoots(Polyline mainRoot, int pointCount)
    {
      List<Polyline> explorationalRoots = new List<Polyline>();
      List<Point3d> points = GeneratePointsAlongPolyline(mainRoot, pointCount);

      NurbsCurve mainRootCurve = mainRoot.ToNurbsCurve();
      mainRootCurve.Domain = new Interval(0, 1);

      for (int i = 0; i < points.Count; i++)
      {
        Point3d point = points[i];
        mainRootCurve.ClosestPoint(point, out double parameter);
        Vector3d mainRootDirection = mainRootCurve.TangentAt(parameter);
        mainRootDirection.Unitize();

        double explorationLength = mainRoot.Length * 0.1;

        // Grow two explorational roots in opposite directions
        explorationalRoots.Add(GrowSingleExplorationalRoot(point, mainRootDirection, explorationLength, false));
        explorationalRoots.Add(GrowSingleExplorationalRoot(point, mainRootDirection, explorationLength, true));
      }

      return explorationalRoots;
    }

    public List<Polyline> GetRootMain()
    {
      return this.mRootMain;
    }

    public List<Polyline> GetRootExplore()
    {
      return this.mRootExplore;
    }


  }
}
