﻿using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using System;
using System.Collections.Generic;
using System.Linq;

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

  class TreeRoot
  {
    public TreeRoot() { }
    public TreeRoot(Plane pln, double height, ref SoilMap sMap)
    {
      mPln = pln;
      mAnchor = pln.Origin;
      mSoilMap = sMap;
    }


    // internal variables
    public Point3d mAnchor;
    private Plane mPln;
    private SoilMap mSoilMap;
  }

}