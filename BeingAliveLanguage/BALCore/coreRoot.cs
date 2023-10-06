using System;
using System.Linq;
using System.Collections.Generic;
using Rhino.Geometry;
using System.Collections.Concurrent;

namespace BeingAliveLanguage
{
  class RootSectional
  {
    // for sectional root, we use a "TREE" structure, and BFS for constructing the "radius" of each branch
    public RootSectional()
    {

    }

    public RootSectional(in SoilMap map, in Point3d anchor,
        string rootType, in int steps = 1, in int density = 2, in int seed = -1,
        in bool envToggle = false, in double envRange = 0.0,
        in List<Curve> envAtt = null, in List<Curve> envRep = null)
    {
      mSoilMap = map;
      mAnchor = anchor;
      mRootType = rootType;
      mSeed = seed;
      mSteps = steps;
      mDensity = density;
      mRootNode = new MapNode(anchor);

      mRnd = mSeed >= 0 ? new Random(mSeed) : Utils.balRnd;
      mDownDir = -mSoilMap.mPln.YAxis;

      // env param
      this.envToggle = envToggle;
      this.envDist = envRange;
      this.envAtt = envAtt;
      this.envRep = envRep;
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

    private void GrowSingleRoot(in MapNode curNode, Vector3d nextDir, ref List<MapNode> resLst)
    {
      // direction scale and extension
      nextDir.Unitize();
      if (Vector3d.Multiply(nextDir, mDownDir) < 1e-2)
      {
        nextDir += mDownDir * 0.3;
      }

      nextDir *= mSoilMap.unitLen * (mRnd.Next(80, 170) / 100.0);

      var endPt = Utils.ExtendDirByAffector(
          curNode.pos, nextDir, mSoilMap,
          envToggle, envDist, envAtt, envRep);

      //tricks to add randomness
      var tmpPos = mSoilMap.GetNearestPoint(endPt);
      resLst.Add(new MapNode(tmpPos));
    }

    public List<MapNode> extendRoot(in MapNode curNode, in Vector3d dir, in string rType = "single")
    {
      var resLst = new List<MapNode>();
      double denParam = curNode.steps < mSteps * 0.2 ? 0.1 : 0.03;

      if (rType == "single")
      {
        // direction variation + small turbulation
        var nextDir = dir;
        nextDir.Unitize();
        if (curNode.steps < mSteps * 0.5) // gravity effect at the initial phases
          nextDir += mDownDir * 0.7;
        else
          nextDir += mDownDir * 0.3;

        nextDir.Unitize();
        var rnd = new Random((int)Math.Round(curNode.pos.DistanceToSquared(mSoilMap.mPln.Origin)));
        if (curNode.steps > 3) // turbulation
        {
          nextDir += (rnd.Next(-50, 50) / 100.0) * mSoilMap.mPln.XAxis;
        }

        // direction scale and extension
        GrowSingleRoot(curNode, nextDir, ref resLst);
      }
      else if (rType == "multi")
      {
        // direction variation + small turbulation
        var initDir = dir;
        var turbDir = Vector3d.Zero;

        var nextDir = dir;
        var nextDir2 = dir;

        var rnd = new Random((int)Math.Round(curNode.pos.DistanceToSquared(mSoilMap.mPln.Origin)));

        nextDir.Unitize();
        if (curNode.steps < mSteps * 0.5) // gravity effect at the initial phases
          initDir += mDownDir * 0.4;
        else
          initDir += mDownDir * 0.2;

        initDir.Unitize();
        if (curNode.steps > 2) // turbulation
        {
          turbDir = (rnd.Next(-50, 50) / 100.0) * mSoilMap.mPln.XAxis;
          nextDir = initDir + turbDir;
        }

        // direction scale and extension
        GrowSingleRoot(curNode, nextDir, ref resLst);

        // ! density control: percentage control based on density param
        if (mRnd.NextDouble() < mDensity * denParam)
        {
          nextDir2 = initDir - turbDir;

          // direction scale and extension
          GrowSingleRoot(curNode, nextDir2, ref resLst);
        }
      }

      return resLst;
    }

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
        mRootNode = new MapNode(anchorOnMap);

      //! prepare initial root and init BFS queue
      bfsQ.Clear();
      mSoilEnv.Clear();

      mRootNode.dir = -mSoilMap.mPln.YAxis * mSoilMap.unitLen * 2;

      #region initial root branches
      //! get a bunch of closest points and sort it based on the angle with "down vector"
      var pts = mSoilMap.GetNearestPoints(mRootNode.pos, branchNum + 10).ToArray();
      var ptLoc = new List<Point3d>();
      var ptAng = new List<double>();
      foreach (var p in pts)
      {
        var vec = p - mRootNode.pos;
        vec.Unitize();

        var sign = Vector3d.CrossProduct(vec, mDownDir).Z > 0 ? 1 : -1;
        var prod = Vector3d.Multiply(vec, mDownDir) * sign;

        if (prod != 0)
        {
          ptLoc.Add(p);
          ptAng.Add(Math.Round(prod, 3));
        }
      }

      // sort the points based on angle
      var arrLoc = ptLoc.ToArray();
      var arrAng = ptAng.ToArray();
      Array.Sort(arrAng, arrLoc);

      var distinctAngle = arrAng.Distinct().ToArray();
      var distinctPt = distinctAngle.Select(x => arrLoc[arrAng.ToList().IndexOf(x)]).ToList();

      // pick pts from the two sides
      var pickedPt = new List<Point3d>();
      for (int i = 0; i < branchNum; i++)
      {
        if (i % 2 == 0)
          pickedPt.Add(distinctPt[i / 2]);
        else
          pickedPt.Add(distinctPt[distinctPt.Count - (i / 2 + 1)]);
      }

      //! add the first rDen points and create initial root branches
      for (int i = 0; i < branchNum; i++)
      {
        var x = new MapNode(pickedPt[i]);
        mRootNode.addChildNode(x, mSoilMap.unitLen);
        bfsQ.Enqueue(x);

        //  collecting initial crv
        rootCrv.Add(new Line(mRootNode.pos, x.pos));
      }
      #endregion


      #region alternative RootCrv strategy
      var initVecLst = new List<Vector3d>();
      var unitAng = Math.PI / (branchNum);

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
        var x = new MapNode(anchorOnMap + initVecLst[i] * mSoilMap.unitLen);
        mRootNode.addChildNode(x, mSoilMap.unitLen);
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
        if (curNode.steps >= rSteps || mSoilMap.IsOnBound(curNode.pos))
        {
          continue; // skip this node, start new item in the queue
        }

        var nextDir = curNode.dir;

        // extend roots
        var nodes = extendRoot(curNode, nextDir, mRootType);
        nodes.ForEach(x =>
        {
          if (curNode.pos.DistanceToSquared(x.pos) >= 0.01 && RootDensityCheck(x.pos))
          {

            curNode.addChildNode(x, mSoilMap.unitLen);
            bfsQ.Enqueue(x);

            //  collecting crv
            mSoilEnv[Utils.PtString(x.pos)] += 1; // record # the location is used
            rootCrv.Add(new Line(curNode.pos, x.pos));
          }
        });
      }

    }

    // rootTyle: 0 - single, 1 - multi(branching)
    // ! deprecated: archived function, only for record purpose. Remove in v0.7.
    public void GrowRoot(double radius, int rDen = 2)
    {
      // init starting ptKey
      var anchorOnMap = mSoilMap.GetNearestPointsStr(mAnchor, 1)[0];
      if (anchorOnMap != null)
        frontKey.Add(anchorOnMap);

      // build a distance map from anchor point
      // using euclidian distance, not grid distance for ease
      disMap.Clear();
      foreach (var pt in mSoilMap.ptMap)
      {
        disMap[pt.Key] = pt.Value.DistanceTo(mAnchor);
      }

      // grow root until given radius is reached
      double curR = 0;
      double aveR = 0;

      int branchNum;
      switch (mRootType)
      {
        case "single":
          branchNum = 1;
          break;
        case "multi":
          branchNum = 2;
          break;
        default:
          branchNum = 1;
          break;
      }

      // TODO: change to "while"?
      for (int i = 0; i < 5000; i++)
      {
        if (frontKey.Count == 0 || curR >= radius)
          break;

        // pop the first element
        var rndIdx = Utils.balRnd.Next(0, frontKey.Count()) % frontKey.Count;
        var startPt = frontKey.ElementAt(rndIdx);
        frontKey.Remove(startPt);
        nextFrontKey.Clear();

        // use this element as starting point, grow roots
        int branchCnt = 0;
        for (int j = 0; j < 20; j++)
        {
          if (branchCnt >= branchNum)
            break;

          // the GetNextPointAndDistance guarantee grow downwards
          var (dis, nextPt) = mSoilMap.GetNextPointAndDistance(in startPt);
          if (nextFrontKey.Add(nextPt))
          {
            rootCrv.Add(new Line(mSoilMap.GetPoint(startPt), mSoilMap.GetPoint(nextPt)));
            curR = disMap[nextPt] > curR ? disMap[nextPt] : curR;

            branchCnt += 1;
          }
        }

        frontKey.UnionWith(nextFrontKey);
        var disLst = frontKey.Select(x => disMap[x]).ToList();
        disLst.Sort();
        aveR = disLst[(disLst.Count() - 1) / 2];
      }
    }

    // public variables
    public List<Line> rootCrv = new List<Line>();

    // internal variables
    HashSet<string> frontKey = new HashSet<string>();
    HashSet<string> nextFrontKey = new HashSet<string>();
    ConcurrentDictionary<string, double> disMap = new ConcurrentDictionary<string, double>();
    Point3d mAnchor = new Point3d();
    MapNode mRootNode = null;
    SoilMap mSoilMap = new SoilMap();
    Queue<MapNode> bfsQ = new Queue<MapNode>();
    Dictionary<string, int> mSoilEnv = new Dictionary<string, int>();

    int mSeed = -1;
    int mSteps = 0;
    int mDensity = 1;
    Random mRnd;
    Vector3d mDownDir;

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