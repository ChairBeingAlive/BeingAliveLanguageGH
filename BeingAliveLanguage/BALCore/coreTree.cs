using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using System;
using System.Collections.Generic;
using System.Linq;
using MIConvexHull;
using System.ComponentModel;
using System.Windows.Forms;

namespace BeingAliveLanguage
{
  class Tree
  {
    public Tree() { }
    public Tree(Plane pln, double height, bool unitary = false)
    {
      mPln = pln;
      mHeight = height;
      mUnitary = unitary;

      maxStdR = height * 0.5;
      minStdR = height * treeSepParam * 0.5;
      stepR = (maxStdR - minStdR) / (numLayer - 1);
    }

    // draw the trees
    public (bool, string) Draw(int phase)
    {
      // record phase
      mCurPhase = phase;

      // ! draw tree trunks
      if (mHeight <= 0)
        return (false, "The height of the tree needs to be > 0.");
      if (phase > 12 || phase <= 0)
        return (false, "Phase out of range ([1, 12] for non-unitary tree).");

      var treeTrunk = new Line(mPln.Origin, mPln.Origin + mHeight * mPln.YAxis).ToNurbsCurve();
      treeTrunk.Domain = new Interval(0.0, 1.0);

      var treeBot = treeTrunk.PointAtStart;
      var treeTop = treeTrunk.PointAtEnd;

      var startRatio = 0.1;
      var seq = Utils.Range(startRatio, 1, numLayer - 1).ToList();

      List<double> remapT;
      if (mUnitary)
        remapT = seq.Select(x => Math.Sqrt(startRatio) + x / (1 - startRatio) * (1 - Math.Sqrt(startRatio))).ToList();
      else
        remapT = seq.Select(x => Math.Sqrt(x)).ToList();

      List<Curve> trunkCol = remapT.Select(x => new Line(treeBot, mPln.Origin + mHeight * x * mPln.YAxis).ToNurbsCurve() as Curve).ToList();

      mDebug = trunkCol;

      // ! draw elliptical boundary
      var vecCol = new List<Vector3d>();
      Utils.Range(48, 93, numLayer - 1).ToList().ForEach(x =>
      {
        var vec = mPln.YAxis;
        vec.Rotate(Utils.ToRadian(x), mPln.ZAxis);
        vecCol.Add(vec);
      });

      foreach (var (t, i) in trunkCol.Select((t, i) => (t, i)))
      {
        // arc as half canopy 
        var lBnd = new Arc(treeBot, vecCol[i], t.PointAtEnd).ToNurbsCurve();

        var tmpV = new Vector3d(-vecCol[i].X, vecCol[i].Y, vecCol[i].Z);
        var rBnd = new Arc(treeBot, tmpV, t.PointAtEnd).ToNurbsCurve();

        mCircCol_l.Add(lBnd);
        mCircCol_r.Add(rBnd);

        var fullBnd = Curve.JoinCurves(new List<Curve> { lBnd, rBnd }, 0.02)[0];
        mCircCol.Add(fullBnd);
      }

      // ! draw collections of branches
      // branchPts
      var branchingPt = new List<Point3d>();
      var tCol = new List<double>();

      foreach (var c in mCircCol)
      {
        var events = Intersection.CurveCurve(treeTrunk.ToNurbsCurve(), c, 0.01, 0.01);

        if (events.Count > 1)
        {
          if (mPln.Origin.DistanceTo(events[1].PointA) > 1e-3)
          {
            branchingPt.Add(events[1].PointA);
            tCol.Add(events[1].ParameterA);
          }
          else
          {
            branchingPt.Add(events[0].PointB);
            tCol.Add(events[0].ParameterB);
          }
        }
      }

      #region phase < mMatureIdx
      // ! idx determination
      var curIdx = (phase - 1) * 2;
      var trimN = mUnitary ? phase : Math.Min(phase, mMatureIdx - 1);
      var trimIdx = (trimN - 1) * 2;


      // ! canopy
      var canopyIdx = phase < mDyingIdx ? curIdx : (mDyingIdx - 2) * 2;
      mCurCanopy = mCircCol[canopyIdx];
      mCurCanopy.Domain = new Interval(0.0, 1.0);
      mCurCanopy = mCurCanopy.Trim(0.1, 0.9);

      // ! branches
      var lBranchCol = new List<Curve>();
      var rBranchCol = new List<Curve>();
      var lBranchVec = new Vector3d(mPln.YAxis);
      var rBranchVec = new Vector3d(mPln.YAxis);

      lBranchVec.Rotate(Utils.ToRadian(mOpenAngle), mPln.ZAxis);
      rBranchVec.Rotate(Utils.ToRadian(-mOpenAngle), mPln.ZAxis);

      if (mUnitary)
        mAngleStep *= 0.35;

      foreach (var (p, i) in branchingPt.Select((p, i) => (p, i)))
      {
        lBranchVec.Rotate(Utils.ToRadian(-mAngleStep * i), mPln.ZAxis);
        rBranchVec.Rotate(Utils.ToRadian(mAngleStep * i), mPln.ZAxis);

        lBranchCol.Add(new Line(p, p + 1000 * lBranchVec).ToNurbsCurve());
        rBranchCol.Add(new Line(p, p + 1000 * rBranchVec).ToNurbsCurve());
      }

      // phase out of range
      if (trimIdx >= trunkCol.Count)
        return (false, "Phase out of range ([1, 9] for unitary tree).");

      // side branches: generate the left and right separately based on scaled canopy
      var subL = lBranchCol.GetRange(0, trimIdx);
      subL.ForEach(x => x.Domain = new Interval(0.0, 1.0));
      subL = subL.Select(x => TrimCrv(x, mCurCanopy)).ToList();

      var subR = rBranchCol.GetRange(0, trimIdx);
      subR.ForEach(x => x.Domain = new Interval(0.0, 1.0));
      subR = subR.Select(x => TrimCrv(x, mCurCanopy)).ToList();

      mSideBranch_l.AddRange(subL);
      mSideBranch_r.AddRange(subR);

      mSideBranch = mSideBranch_l.Concat(mSideBranch_r).ToList();

      // trunk
      mCurTrunk = trunkCol[trimIdx];
      mCurTrunk.Domain = new Interval(0.0, 1.0);

      // trimming mCurCanopy to adapt to the current phase 
      mCurCanopy.Domain = new Interval(0.0, 1.0);
      var param = 0.1 + phase * 0.03;
      mCurCanopy = mCurCanopy.Trim(param, 1 - param);

      // ! split to left/right part for global scaling
      var canRes = mCurCanopy.Split(0.5);
      mCurCanopy_l = canRes[0];
      mCurCanopy_r = canRes[1];

      #endregion

      // branch removal at bottom part
      //if (phase > 6)
      //    mSideBranch = mSideBranch.GetRange(2);

      #region phase >= matureIdx && phase < mDyingIdx

      // top branches: if unitary, we can stop here.
      if (mUnitary)
        return (true, "");

      // - if not unitary tree, then do top branching
      if (phase >= mMatureIdx && phase < mDyingIdx)
      {
        var cPln = mPln.Clone();
        cPln.Translate(new Vector3d(mCurTrunk.PointAtEnd - mPln.Origin));
        mSubBranch_l.Clear();
        mSubBranch_r.Clear();
        mSubBranch.Clear();

        var topB = BiBranching(cPln, phase - mMatureIdx + 1);

        // do the scale transformation
        //var lSca = Transform.Scale(cPln, mScale.Item1, 1, 1);
        //lB.ForEach(x => x.Item1.Transform(lSca));

        //var rSca = Transform.Scale(cPln, mScale.Item2, 1, 1);
        //rB.ForEach(x => x.Item1.Transform(rSca));
        var lB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'l').ToList();
        var rB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'r').ToList();

        mSubBranch_l.AddRange(lB.Select(x => x.Item1));
        mSubBranch_r.AddRange(rB.Select(x => x.Item1));

        mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();
        //mSubBranch.AddRange(lB.Select(x => x.Item1));
        //mSubBranch.AddRange(rB.Select(x => x.Item1));
      }
      #endregion
      #region phase >= dyIngidx
      else if (phase >= mDyingIdx)
      {
        mSideBranch.ForEach(x => x.Domain = new Interval(0.0, 1.0));
        var cPln = mPln.Clone();

        // ! Top branching, corner case
        if (phase == mDyingIdx)
        {
          // keep top branching (Dec.2022)
          cPln.Translate(new Vector3d(mCurTrunk.PointAtEnd - mPln.Origin));
          mSubBranch.Clear();

          var topB = BiBranching(cPln, mDyingIdx - mMatureIdx);

          var lB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'l').ToList();
          var rB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'r').ToList();

          mSubBranch_l.AddRange(lB.Select(x => x.Item1));
          mSubBranch_r.AddRange(rB.Select(x => x.Item1));

          mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();
        }
        else if (phase == mDyingIdx + 1)
        {
          // for phase 11, keep only the right side of the top branch
          cPln.Translate(new Vector3d(mCurTrunk.PointAtEnd - mPln.Origin));
          mSubBranch.Clear();

          var topB = BiBranching(cPln, mDyingIdx - mMatureIdx);

          var rB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'r').ToList();
          mSubBranch_r.AddRange(rB.Select(x => x.Item1));
          mSubBranch.AddRange(rB.Select(x => x.Item1));
        }
        else
        {
          mSubBranch.Clear();
        }

        // ! Side new born branch and branched trunk
        if (phase == mDyingIdx)
        {
          mNewBornBranch = CrvSelection(mSideBranch, 0, 18, 3);
          mNewBornBranch = mNewBornBranch.Select(x => x.Trim(0.0, 0.3)).ToList();

          var babyTreeCol = new List<Curve>();
          foreach (var b in mNewBornBranch)
          {
            b.Domain = new Interval(0, 1);
            List<Point3d> ptMidEnd = new List<Point3d> { b.PointAtEnd };
            //List<Point3d> ptMidEnd = new List<Point3d> { b.PointAtEnd, b.PointAt(0.5) };

            foreach (var p in ptMidEnd)
            {
              cPln = mPln.Clone();
              cPln.Translate(new Vector3d(p - mPln.Origin));

              var cTree = new Tree(cPln, mHeight / 3.0);
              cTree.Draw(1);

              babyTreeCol.Add(cTree.mCurCanopy);
              babyTreeCol.Add(cTree.mCurTrunk);
            }
          }
          mNewBornBranch.AddRange(babyTreeCol);
        }
        else if (phase > mDyingIdx)
        {
          // base branch
          mNewBornBranch = CrvSelection(mSideBranch, 0, 16, 5);
          mNewBornBranch = mNewBornBranch.Select(x => x.Trim(0.0, 0.3)).ToList();

          // top two branches use polylinecurve
          var top2 = new List<Curve>();
          for (int i = 0; i < 4; i += 2)
          {
            var c = mNewBornBranch[i];
            c.Trim(0.0, 0.35);
            top2.Add(c);
          }

          // bottom two branches use curve
          var bot2 = new List<Curve>();
          for (int i = 1; i < 4; i += 2)
          {
            var c = mNewBornBranch[i].Trim(0.0, 0.2);
            var pt2 = c.PointAtEnd + mPln.YAxis * c.GetLength() / 2;
            var newC = NurbsCurve.Create(false, 2, new List<Point3d> { c.PointAtStart, c.PointAtEnd, pt2 });
            bot2.Add(newC);
          }

          // collect new born branches, remove canopy and side branches
          mNewBornBranch = top2.Concat(bot2).ToList();
          mCurCanopy_l = null;
          mCurCanopy_r = null;
          mCurCanopy = null;

          // create babyTree
          var babyCol = new List<Curve>();
          foreach (var b in mNewBornBranch)
          {
            cPln = mPln.Clone();
            cPln.Translate(new Vector3d(b.PointAtEnd - mPln.Origin));
            var cTree = new Tree(cPln, mHeight / 3.0);
            cTree.Draw(phase - mDyingIdx + 1);

            babyCol.Add(cTree.mCurCanopy);
            babyCol.Add(cTree.mCurTrunk);
            babyCol.AddRange(cTree.mSideBranch);

            // for debugging
            mDebug.AddRange(cTree.mCircCol);
          }

          // attach the babyTree and split the trunk
          mNewBornBranch.AddRange(babyCol);

          // process Trunk spliting.
          var leftTrunk = mCurTrunk.Duplicate() as Curve;
          leftTrunk.Translate(-mPln.XAxis * mHeight / 150);
          var rightTrunk = mCurTrunk.Duplicate() as Curve;
          rightTrunk.Translate(mPln.XAxis * mHeight / 150);
          mCurTrunk = new PolylineCurve(new List<Point3d> {
                        leftTrunk.PointAtStart, leftTrunk.PointAtEnd,
                        rightTrunk.PointAtEnd, rightTrunk.PointAtStart });
        }

        mCurCanopy_l = null;
        mCurCanopy_r = null;
        mCurCanopy = null;
        mSideBranch_l.Clear();
        mSideBranch_r.Clear();
        mSideBranch.Clear();
      }
      #endregion

      //mDebug = sideBranch;

      return (true, "");
    }

    // scale the trees so that they don't overlap
    public double CalWidth()
    {
      var bbx = mSideBranch.Select(x => x.GetBoundingBox(false)).ToList();
      var finalBbx = mCurTrunk.GetBoundingBox(false);
      bbx.ForEach(x => finalBbx.Union(x));

      // using diag to check which plane the trees are
      Vector3d diag = finalBbx.Max - finalBbx.Min;
      double wid = Vector3d.Multiply(diag, mPln.XAxis);

      return wid;
    }

    public void Scale(in Tuple<double, double> scal)
    {
      var lScal = Transform.Scale(mPln, scal.Item1, 1, 1);
      var rScal = Transform.Scale(mPln, scal.Item2, 1, 1);

      // circumBound
      mCircCol.Clear();
      for (int i = 0; i < mCircCol_l.Count; i++)
      {
        mCircCol_l[i].Transform(lScal);
        mCircCol_r[i].Transform(rScal);
        var fullBnd = Curve.JoinCurves(new List<Curve> { mCircCol_l[i], mCircCol_r[i] }, 0.01)[0];
        mCircCol.Add(fullBnd);
      }

      // side branches
      for (int i = 0; i < mSideBranch_l.Count; i++)
      {
        mSideBranch_l[i].Transform(lScal);
        mSideBranch_r[i].Transform(rScal);
      }
      mSideBranch = mSideBranch_l.Concat(mSideBranch_r).ToList();

      // subbranches
      for (int i = 0; i < mSubBranch_l.Count; i++)
      {
        mSubBranch_l[i].Transform(lScal);
        mSubBranch_r[i].Transform(rScal);
      }
      mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();

      if (mCurCanopy != null)
      {
        mCurCanopy_l.Transform(lScal);
        mCurCanopy_r.Transform(rScal);
        mCurCanopy = Curve.JoinCurves(new List<Curve> { mCurCanopy_l, mCurCanopy_r }, 0.02)[0];
      }

    }

    private List<Curve> CrvSelection(in List<Curve> lst, int startId, int endId, int step)
    {
      List<Curve> res = new List<Curve>();
      for (int i = startId; i < endId; i += step)
      {
        res.Add(lst[i]);
        //res[res.Count - 1].Domain = new Interval(0, 1);
      }

      return res;
    }

    private Curve TrimCrv(in Curve C0, in Curve C1)
    {
      var events = Intersection.CurveCurve(C0, C1, 0.01, 0.01);
      if (events.Count != 0)
        return C0.Trim(0.0, events[0].ParameterA);
      else
        return C0;
    }

    private List<Tuple<Curve, string>> BiBranching(in Plane pln, int step)
    {
      // use a string "i-j-l/r" to record the position of the branch -- end pt represent the branch
      var subPt = new List<Tuple<Point3d, string>> { Tuple.Create(pln.Origin, "n") };
      var subLn = new List<Tuple<Curve, string>> { Tuple.Create(
                new Line(pln.Origin, pln.Origin + pln.YAxis).ToNurbsCurve() as Curve, "") };
      var resCol = new List<Tuple<Curve, string>>();

      for (int i = 0; i < step; i++)
      {
        var ptCol = new List<Tuple<Point3d, string>>();
        var lnCol = new List<Tuple<Curve, string>>();

        var scalingParam = Math.Pow(0.85, (mMatureIdx - mMatureIdx + 1));
        var vecLen = mCurTrunk.GetLength() * 0.1 * scalingParam;
        var angleX = (mOpenAngle - (mMatureIdx + step) * mAngleStep * 3) * scalingParam;

        // for each phase, do bi-branching: 1 node -> 2 branches
        foreach (var (pt, j) in subPt.Select((pt, j) => (pt, j)))
        {
          var curVec = new Vector3d(subLn[j].Item1.PointAtEnd - subLn[j].Item1.PointAtStart);
          curVec.Unitize();

          var vecA = new Vector3d(curVec);
          var vecB = new Vector3d(curVec);
          vecA.Rotate(Utils.ToRadian(angleX), mPln.ZAxis);
          vecB.Rotate(-Utils.ToRadian(angleX), mPln.ZAxis);

          var endA = subPt[j].Item1 + vecA * vecLen;
          var endB = subPt[j].Item1 + vecB * vecLen;

          // trace string: trace of the trace_prevPt + trace_curPt
          ptCol.Add(Tuple.Create(endA, subPt[j].Item2 + "l"));
          ptCol.Add(Tuple.Create(endB, subPt[j].Item2 + "r"));

          lnCol.Add(Tuple.Create(new Line(pt.Item1, endA).ToNurbsCurve() as Curve, pt.Item2 + "l"));
          lnCol.Add(Tuple.Create(new Line(pt.Item1, endB).ToNurbsCurve() as Curve, pt.Item2 + "r"));
        }

        subPt = ptCol;
        subLn = lnCol;

        resCol.AddRange(subLn);
      }

      return resCol;
    }

    // tree core param
    public Plane mPln;
    public double mHeight;
    public int mCurPhase;
    bool mUnitary = false;

    double mAngleStep = 1.2;
    readonly int numLayer = 18;
    readonly double mOpenAngle = 57;

    // mature and dying range idx
    readonly int mMatureIdx = 6;
    readonly int mDyingIdx = 10;

    // other parameter
    double treeSepParam = 0.2;
    readonly double maxStdR, minStdR, stepR;

    // curve collection
    public Curve mCurCanopy_l;
    public Curve mCurCanopy_r;
    public Curve mCurCanopy;

    public Curve mCurTrunk;

    public List<Curve> mCircCol_l = new List<Curve>();
    public List<Curve> mCircCol_r = new List<Curve>();
    public List<Curve> mCircCol = new List<Curve>();

    public List<Curve> mSideBranch_l = new List<Curve>();
    public List<Curve> mSideBranch_r = new List<Curve>();
    public List<Curve> mSideBranch = new List<Curve>();

    public List<Curve> mSubBranch_l = new List<Curve>();
    public List<Curve> mSubBranch_r = new List<Curve>();
    public List<Curve> mSubBranch = new List<Curve>();

    public List<Curve> mNewBornBranch = new List<Curve>();

    public List<Curve> mDebug = new List<Curve>();

  }

  class BranchNode3D
  {
    public BranchNode3D() { }
    public BranchNode3D(int id, int phase, in Point3d node)
    {
      mNode = node;

      mID = id;
      mNodePhase = phase;
    }

    public BranchNode3D(int id, int phase, in Point3d node, bool permanent = false)
    {
      mNode = node;

      mID = id;
      mNodePhase = phase;
      flagPermanent = permanent;
    }

    public void AddBranchAlong(Vector3d vec)
    {
      var tmpLen = new Line(mNode, mNode + vec).ToNurbsCurve();
      if (tmpLen != null)
      {
        tmpLen.Domain = new Interval(0.0, 1.0);
        mBranch.Add(tmpLen);
      }
    }

    public void GrowToPhase(int phase)
    {
      int phaseDiff = phase - mNodePhase;

      if (!flagPermanent)
      {
        if (phaseDiff <= 0)
        {
          return;
        }

        if (phaseDiff <= 3)
        {
          mBranch = mBranch.Select(x => new Line(x.PointAtStart, x.PointAtStart + Math.Pow(1.2, phaseDiff) * (x.PointAtEnd - x.PointAtStart)).ToNurbsCurve() as Curve).ToList();
        }

        if (phaseDiff > 3)
        {
          mBranch.Clear();
        }
      }
    }

    public int TurnOff(Dictionary<int, HashSet<int>> branchRelation, List<BranchNode3D> allNodes)
    {
      int totalAffectedBranch = 0;
      if (flagShow == false)
      {
        return 0;
      }

      flagShow = false;
      totalAffectedBranch++;

      // recursively toggle all sub branches
      if (branchRelation.ContainsKey(mID))
      {
        foreach (var idx in branchRelation[mID])
        {
          totalAffectedBranch += allNodes[idx].TurnOff(branchRelation, allNodes);
        }
      }

      return totalAffectedBranch;
    }

    public void TogglePermanent()
    {
      flagPermanent = !flagPermanent;
    }

    public void ToggleEndNode()
    {
      flagEndNode = !flagEndNode;
    }

    public void ToggleSplitable()
    {
      flagSplittable = !flagSplittable;
    }

    public Point3d GetPos()
    {
      return mNode;
    }

    Point3d mNode = new Point3d();
    public int mNodePhase = -1;

    public int mID = -1;
    public bool flagPermanent { get; set; } = false;
    public bool flagEndNode { get; set; } = true;
    public bool flagShow = true;
    public bool flagSplittable = false;

    public List<Curve> mBranch { get; set; } = new List<Curve>();
  }

  class Tree3D
  {
    public Tree3D() { }

    public Tree3D(Plane pln, double globalScale, double trunkScale, int seed = 0, bool branchRot = false)
    {
      mPln = pln;
      mGScale = globalScale;
      mTScale = trunkScale;

      var baseLen = 10;
      mScaledLen = baseLen * mGScale;
      mRnd = new Random(seed);
      mBranchRot = branchRot;

      mMaxSideBranchLen = 0.5 * mScaledLen;
      mMinSideBranchLen = 0.25 * mScaledLen * mTScale / mStage1;
    }

    public void SetNearestDist(double dist)
    {
      mNearestTreeDist = dist;
    }

    public void SetNearestTrees(List<Point3d> nearbyTrees)
    {
      mNearestTrees = nearbyTrees;
    }

    /// <summary>
    /// The tree drawing function
    /// We have three stages of the tree:
    /// phase 1-4: young tree, growing without branching
    /// phase 5-8: mature tree, growing with branching
    /// phase 9-11: dying tree, growing with branching
    /// phase 12: dead tree, no growth
    /// </summary>
    public bool Generate(int phase, double angleMain, double angleTop, int dupNum = 0)
    {
      mPhase = phase;
      mAngleMain = angleMain;
      mAngleTop = angleTop;
      mBranchRelation.Clear();

      // guard
      dupNum = Math.Min(dupNum, 3);

      // Stage-based tree growing
      GrowStage1();

      if (mPhase > mStage1)
      {
        // lock nodes that started from phase 3 to be eternal node
        foreach (var node in mAllNode)
        {
          node.TogglePermanent();
        }

        GrowStage2(dupNum);
      }

      if (mPhase > mStage2)
      {
        GrowStage3();
      }

      if (mPhase > mStage3)
      {
        GrowStage4();
      }

      // scale 2D if tree size is too large (> 0.5 nearest tree distance)
      ForestRescale();

      return true;
    }

    public void AddNodeToTree(BranchNode3D curNode, BranchNode3D newNode)
    {
      mAllNode.Add(newNode);
      if (!mBranchRelation.ContainsKey(curNode.mID))
        mBranchRelation.Add(curNode.mID, new HashSet<int>());

      mBranchRelation[curNode.mID].Add(newNode.mID);
    }

    public void GrowStage1()
    {
      // auxiliary phase variable
      var auxPhaseS1 = Math.Min(mPhase, mStage1);

      // Grow trunk segments based on the current phase
      double segLen = mScaledLen * mTScale / mStage1;
      while (mTrunkSegments.Count < auxPhaseS1)
      {
        Point3d startPoint = mTrunkSegments.Count == 0 ? mPln.Origin : mTrunkSegments.Last().To;
        Point3d endPoint = startPoint + mPln.ZAxis * segLen;
        mTrunkSegments.Add(new Line(startPoint, endPoint));
      }

      // Update base node
      if (mBaseNode == null)
      {
        mBaseNode = new BranchNode3D(0, 0, mPln.Origin);
      }
      mBaseNode.mBranch.Clear();
      mBaseNode.AddBranchAlong(mTrunkSegments.Last().To - mTrunkSegments.First().From);
      mAllNode.Add(mBaseNode);

      mTrunkBranchNode.Clear();
      var curDir = mPln.YAxis;

      // Define branch positions for each segment
      var branchPositions = new List<double> { 0.25, 0.75 };

      // ! phase 1-4: base phase, always needed
      var totalBranchLayer = 2 * auxPhaseS1 - 1;
      var verAngleIncrement = Utils.ToRadian(mAngleMain) / (totalBranchLayer + 1);
      curDir.Rotate(verAngleIncrement, mPln.XAxis);

      // Calculate branch length
      double branchLenIncrement = (mMaxSideBranchLen - mMinSideBranchLen) / mStage1;
      var bottomBranchLen = mMinSideBranchLen + (auxPhaseS1 * branchLenIncrement);

      // Calculate branch position on the trunk
      for (int segIdx = 0; segIdx < auxPhaseS1; segIdx++)
      {
        foreach (double posRatio in branchPositions)
        {
          if (segIdx == 0 && posRatio == 0.25)
            continue; // Skip 0.25 for the first segment

          var pt = mTrunkSegments[segIdx].PointAt(posRatio);
          int curBranchLayer = segIdx * 2 + branchPositions.IndexOf(posRatio);

          for (int brNum = 0; brNum < mNumBranchPerLayer; brNum++)
          {
            var node = new BranchNode3D(mAllNode.Count, segIdx + 1, pt);

            // Calculate branch length based on position and growth
            double branchLen = (totalBranchLayer == 1 ? mMinSideBranchLen : Utils.remap(curBranchLayer, 1, totalBranchLayer, bottomBranchLen, mMinSideBranchLen));

            // Rotation in XY-plane
            double horRotRadian = Math.PI * 2 / mNumBranchPerLayer;
            curDir.Rotate(horRotRadian, mPln.ZAxis);

            node.AddBranchAlong(curDir * branchLen);
            mTrunkBranchNode.Add(node); // add the node to the trunckNode lst

            AddNodeToTree(mBaseNode, node);
          }
          // for the next layer, rotate vertically as the layers goes up
          var verRotAxis = Vector3d.CrossProduct(curDir, mPln.ZAxis);
          curDir.Rotate(verAngleIncrement, verRotAxis);

          // also rotate the starting position so that two layers don't overlap
          if (mBranchRot)
          {
            curDir.Rotate(Math.PI / mNumBranchPerLayer, mPln.ZAxis);
          }
        }
      } // end of iteratively add side branches

      // we need to add a virtual branch on the top to match the same branching mechanism as the side branches
      var topNode = new BranchNode3D(
        mAllNode.Count, auxPhaseS1, mBaseNode.mBranch[0].PointAtEnd);
      topNode.AddBranchAlong(mPln.ZAxis * 0.1);
      topNode.ToggleSplitable();

      AddNodeToTree(mBaseNode, topNode);
    }

    public void GrowStage2(int dupNum = 0)
    {
      // auxiliary phase variable
      var auxPhaseS2 = Math.Min(mPhase, mStage2);


      // ! phase 5-10: branching phase
      var numBranchPerBranch = 3;
      var splitInitLen = mScaledLen * 0.2;

      // Select nodes to branch: top nodes from previous phase and selected side branches
      for (int curPhase = mStage1 + 1; curPhase <= auxPhaseS2; curPhase++)
      {
        // Continue to grow branch emerged in Stage 1
        var addedPhase = curPhase - mStage1;
        foreach (var node in mTrunkBranchNode)
        {
          foreach (var br in node.mBranch)
          {

          }


        }


        // for each end node, branch out several new branches
        var startNodeId = mAllNode.Count;
        var nodesToBranch = mAllNode.Where(node => node.flagSplittable == true).ToList();

        // Select additional side branches to grow
        if (curPhase == mStage1 + 1)
        {
          var sideNodeToBranch = SelectTopUnbranchedNodes(dupNum);
          sideNodeToBranch.ForEach(x => x.ToggleSplitable());
          nodesToBranch.AddRange(sideNodeToBranch);
        }

        foreach (var node in nodesToBranch)
        {
          // auxilary line from Curve -> Line
          var parentLn = new Line(node.mBranch[0].PointAtStart, node.mBranch[0].PointAtEnd);

          var pt = parentLn.To;
          //var pt = node.GetPos();
          var initDir = parentLn.Direction;
          initDir.Unitize();

          // get a perpendicular vector to the current direction
          var perpVec = Vector3d.CrossProduct(initDir, (mPln.ZAxis + mPln.XAxis + mPln.YAxis));
          initDir.Rotate(Utils.ToRadian(mAngleTop), perpVec);
          initDir.Rotate(mRnd.NextDouble(), parentLn.Direction);

          for (int n = 0; n < numBranchPerBranch; n++)
          {
            initDir.Rotate(Math.PI * 2 / numBranchPerBranch, parentLn.Direction);

            // AUX: each rotation, anti-gravity growth is applied
            var auxDir = initDir;
            var auxPerpDir = Vector3d.CrossProduct(initDir, mPln.ZAxis);
            auxDir.Rotate(Math.PI * 0.05, auxPerpDir);

            var newLenth = splitInitLen * 0.67;
            var newNode = new BranchNode3D(startNodeId++, curPhase, pt);

            newNode.AddBranchAlong(auxDir * newLenth);
            newNode.ToggleSplitable();
            AddNodeToTree(node, newNode);

            // todo: not sure should do here
            newNode.TogglePermanent();
          }
          node.ToggleSplitable();
          node.ToggleEndNode();
        }
      }
    }

    public void GrowStage3()
    {
      // auxiliary phase variable
      var auxPhaseS3 = Math.Min(mPhase, mStage3);

      for (int curPhase = mStage2 + 1; curPhase <= auxPhaseS3; curPhase++)
      {
        int removeNum = (int)(mAllNode.Count * 0.3);

        int accumRm = 0;

        while (true)
        {
          var rmId = mRnd.Next(mAllNode.Count);
          accumRm += mAllNode[rmId].TurnOff(mBranchRelation, mAllNode);

          if (accumRm >= removeNum)
            break;
        }
      }
    }

    public void GrowStage4()
    {
      // auxiliary phase variable
      var auxPhaseS4 = Math.Min(mPhase, mStage4);

      // for the final stage, remove all the side branches and several main branches
      foreach (var node in mAllNode)
      {
        if (node.mNodePhase == mStage1)
        {
          if (node.mID % 2 != 0)
            node.TurnOff(mBranchRelation, mAllNode);
        }

        if (node.mNodePhase < mStage1)
        {
          node.TurnOff(mBranchRelation, mAllNode);
        }

      }
    }

    private List<BranchNode3D> SelectTopUnbranchedNodes(int count)
    {
      // Get all nodes from Stage 1
      var stage1Nodes = mTrunkBranchNode.Where(node => node.mNodePhase <= mStage1).ToList();

      // Sort them by height (Z-coordinate)
      stage1Nodes.Sort((a, b) => b.mBranch[0].PointAtEnd.Z.CompareTo(a.mBranch[0].PointAtEnd.Z));
      var topOnes = stage1Nodes.Take(mNumBranchPerLayer).ToList();

      // randommly pick count Num of branches
      var res = new HashSet<BranchNode3D>();
      while (res.Count < count)
      {
        var idx = mRnd.Next(topOnes.Count);
        res.Add(topOnes[idx]);
      }

      return res.ToList();
    }

    public void ForestRescale()
    {
      double openingAngle = 0.5 * Math.PI;

      // Collect all branches and measure the max Radius
      List<double> rCollection = new List<double>();
      foreach (var node in mAllNode)
      {
        foreach (var ln in node.mBranch)
        {
          mPln.RemapToPlaneSpace(ln.PointAtStart, out var ptStart);
          mPln.RemapToPlaneSpace(ln.PointAtEnd, out var ptEnd);

          var distA = Math.Sqrt(ptStart.X * ptStart.X + ptStart.Y * ptStart.Y);
          var distB = Math.Sqrt(ptEnd.X * ptEnd.X + ptEnd.Y * ptEnd.Y);

          rCollection.Add(distA);
          rCollection.Add(distB);
        }
      }
      var maxR = rCollection.Count > 0 ? rCollection.Max() : double.MaxValue;

      // Examine each main branch and scale if needed
      foreach (var mainBranch in mTrunkBranchNode)
      {
        // Project branch direction onto XY plane
        Vector3d branchDir = mainBranch.mBranch[0].PointAtEnd - mainBranch.mBranch[0].PointAtStart;
        Vector3d branchDir2D = new Vector3d(branchDir.X, branchDir.Y, 0);
        branchDir2D.Unitize();


        foreach (var treePt in mNearestTrees)
        {
          // Find the nearest tree within the opening angle
          double nearestDist = double.MaxValue;

          Vector3d treeDir = treePt - mPln.Origin;
          Vector3d treeDir2D = new Vector3d(treeDir.X, treeDir.Y, 0);
          treeDir2D.Unitize();

          double angle = Vector3d.VectorAngle(branchDir2D, treeDir2D);
          angle %= Math.PI;

          if (angle <= openingAngle / 2)
          {
            double dist = treeDir.Length;
            if (dist < nearestDist)
            {
              nearestDist = dist;
            }
          }

          // SCALE: when the branch if the nearest tree is smaller than 3x the branch lengths
          if (nearestDist < branchDir.Length * 3)
          {
            double scaleFactor = Math.Min(nearestDist * 0.4 / maxR, 1.0);
            ScaleBranchHierarchy(mainBranch, scaleFactor);
          }
        }
      }
    }

    private void ScaleBranchHierarchy(BranchNode3D node, double scaleFactor)
    {
      // Scale the current node's branches
      foreach (var branch in node.mBranch)
      {
        var xform = Transform.Scale(mPln, scaleFactor, scaleFactor, 1);
        branch.Transform(xform);
      }

      // Recursively scale child branches
      if (mBranchRelation.ContainsKey(node.mID))
      {
        foreach (var childId in mBranchRelation[node.mID])
        {
          var childNode = mAllNode.First(n => n.mID == childId);
          ScaleBranchHierarchy(childNode, scaleFactor);
        }
      }
    }

    public Dictionary<int, List<Curve>> GetBranch()
    {
      var branchCollection = new Dictionary<int, List<Curve>>();
      foreach (var node in mAllNode)
      {
        if (!node.flagShow)
          continue;

        if (branchCollection.ContainsKey(node.mNodePhase))
          branchCollection[node.mNodePhase].AddRange(node.mBranch);
        else
          branchCollection.Add(node.mNodePhase, node.mBranch);
      }
      return branchCollection;
    }

    public List<Curve> GetTrunk()
    {
      return mBaseNode.mBranch;
    }

    public void GetCanopyVolume(out Mesh canopyMesh)
    {
      var ptCol = new List<Point3d>();
      foreach (var node in mAllNode)
      {
        if (!node.flagShow)
          continue;

        foreach (var ln in node.mBranch)
        {
          ptCol.Add(ln.PointAtEnd);
        }
      }
      // add the first pt in brach to extend the volume
      ptCol.Add(mAllNode[0].mBranch[0].PointAtStart);

      var cvxPt = ptCol.Select(p =>
                      new DefaultVertex { Position = new[] { p.X, p.Y, p.Z } }).ToList();

      canopyMesh = new Mesh();
      var hull = ConvexHull.Create(cvxPt).Result;
      var convexHullVertices = hull.Points.ToArray();

      foreach (var pt in hull.Points)
      {
        double[] pos = pt.Position;
        canopyMesh.Vertices.Add(new Point3d(pos[0], pos[1], pos[2]));
      }

      foreach (var f in hull.Faces)
      {
        int a = Array.IndexOf(convexHullVertices, f.Vertices[0]);
        int b = Array.IndexOf(convexHullVertices, f.Vertices[1]);
        int c = Array.IndexOf(convexHullVertices, f.Vertices[2]);
        canopyMesh.Faces.AddFace(a, b, c);
      }
    }

    public void GetTrunckVolume(in int curPhase, out Mesh trunkMesh)
    {
      var trunk = this.GetTrunk()[0];
      var trunkTop = curPhase > mStage3 ? mAllNode[13].mBranch[0].PointAtStart : mAllNode[9].mBranch[0].PointAtStart;
      trunk.ClosestPoint(trunkTop, out double tTop);

      // trim trunk rail and prepair for trunk mesh generation
      var trunkRail = trunk.Trim(0.0, tTop);
      var radius = trunkRail.GetLength() * 0.2 * curPhase / 13.0;

      trunkMesh = Mesh.CreateFromCurvePipe(trunkRail, radius, 8, 1, MeshPipeCapStyle.Flat, true);
    }

    Random mRnd = new Random();
    bool mBranchRot = false;

    // tree core param
    public double mScaledLen = 10;

    public Plane mPln { get; set; }

    public int mPhase;
    public int mNumBranchPerLayer = 6;
    public double mGScale;
    public double mTScale;
    public double mAngleMain;
    public double mAngleTop;
    public double mMaxSideBranchLen;
    public double mMinSideBranchLen;

    public double mNearestTreeDist = double.MaxValue;
    public Point3d mNearestTree = new Point3d();

    // variables
    public int mStage1 = 4;
    public int mStage2 = 10;
    public int mStage3 = 12;
    public int mStage4 = 13;

    // curve collection
    BranchNode3D mBaseNode;

    public List<Point3d> mNearestTrees = new List<Point3d>();
    public Dictionary<int, HashSet<int>> mBranchRelation = new Dictionary<int, HashSet<int>>();

    // all node for branches, including the base node for trunck and all sub-nodes
    public List<BranchNode3D> mAllNode { get; set; } = new List<BranchNode3D>();

    // all nodes that are attached to the trunck, only for 1st-level branches
    public List<Line> mTrunkSegments { get; private set; } = new List<Line>();
    public List<BranchNode3D> mTrunkBranchNode { get; set; } = new List<BranchNode3D>();
    public List<string> mMmsg { get; set; } = new List<string>();
  }

}
