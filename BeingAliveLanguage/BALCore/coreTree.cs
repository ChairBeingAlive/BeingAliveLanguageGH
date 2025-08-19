using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using System;
using System.Collections.Generic;
using System.Linq;
using MIConvexHull;

namespace BeingAliveLanguage {
  //  class Tree2D {
  //    public Tree2D() {}
  //    public Tree2D(Plane pln, double height, bool unitary = false) {
  //      mPln = pln;
  //      mHeight = height;
  //      mUnitary = unitary;

  //      maxStdR = height * 0.5;
  //      minStdR = height * treeSepParam * 0.5;
  //      stepR = (maxStdR - minStdR) / (numLayer - 1);
  //    }

  //    // draw the trees
  //    public (bool, string) Draw(int phase) {
  //      // record phase
  //      mCurPhase = phase;

  //      // ! draw tree trunks
  //      if (mHeight <= 0)
  //        return (false, "The height of the tree needs to be > 0.");
  //      if (phase > 12 || phase <= 0)
  //        return (false, "Phase out of range ([1, 12] for non-unitary tree).");

  //      var treeTrunk = new Line(mPln.Origin, mPln.Origin + mHeight * mPln.YAxis).ToNurbsCurve();
  //      treeTrunk.Domain = new Interval(0.0, 1.0);

  //      var treeBot = treeTrunk.PointAtStart;
  //      var treeTop = treeTrunk.PointAtEnd;

  //      var startRatio = 0.1;
  //      var seq = Utils.Range(startRatio, 1, numLayer - 1).ToList();

  //      List<double> remapT;
  //      if (mUnitary)
  //        remapT = seq.Select(x => Math.Sqrt(startRatio) +
  //                                 x / (1 - startRatio) * (1 - Math.Sqrt(startRatio)))
  //                     .ToList();
  //      else
  //        remapT = seq.Select(x => Math.Sqrt(x)).ToList();

  //      List<Curve> trunkCol =
  //          remapT
  //              .Select(x => new Line(treeBot, mPln.Origin + mHeight * x * mPln.YAxis)
  //                               .ToNurbsCurve() as Curve)
  //              .ToList();

  //      mDebug = trunkCol;

  //      // ! draw elliptical boundary
  //      var vecCol = new List<Vector3d>();
  //      Utils.Range(48, 93, numLayer - 1).ToList().ForEach(x => {
  //        var vec = mPln.YAxis;
  //        vec.Rotate(Utils.ToRadian(x), mPln.ZAxis);
  //        vecCol.Add(vec);
  //      });

  //      foreach (var (t, i) in trunkCol.Select((t, i) => (t, i))) {
  //        // arc as half canopy
  //        var lBnd = new Arc(treeBot, vecCol[i], t.PointAtEnd).ToNurbsCurve();

  //        var tmpV = new Vector3d(-vecCol[i].X, vecCol[i].Y, vecCol[i].Z);
  //        var rBnd = new Arc(treeBot, tmpV, t.PointAtEnd).ToNurbsCurve();

  //        mCircCol_l.Add(lBnd);
  //        mCircCol_r.Add(rBnd);

  //        var fullBnd = Curve.JoinCurves(new List<Curve> { lBnd, rBnd }, 0.02)[0];
  //        mCircCol.Add(fullBnd);
  //      }

  //      // ! draw collections of branches
  //      // branchPts
  //      var branchingPt = new List<Point3d>();
  //      var tCol = new List<double>();

  //      foreach (var c in mCircCol) {
  //        var events = Intersection.CurveCurve(treeTrunk.ToNurbsCurve(), c, 0.01, 0.01);

  //        if (events.Count > 1) {
  //          if (mPln.Origin.DistanceTo(events[1].PointA) > 1e-3) {
  //            branchingPt.Add(events[1].PointA);
  //            tCol.Add(events[1].ParameterA);
  //          } else {
  //            branchingPt.Add(events[0].PointB);
  //            tCol.Add(events[0].ParameterB);
  //          }
  //        }
  //      }

  // #region phase < mMatureIdx
  //       // ! idx determination
  //       var curIdx = (phase - 1) * 2;
  //       var trimN = mUnitary ? phase : Math.Min(phase, mMatureIdx - 1);
  //       var trimIdx = (trimN - 1) * 2;

  //      // ! canopy
  //      var canopyIdx = phase < mDyingIdx ? curIdx : (mDyingIdx - 2) * 2;
  //      mCurCanopy = mCircCol[canopyIdx];
  //      mCurCanopy.Domain = new Interval(0.0, 1.0);
  //      mCurCanopy = mCurCanopy.Trim(0.1, 0.9);

  //      // ! branches
  //      var lBranchCol = new List<Curve>();
  //      var rBranchCol = new List<Curve>();
  //      var lBranchVec = new Vector3d(mPln.YAxis);
  //      var rBranchVec = new Vector3d(mPln.YAxis);

  //      lBranchVec.Rotate(Utils.ToRadian(mOpenAngle), mPln.ZAxis);
  //      rBranchVec.Rotate(Utils.ToRadian(-mOpenAngle), mPln.ZAxis);

  //      if (mUnitary)
  //        mAngleStep *= 0.35;

  //      foreach (var (p, i) in branchingPt.Select((p, i) => (p, i))) {
  //        lBranchVec.Rotate(Utils.ToRadian(-mAngleStep * i), mPln.ZAxis);
  //        rBranchVec.Rotate(Utils.ToRadian(mAngleStep * i), mPln.ZAxis);

  //        lBranchCol.Add(new Line(p, p + 1000 * lBranchVec).ToNurbsCurve());
  //        rBranchCol.Add(new Line(p, p + 1000 * rBranchVec).ToNurbsCurve());
  //      }

  //      // phase out of range
  //      if (trimIdx >= trunkCol.Count)
  //        return (false, "Phase out of range ([1, 9] for unitary tree).");

  //      // side branches: generate the left and right separately based on scaled canopy
  //      var subL = lBranchCol.GetRange(0, trimIdx);
  //      subL.ForEach(x => x.Domain = new Interval(0.0, 1.0));
  //      subL = subL.Select(x => TrimCrv(x, mCurCanopy)).ToList();

  //      var subR = rBranchCol.GetRange(0, trimIdx);
  //      subR.ForEach(x => x.Domain = new Interval(0.0, 1.0));
  //      subR = subR.Select(x => TrimCrv(x, mCurCanopy)).ToList();

  //      mSideBranch_l.AddRange(subL);
  //      mSideBranch_r.AddRange(subR);

  //      mSideBranch = mSideBranch_l.Concat(mSideBranch_r).ToList();

  //      // trunk
  //      mCurTrunk = trunkCol[trimIdx];
  //      mCurTrunk.Domain = new Interval(0.0, 1.0);

  //      // trimming mCurCanopy to adapt to the current phase
  //      mCurCanopy.Domain = new Interval(0.0, 1.0);
  //      var param = 0.1 + phase * 0.03;
  //      mCurCanopy = mCurCanopy.Trim(param, 1 - param);

  //      // ! split to left/right part for global scaling
  //      var canRes = mCurCanopy.Split(0.5);
  //      mCurCanopy_l = canRes[0];
  //      mCurCanopy_r = canRes[1];

  // #endregion

  //      // branch removal at bottom part
  //      // if (phase > 6)
  //      //    mSideBranch = mSideBranch.GetRange(2);

  // #region phase >= matureIdx && phase < mDyingIdx

  //      // top branches: if unitary, we can stop here.
  //      if (mUnitary)
  //        return (true, "");

  //      // - if not unitary tree, then do top branching
  //      if (phase >= mMatureIdx && phase < mDyingIdx) {
  //        var cPln = mPln.Clone();
  //        cPln.Translate(new Vector3d(mCurTrunk.PointAtEnd - mPln.Origin));
  //        mSubBranch_l.Clear();
  //        mSubBranch_r.Clear();
  //        mSubBranch.Clear();

  //        var topB = BiBranching(cPln, phase - mMatureIdx + 1);

  //        // do the scale transformation
  //        // var lSca = Transform.Scale(cPln, mScale.Item1, 1, 1);
  //        // lB.ForEach(x => x.Item1.Transform(lSca));

  //        // var rSca = Transform.Scale(cPln, mScale.Item2, 1, 1);
  //        // rB.ForEach(x => x.Item1.Transform(rSca));
  //        var lB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'l').ToList();
  //        var rB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'r').ToList();

  //        mSubBranch_l.AddRange(lB.Select(x => x.Item1));
  //        mSubBranch_r.AddRange(rB.Select(x => x.Item1));

  //        mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();
  //        // mSubBranch.AddRange(lB.Select(x => x.Item1));
  //        // mSubBranch.AddRange(rB.Select(x => x.Item1));
  //      }
  // #endregion
  // #region phase >= dyIngidx
  //      else if (phase >= mDyingIdx) {
  //        mSideBranch.ForEach(x => x.Domain = new Interval(0.0, 1.0));
  //        var cPln = mPln.Clone();

  //        // ! Top branching, corner case
  //        if (phase == mDyingIdx) {
  //          // keep top branching (Dec.2022)
  //          cPln.Translate(new Vector3d(mCurTrunk.PointAtEnd - mPln.Origin));
  //          mSubBranch.Clear();

  //          var topB = BiBranching(cPln, mDyingIdx - mMatureIdx);

  //          var lB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'l').ToList();
  //          var rB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'r').ToList();

  //          mSubBranch_l.AddRange(lB.Select(x => x.Item1));
  //          mSubBranch_r.AddRange(rB.Select(x => x.Item1));

  //          mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();
  //        } else if (phase == mDyingIdx + 1) {
  //          // for phase 11, keep only the right side of the top branch
  //          cPln.Translate(new Vector3d(mCurTrunk.PointAtEnd - mPln.Origin));
  //          mSubBranch.Clear();

  //          var topB = BiBranching(cPln, mDyingIdx - mMatureIdx);

  //          var rB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'r').ToList();
  //          mSubBranch_r.AddRange(rB.Select(x => x.Item1));
  //          mSubBranch.AddRange(rB.Select(x => x.Item1));
  //        } else {
  //          mSubBranch.Clear();
  //        }

  //        // ! Side new born branch and branched trunk
  //        if (phase == mDyingIdx) {
  //          mNewBornBranch = CrvSelection(mSideBranch, 0, 18, 3);
  //          mNewBornBranch = mNewBornBranch.Select(x => x.Trim(0.0, 0.3)).ToList();

  //          var babyTreeCol = new List<Curve>();
  //          foreach (var b in mNewBornBranch) {
  //            b.Domain = new Interval(0, 1);
  //            List<Point3d> ptMidEnd = new List<Point3d> { b.PointAtEnd };
  //            // List<Point3d> ptMidEnd = new List<Point3d> { b.PointAtEnd, b.PointAt(0.5) };

  //            foreach (var p in ptMidEnd) {
  //              cPln = mPln.Clone();
  //              cPln.Translate(new Vector3d(p - mPln.Origin));

  //              var cTree = new Tree2D(cPln, mHeight / 3.0);
  //              cTree.Draw(1);

  //              babyTreeCol.Add(cTree.mCurCanopy);
  //              babyTreeCol.Add(cTree.mCurTrunk);
  //            }
  //          }
  //          mNewBornBranch.AddRange(babyTreeCol);
  //        } else if (phase > mDyingIdx) {
  //          // base branch
  //          mNewBornBranch = CrvSelection(mSideBranch, 0, 16, 5);
  //          mNewBornBranch = mNewBornBranch.Select(x => x.Trim(0.0, 0.3)).ToList();

  //          // top two branches use polylinecurve
  //          var top2 = new List<Curve>();
  //          for (int i = 0; i < 4; i += 2) {
  //            var c = mNewBornBranch[i];
  //            c.Trim(0.0, 0.35);
  //            top2.Add(c);
  //          }

  //          // bottom two branches use curve
  //          var bot2 = new List<Curve>();
  //          for (int i = 1; i < 4; i += 2) {
  //            var c = mNewBornBranch[i].Trim(0.0, 0.2);
  //            var pt2 = c.PointAtEnd + mPln.YAxis * c.GetLength() / 2;
  //            var newC = NurbsCurve.Create(false, 2,
  //                                         new List<Point3d> { c.PointAtStart, c.PointAtEnd, pt2
  //                                         });
  //            bot2.Add(newC);
  //          }

  //          // collect new born branches, remove canopy and side branches
  //          mNewBornBranch = top2.Concat(bot2).ToList();
  //          mCurCanopy_l = null;
  //          mCurCanopy_r = null;
  //          mCurCanopy = null;

  //          // create babyTree
  //          var babyCol = new List<Curve>();
  //          foreach (var b in mNewBornBranch) {
  //            cPln = mPln.Clone();
  //            cPln.Translate(new Vector3d(b.PointAtEnd - mPln.Origin));
  //            var cTree = new Tree2D(cPln, mHeight / 3.0);
  //            cTree.Draw(phase - mDyingIdx + 1);

  //            babyCol.Add(cTree.mCurCanopy);
  //            babyCol.Add(cTree.mCurTrunk);
  //            babyCol.AddRange(cTree.mSideBranch);

  //            // for debugging
  //            mDebug.AddRange(cTree.mCircCol);
  //          }

  //          // attach the babyTree and split the trunk
  //          mNewBornBranch.AddRange(babyCol);

  //          // process Trunk spliting.
  //          var leftTrunk = mCurTrunk.Duplicate() as Curve;
  //          leftTrunk.Translate(-mPln.XAxis * mHeight / 150);
  //          var rightTrunk = mCurTrunk.Duplicate() as Curve;
  //          rightTrunk.Translate(mPln.XAxis * mHeight / 150);
  //          mCurTrunk = new PolylineCurve(
  //              new List<Point3d> { leftTrunk.PointAtStart, leftTrunk.PointAtEnd,
  //                                  rightTrunk.PointAtEnd, rightTrunk.PointAtStart });
  //        }

  //        mCurCanopy_l = null;
  //        mCurCanopy_r = null;
  //        mCurCanopy = null;
  //        mSideBranch_l.Clear();
  //        mSideBranch_r.Clear();
  //        mSideBranch.Clear();
  //      }
  // #endregion

  //      // mDebug = sideBranch;

  //      return (true, "");
  //    }

  //    // scale the trees so that they don't overlap
  //    public double CalWidth() {
  //      var bbx = mSideBranch.Select(x => x.GetBoundingBox(false)).ToList();
  //      var finalBbx = mCurTrunk.GetBoundingBox(false);
  //      bbx.ForEach(x => finalBbx.Union(x));

  //      // using diag to check which plane the trees are
  //      Vector3d diag = finalBbx.Max - finalBbx.Min;
  //      double wid = Vector3d.Multiply(diag, mPln.XAxis);

  //      return wid;
  //    }

  //    public void Scale(in Tuple<double, double> scal) {
  //      var lScal = Transform.Scale(mPln, scal.Item1, 1, 1);
  //      var rScal = Transform.Scale(mPln, scal.Item2, 1, 1);

  //      // circumBound
  //      mCircCol.Clear();
  //      for (int i = 0; i < mCircCol_l.Count; i++) {
  //        mCircCol_l[i].Transform(lScal);
  //        mCircCol_r[i].Transform(rScal);
  //        var fullBnd = Curve.JoinCurves(new List<Curve> { mCircCol_l[i], mCircCol_r[i] },
  //        0.01)[0]; mCircCol.Add(fullBnd);
  //      }

  //      // side branches
  //      for (int i = 0; i < mSideBranch_l.Count; i++) {
  //        mSideBranch_l[i].Transform(lScal);
  //        mSideBranch_r[i].Transform(rScal);
  //      }
  //      mSideBranch = mSideBranch_l.Concat(mSideBranch_r).ToList();

  //      // subbranches
  //      for (int i = 0; i < mSubBranch_l.Count; i++) {
  //        mSubBranch_l[i].Transform(lScal);
  //        mSubBranch_r[i].Transform(rScal);
  //      }
  //      mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();

  //      if (mCurCanopy != null) {
  //        mCurCanopy_l.Transform(lScal);
  //        mCurCanopy_r.Transform(rScal);
  //        mCurCanopy = Curve.JoinCurves(new List<Curve> { mCurCanopy_l, mCurCanopy_r }, 0.02)[0];
  //      }
  //    }

  //    private List<Curve> CrvSelection(in List<Curve> lst, int startId, int endId, int step) {
  //      List<Curve> res = new List<Curve>();
  //      for (int i = startId; i < endId; i += step) {
  //        res.Add(lst[i]);
  //        // res[res.Count - 1].Domain = new Interval(0, 1);
  //      }

  //      return res;
  //    }

  //    private Curve TrimCrv(in Curve C0, in Curve C1) {
  //      var events = Intersection.CurveCurve(C0, C1, 0.01, 0.01);
  //      if (events.Count != 0)
  //        return C0.Trim(0.0, events[0].ParameterA);
  //      else
  //        return C0;
  //    }

  //    private List<Tuple<Curve, string>> BiBranching(in Plane pln, int step) {
  //      // use a string "i-j-l/r" to record the position of the branch -- end pt represent the
  //      branch var subPt = new List<Tuple<Point3d, string>> { Tuple.Create(pln.Origin, "n") }; var
  //      subLn = new List<Tuple<Curve, string>> { Tuple.Create(
  //          new Line(pln.Origin, pln.Origin + pln.YAxis).ToNurbsCurve() as Curve, "") };
  //      var resCol = new List<Tuple<Curve, string>>();

  //      for (int i = 0; i < step; i++) {
  //        var ptCol = new List<Tuple<Point3d, string>>();
  //        var lnCol = new List<Tuple<Curve, string>>();

  //        var scalingParam = Math.Pow(0.85, (mMatureIdx - mMatureIdx + 1));
  //        var vecLen = mCurTrunk.GetLength() * 0.1 * scalingParam;
  //        var angleX = (mOpenAngle - (mMatureIdx + step) * mAngleStep * 3) * scalingParam;

  //        // for each phase, do bi-branching: 1 node -> 2 branches
  //        foreach (var (pt, j) in subPt.Select((pt, j) => (pt, j))) {
  //          var curVec = new Vector3d(subLn[j].Item1.PointAtEnd - subLn[j].Item1.PointAtStart);
  //          curVec.Unitize();

  //          var vecA = new Vector3d(curVec);
  //          var vecB = new Vector3d(curVec);
  //          vecA.Rotate(Utils.ToRadian(angleX), mPln.ZAxis);
  //          vecB.Rotate(-Utils.ToRadian(angleX), mPln.ZAxis);

  //          var endA = subPt[j].Item1 + vecA * vecLen;
  //          var endB = subPt[j].Item1 + vecB * vecLen;

  //          // trace string: trace of the trace_prevPt + trace_curPt
  //          ptCol.Add(Tuple.Create(endA, subPt[j].Item2 + "l"));
  //          ptCol.Add(Tuple.Create(endB, subPt[j].Item2 + "r"));

  //          lnCol.Add(Tuple.Create(new Line(pt.Item1, endA).ToNurbsCurve() as Curve, pt.Item2 +
  //          "l")); lnCol.Add(Tuple.Create(new Line(pt.Item1, endB).ToNurbsCurve() as Curve,
  //          pt.Item2 + "r"));
  //        }

  //        subPt = ptCol;
  //        subLn = lnCol;

  //        resCol.AddRange(subLn);
  //      }

  //      return resCol;
  //    }

  //    // tree core param
  //    public Plane mPln;
  //    public double mHeight;
  //    // placeholder, not used for 2D cases
  //    public double mRadius = 1;
  //    public int mCurPhase;
  //    bool mUnitary = false;

  //    double mAngleStep = 1.2;
  //    readonly int numLayer = 18;
  //    readonly double mOpenAngle = 57;

  //    // mature and dying range idx
  //    readonly int mMatureIdx = 6;
  //    readonly int mDyingIdx = 10;

  //    // other parameter
  //    double treeSepParam = 0.2;
  //    readonly double maxStdR, minStdR, stepR;

  //    // curve collection
  //    public Curve mCurCanopy_l;
  //    public Curve mCurCanopy_r;
  //    public Curve mCurCanopy;

  //    public Curve mCurTrunk;

  //    public List<Curve> mCircCol_l = new List<Curve>();
  //    public List<Curve> mCircCol_r = new List<Curve>();
  //    public List<Curve> mCircCol = new List<Curve>();

  //    public List<Curve> mSideBranch_l = new List<Curve>();
  //    public List<Curve> mSideBranch_r = new List<Curve>();
  //    public List<Curve> mSideBranch = new List<Curve>();

  //    public List<Curve> mSubBranch_l = new List<Curve>();
  //    public List<Curve> mSubBranch_r = new List<Curve>();
  //    public List<Curve> mSubBranch = new List<Curve>();

  //    public List<Curve> mNewBornBranch = new List<Curve>();

  //    public List<Curve> mDebug = new List<Curve>();
  //  }

  class Tree2D {
    public Tree2D() {}
    public Tree2D(Plane pln, double height, bool unitary = false) {
      mPln = pln;
      mHeight = height;
      mUnitary = unitary;

      // Configure growth parameters
      mTrunkSegLen = height / mStage1;
      mMaxBranchLen = height * 0.5;
      mMinBranchLen = height * 0.25;
    }

    // Draw the tree based on its phase
    public (bool, string) Draw(int phase) {
      // Record current phase
      mCurPhase = phase;

      // Validate input parameters
      if (mHeight <= 0)
        return (false, "The height of the tree needs to be > 0.");
      if (phase > mStage4 || phase <= 0)
        return (false, "Phase out of range ([1, 13] for tree).");

      // Clear previous data
      ClearTreeData();

      // Generate tree components according to its growth stage
      if (phase < mStage1) {
        // Phases 1-3: Just young tree,
        GrowStage1();
      } else if (phase <= mStage2) {
        // Phase 4: with a bit of special treatment
        // Phases 5-8: Mature tree with continued branching
        GrowStage1();
        GrowStage2();
      } else if (phase <= mStage3) {
        // Phases 9-10: Aging tree, no new branches
        GrowStage1();
        GrowStage2();
        GrowStage3();
      } else {
        // Phases 11+: Dying tree
        GrowStage1();
        GrowStage2();
        GrowStage3();
        GrowStage4();
      }

      return (true, "");
    }

    // Calculate the width of the tree for scaling purposes
    public double CalWidth() {
      var allBranches = new List<Curve>();
      allBranches.Add(mCurTrunk);
      allBranches.AddRange(mSideBranch);
      allBranches.AddRange(mSubBranch);

      if (allBranches.Count == 0)
        return 0;

      var finalBbx = allBranches[0].GetBoundingBox(false);
      for (int i = 1; i < allBranches.Count; i++) {
        finalBbx.Union(allBranches[i].GetBoundingBox(false));
      }

      // Get the width along the X-axis
      Vector3d diag = finalBbx.Max - finalBbx.Min;
      double width = Vector3d.Multiply(diag, mPln.XAxis);

      return width;
    }

    // Scale the tree to prevent overlapping
    public void Scale(in Tuple<double, double> scal) {
      // Create transforms for left and right sides
      var lScal = Transform.Scale(mPln, scal.Item1, 1, 1);
      var rScal = Transform.Scale(mPln, scal.Item2, 1, 1);

      // Scale side branches
      for (int i = 0; i < mSideBranch_l.Count; i++) {
        mSideBranch_l[i].Transform(lScal);
      }
      for (int i = 0; i < mSideBranch_r.Count; i++) {
        mSideBranch_r[i].Transform(rScal);
      }
      mSideBranch = mSideBranch_l.Concat(mSideBranch_r).ToList();

      // Scale top branches
      for (int i = 0; i < mSubBranch_l.Count; i++) {
        mSubBranch_l[i].Transform(lScal);
      }
      for (int i = 0; i < mSubBranch_r.Count; i++) {
        mSubBranch_r[i].Transform(rScal);
      }
      mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();

      // Scale canopy if it exists
      if (mCurCanopy != null) {
        mCurCanopy_l?.Transform(lScal);
        mCurCanopy_r?.Transform(rScal);

        if (mCurCanopy_l != null && mCurCanopy_r != null) {
          var joined = Curve.JoinCurves(new List<Curve> { mCurCanopy_l, mCurCanopy_r }, 0.02);
          if (joined.Length > 0) {
            mCurCanopy = joined[0];
          }
        }
      }

      // Generate an outline curve that represents the tree boundary
      // GenerateOutlineCurve();
    }

    // Stage 1: Young tree growth (phases 1-4)
    private void GrowStage1() {
      // Calculate how much of stage 1 to grow based on phase
      int growthPhase = Math.Min(mCurPhase, mStage1);

      // Create the trunk
      double trunkHeight = mTrunkSegLen * growthPhase;
      Point3d trunkStart = mPln.Origin;
      Point3d trunkEnd = mPln.Origin + mPln.YAxis * trunkHeight;
      mCurTrunk = new Line(trunkStart, trunkEnd).ToNurbsCurve();
      mCurTrunk.Domain = new Interval(0.0, 1.0);

      // Always generate side branches, even at phase 1
      GenerateSideBranches(growthPhase);
    }

    // Stage 2: Mature tree growth (phases 5-8)
    private void GrowStage2() {
      // If a phase override is provided, use it instead of calculating from current phase
      int stage2Phase = mCurPhase - mStage1 + 1;

      // Don't add more branches after stage 2 is complete
      if (mCurPhase > mStage2) {
        stage2Phase = mStage2 - mStage1;
      }

      // Create top branches using bi-branching
      Point3d topPoint = mCurTrunk.PointAtEnd;
      Plane topPlane = mPln.Clone();
      topPlane.Origin = topPoint;

      var topBranches = BiBranching(topPlane, stage2Phase);

      // Separate left and right branches
      foreach (var branch in topBranches) {
        if (branch.Item2 != null && branch.Item2.EndsWith("l")) {
          mSubBranch_l.Add(branch.Item1);
        } else if (branch.Item2 != null && branch.Item2.EndsWith("r")) {
          mSubBranch_r.Add(branch.Item1);
        }
      }

      mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();
    }

    // Stage 3: Aging tree (phases 9-10)
    private void GrowStage3() {
      int stage3Phase = mCurPhase - mStage2;

      // Create a collection of all branches (both side and top)
      var allBranches = new List<Tuple<Curve, bool, List<int>>>();  // Curve, isLeft, childIndices

      // Track branches by their spatial position to ensure deterministic removal across phases
      var branchPositionMap = new Dictionary<Point3d, int>();

      // Add side branches with metadata
      for (int i = 0; i < mSideBranch_l.Count; i++) {
        var curve = mSideBranch_l[i];
        allBranches.Add(Tuple.Create(curve, true, new List<int>()));
        branchPositionMap[curve.PointAtStart] = allBranches.Count - 1;
      }

      for (int i = 0; i < mSideBranch_r.Count; i++) {
        var curve = mSideBranch_r[i];
        allBranches.Add(Tuple.Create(curve, false, new List<int>()));
        branchPositionMap[curve.PointAtStart] = allBranches.Count - 1;
      }

      // Add top branches with metadata
      int sideBranchCount = allBranches.Count;
      for (int i = 0; i < mSubBranch_l.Count; i++) {
        var curve = mSubBranch_l[i];
        allBranches.Add(Tuple.Create(curve, true, new List<int>()));
        branchPositionMap[curve.PointAtStart] = allBranches.Count - 1;
      }

      for (int i = 0; i < mSubBranch_r.Count; i++) {
        var curve = mSubBranch_r[i];
        allBranches.Add(Tuple.Create(curve, false, new List<int>()));
        branchPositionMap[curve.PointAtStart] = allBranches.Count - 1;
      }

      // Identify parent-child relationships based on proximity
      for (int i = 0; i < allBranches.Count; i++) {
        for (int j = 0; j < allBranches.Count; j++) {
          if (i == j)
            continue;

          if (allBranches[j].Item1.PointAtStart.DistanceTo(allBranches[i].Item1.PointAtEnd) < 0.1) {
            allBranches[i].Item3.Add(j);
          }
        }
      }

      // Use a fixed seed to ensure consistency across phases
      // Random rnd = new Random(1234);
      var rnd = Utils.balRnd.Next();

      // Generate a priority list for removal - this is deterministic and stable across phases
      var removalPriority =
          Enumerable.Range(0, allBranches.Count)
              .OrderBy(i => {
                // Hash the branch position for deterministic ordering
                var pt = allBranches[i].Item1.PointAtStart;
                return pt.X * 7919 + pt.Y * 6113 + pt.Z * 3967;  // Use prime multipliers
              })
              .ToList();

      // Calculate target percentage for current phase (10% per phase)
      double removalPercentage = 0.3 * stage3Phase;
      int targetRemovalCount = (int)(allBranches.Count * removalPercentage);

      // Create a record of which branches to remove
      HashSet<int> branchesToRemove = new HashSet<int>();

      // Track the total number of branches that will be removed (including children)
      int totalBranchesToRemove = 0;

      // Function to count a branch and all its descendants
      int CountBranchAndChildren(int branchIndex) {
        if (branchesToRemove.Contains(branchIndex))
          return 0;

        int count = 1;  // Count this branch

        // Count all child branches
        foreach (int childIndex in allBranches[branchIndex].Item3) {
          count += CountBranchAndChildren(childIndex);
        }

        return count;
      }

      // Function to recursively mark branches for removal
      void MarkBranchAndChildren(int branchIndex) {
        if (branchesToRemove.Contains(branchIndex))
          return;

        branchesToRemove.Add(branchIndex);

        // Recursively mark child branches
        foreach (int childIndex in allBranches[branchIndex].Item3) {
          MarkBranchAndChildren(childIndex);
        }
      }

      // Prefer removing top branches first (like Tree3D prefers mNodePhase >= 7)
      var topBranchIndices = Enumerable.Range(sideBranchCount, allBranches.Count - sideBranchCount);

      // Gather branches in priority order until we reach our target
      foreach (int i in removalPriority) {
        // Check if we've already marked enough branches
        if (totalBranchesToRemove >= targetRemovalCount)
          break;

        // Prioritize top branches before side branches
        if (i < sideBranchCount && totalBranchesToRemove < targetRemovalCount * 0.5) {
          // Only remove side branches after we've removed a certain number of top branches
          continue;
        }

        // Skip already marked branches
        if (branchesToRemove.Contains(i))
          continue;

        // Calculate how many branches would be removed if we mark this one
        int branchesAffected = CountBranchAndChildren(i);

        // Only add if it doesn't exceed our target by too much
        if (totalBranchesToRemove + branchesAffected <= targetRemovalCount * 1.2) {
          MarkBranchAndChildren(i);
          totalBranchesToRemove += branchesAffected;
        }
      }

      // If we still need more branches, consider remaining ones
      if (totalBranchesToRemove < targetRemovalCount) {
        foreach (int i in removalPriority) {
          if (totalBranchesToRemove >= targetRemovalCount)
            break;
          if (branchesToRemove.Contains(i))
            continue;

          int branchesAffected = CountBranchAndChildren(i);
          if (totalBranchesToRemove + branchesAffected <= targetRemovalCount * 1.2) {
            MarkBranchAndChildren(i);
            totalBranchesToRemove += branchesAffected;
          }
        }
      }

      // Now actually remove the branches
      var newSideBranchL = new List<Curve>();
      var newSideBranchR = new List<Curve>();
      var newSubBranchL = new List<Curve>();
      var newSubBranchR = new List<Curve>();

      // Keep branches that weren't marked for removal
      for (int i = 0; i < allBranches.Count; i++) {
        if (branchesToRemove.Contains(i))
          continue;

        var branch = allBranches[i];
        bool isLeft = branch.Item2;

        if (i < sideBranchCount) {
          // Side branch
          if (isLeft) {
            newSideBranchL.Add(branch.Item1);
          } else {
            newSideBranchR.Add(branch.Item1);
          }
        } else {
          // Top branch
          if (isLeft) {
            newSubBranchL.Add(branch.Item1);
          } else {
            newSubBranchR.Add(branch.Item1);
          }
        }
      }

      // Update branch collections
      mSideBranch_l = newSideBranchL;
      mSideBranch_r = newSideBranchR;
      mSideBranch = mSideBranch_l.Concat(mSideBranch_r).ToList();

      mSubBranch_l = newSubBranchL;
      mSubBranch_r = newSubBranchR;
      mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();
    }

    // Stage 4: Dying tree (phases 11+)
    private void GrowStage4() {
      int stage4Phase = mCurPhase - mStage3;
      Random rnd = new Random(mCurPhase + 42);  // Different seed than Stage 3

      // For dying phase, add new growth from the base (saplings)
      if (stage4Phase == 1) {
        // Select a few side branches to be the base for new growth
        var selectedBranches = SelectBaseForNewGrowth();

        // Create small trees at the ends of selected branches
        foreach (var branch in selectedBranches) {
          Plane branchPlane = mPln.Clone();
          branchPlane.Origin = branch.PointAtEnd;

          // Create a smaller tree (1/3 of original height)
          var sapling = new Tree2D(branchPlane, mHeight / 3.0);
          sapling.Draw(1);  // Start with phase 1

          // Add the sapling's components to our newborn branch collection
          mNewBornBranch.Add(sapling.mCurTrunk);
          mNewBornBranch.AddRange(sapling.mSideBranch);
          if (sapling.mCurCanopy != null) {
            mNewBornBranch.Add(sapling.mCurCanopy);
          }
        }
      } else if (stage4Phase >= 2) {
        // For later phases, grow the saplings
        var selectedBranches = SelectBaseForNewGrowth();

        foreach (var branch in selectedBranches) {
          Plane branchPlane = mPln.Clone();
          branchPlane.Origin = branch.PointAtEnd;

          var sapling = new Tree2D(branchPlane, mHeight / 3.0);
          sapling.Draw(stage4Phase);  // Grow saplings according to stage4Phase

          mNewBornBranch.Add(sapling.mCurTrunk);
          mNewBornBranch.AddRange(sapling.mSideBranch);
          mNewBornBranch.AddRange(sapling.mSubBranch);
        }

        // In the final phases, the main tree structure degrades significantly
        if (stage4Phase >= 3) {
          // Clear most side branches and all top branches
          if (mSideBranch_l.Count > 2)
            mSideBranch_l = mSideBranch_l.Take(2).ToList();
          if (mSideBranch_r.Count > 2)
            mSideBranch_r = mSideBranch_r.Take(2).ToList();
          mSideBranch = mSideBranch_l.Concat(mSideBranch_r).ToList();

          // Clear all top branches
          mSubBranch_l.Clear();
          mSubBranch_r.Clear();
          mSubBranch.Clear();

          // Clear canopy
          mCurCanopy = null;
          mCurCanopy_l = null;
          mCurCanopy_r = null;

          // Split the trunk to show deterioration (like in the original Tree2D)
          var leftTrunk = mCurTrunk.Duplicate() as Curve;
          leftTrunk.Translate(-mPln.XAxis * mHeight / 150);
          var rightTrunk = mCurTrunk.Duplicate() as Curve;
          rightTrunk.Translate(mPln.XAxis * mHeight / 150);

          mCurTrunk = new PolylineCurve(
              new List<Point3d> { leftTrunk.PointAtStart, leftTrunk.PointAtEnd,
                                  rightTrunk.PointAtEnd, rightTrunk.PointAtStart });
        }
      }
    }

    // Generate side branches based on the current phase
    private void GenerateSideBranches(int phase) {
      mSideBranch_l.Clear();
      mSideBranch_r.Clear();

      // Always generate at least one branch per side, more in later phases
      int numBranchesPerSide = Math.Max(1, phase * 2 - 1);
      double trunkHeight = mCurTrunk.GetLength();

      // Calculate branch parameters
      double branchLengthBase = Utils.remap(phase, 1, mStage1, mMinBranchLen, mMaxBranchLen);
      double baseAngle = mBaseAngle * (1 - 0.05 * (phase - 1));

      for (int i = 0; i < numBranchesPerSide; i++) {
        // Position along trunk (distribute evenly, avoiding very bottom)
        double posRatio = 0.3 + 0.6 * i / Math.Max(1, numBranchesPerSide - 1);
        Point3d branchPoint = mCurTrunk.PointAt(posRatio);

        // Calculate branch length (shorter at top and bottom, longer in middle)
        double lengthFactor = 4 * posRatio * (1 - posRatio);  // Parabolic distribution
        double branchLength = branchLengthBase * lengthFactor;

        // Calculate angle (steeper at top)
        double angle = baseAngle * (1 - 0.3 * posRatio);

        // Left branch
        Vector3d leftDir = new Vector3d(mPln.YAxis);
        leftDir.Rotate(Utils.ToRadian(angle), mPln.ZAxis);
        Curve leftBranch =
            new Line(branchPoint, branchPoint + leftDir * branchLength).ToNurbsCurve();
        mSideBranch_l.Add(leftBranch);

        // Right branch
        Vector3d rightDir = new Vector3d(mPln.YAxis);
        rightDir.Rotate(Utils.ToRadian(-angle), mPln.ZAxis);
        Curve rightBranch =
            new Line(branchPoint, branchPoint + rightDir * branchLength).ToNurbsCurve();
        mSideBranch_r.Add(rightBranch);
      }

      // Combine left and right branches
      mSideBranch = mSideBranch_l.Concat(mSideBranch_r).ToList();
    }

    // Generate an outline curve to represent the tree boundary
    private void GenerateOutlineCurve() {
      if (mCurPhase < mStage3) {  // Only generate canopy for non-dying trees
                                  // Create a list of all branch endpoints
        var points = new List<Point3d>();

        // Add trunk top
        points.Add(mCurTrunk.PointAtEnd);

        // Add side branch endpoints
        foreach (var branch in mSideBranch) {
          points.Add(branch.PointAtEnd);
        }

        // Add top branch endpoints
        foreach (var branch in mSubBranch) {
          points.Add(branch.PointAtEnd);
        }

        // Add trunk base
        points.Add(mCurTrunk.PointAtStart);

        if (points.Count < 3) {
          // Not enough points for a proper canopy
          return;
        }

        // Sort points by polar angle around trunk base
        Point3d center = mCurTrunk.PointAtStart;
        var sortedPoints = points
                               .OrderBy(p => {
                                 Vector3d v = p - center;
                                 return Math.Atan2(v.Y, v.X);
                               })
                               .ToList();

        // Create left and right canopy sections
        var leftPoints = new List<Point3d>();
        var rightPoints = new List<Point3d>();

        // Find the split point (trunk top)
        int splitIndex = sortedPoints.FindIndex(p => p.DistanceTo(mCurTrunk.PointAtEnd) < 1e-6);

        if (splitIndex >= 0) {
          // Add points to left and right sections
          for (int i = 0; i <= splitIndex; i++) {
            leftPoints.Add(sortedPoints[i]);
          }

          for (int i = splitIndex; i < sortedPoints.Count; i++) {
            rightPoints.Add(sortedPoints[i]);
          }

          // Add trunk base to close the curves
          leftPoints.Add(center);
          rightPoints.Add(center);

          // Create canopy curves
          if (leftPoints.Count >= 3) {
            mCurCanopy_l = new PolylineCurve(leftPoints);
          }

          if (rightPoints.Count >= 3) {
            mCurCanopy_r = new PolylineCurve(rightPoints);
          }

          // Join left and right to form complete canopy
          if (mCurCanopy_l != null && mCurCanopy_r != null) {
            var joined = Curve.JoinCurves(new List<Curve> { mCurCanopy_l, mCurCanopy_r }, 0.02);
            if (joined.Length > 0) {
              mCurCanopy = joined[0];
            }
          }
        }
      }
    }

    // Select branches to use as base for new growth in dying phase
    private List<Curve> SelectBaseForNewGrowth() {
      List<Curve> selectedBranches = new List<Curve>();
      Random rnd = new Random(mCurPhase);  // Use phase as seed

      // Select a few lower branches from each side
      int branchesPerSide = 2;

      // Helper function to select branches
      List<Curve> SelectFromSide(List<Curve> side) {
        if (side.Count == 0)
          return new List<Curve>();

        // Sort branches by height (Y coordinate)
        var sortedBranches = side.OrderBy(b => b.PointAtStart.Y).ToList();

        // Select from lower half
        int lowerHalfCount = Math.Max(1, sortedBranches.Count / 2);
        var lowerBranches = sortedBranches.Take(lowerHalfCount).ToList();

        // Randomly select specified number
        var selected = new List<Curve>();
        for (int i = 0; i < branchesPerSide && lowerBranches.Count > 0; i++) {
          int idx = rnd.Next(lowerBranches.Count);
          selected.Add(lowerBranches[idx]);
          lowerBranches.RemoveAt(idx);
        }

        return selected;
      }

      // Select branches from both sides
      selectedBranches.AddRange(SelectFromSide(mSideBranch_l));
      selectedBranches.AddRange(SelectFromSide(mSideBranch_r));

      // Trim selected branches to make them shorter
      for (int i = 0; i < selectedBranches.Count; i++) {
        var branch = selectedBranches[i];
        branch.Domain = new Interval(0.0, 1.0);
        selectedBranches[i] = branch.Trim(0.0, 0.3);  // Use only 30% of the branch
      }

      return selectedBranches;
    }

    // Recursive bifurcation for top branches
    private List<Tuple<Curve, string>> BiBranching(in Plane pln, int step) {
      // Starting point
      var subPt = new List<Tuple<Point3d, string>> { Tuple.Create(pln.Origin, "n") };

      // Starting branch (vertical trunk extension)
      var subLn = new List<Tuple<Curve, string>> { Tuple.Create(
          new Line(pln.Origin, pln.Origin + pln.YAxis).ToNurbsCurve() as Curve, "") };

      var resCol = new List<Tuple<Curve, string>>();

      // Branch scaling factor for each generation
      double scaleFactor = 0.85;

      for (int i = 0; i < step; i++) {
        var ptCol = new List<Tuple<Point3d, string>>();
        var lnCol = new List<Tuple<Curve, string>>();

        // Calculate branch parameters for this generation
        double scalingParam = Math.Pow(scaleFactor, i);
        double vecLen = mHeight * 0.1 * scalingParam;
        double branchAngle = mTopBranchAngle * scalingParam;

        // For each endpoint from previous generation, create two new branches
        foreach (var (pt, j) in subPt.Select((pt, j) => (pt, j))) {
          // Get direction of parent branch
          var curVec = new Vector3d(subLn[j].Item1.PointAtEnd - subLn[j].Item1.PointAtStart);
          curVec.Unitize();

          // Create two branch directions by rotating parent direction
          var vecA = new Vector3d(curVec);
          var vecB = new Vector3d(curVec);
          vecA.Rotate(Utils.ToRadian(branchAngle), mPln.ZAxis);
          vecB.Rotate(-Utils.ToRadian(branchAngle), mPln.ZAxis);

          // Calculate endpoints
          var endA = subPt[j].Item1 + vecA * vecLen;
          var endB = subPt[j].Item1 + vecB * vecLen;

          // Add points with path strings
          ptCol.Add(Tuple.Create(endA, subPt[j].Item2 + "l"));
          ptCol.Add(Tuple.Create(endB, subPt[j].Item2 + "r"));

          // Add branches with path strings
          lnCol.Add(Tuple.Create(new Line(pt.Item1, endA).ToNurbsCurve() as Curve, pt.Item2 + "l"));
          lnCol.Add(Tuple.Create(new Line(pt.Item1, endB).ToNurbsCurve() as Curve, pt.Item2 + "r"));
        }

        // Update for next iteration
        subPt = ptCol;
        subLn = lnCol;

        // Add current generation branches to result
        resCol.AddRange(subLn);
      }

      return resCol;
    }

    // Clear all tree data structures
    private void ClearTreeData() {
      mCurTrunk = null;
      mCurCanopy = null;
      mCurCanopy_l = null;
      mCurCanopy_r = null;
      mCircCol.Clear();
      mSideBranch_l.Clear();
      mSideBranch_r.Clear();
      mSideBranch.Clear();
      mSubBranch_l.Clear();
      mSubBranch_r.Clear();
      mSubBranch.Clear();
      mNewBornBranch.Clear();
      mDebug.Clear();
    }

    // Tree core parameters
    public Plane mPln;
    public double mHeight;
    public double mRadius = 1;
    public int mCurPhase;
    bool mUnitary = false;

    // Growth parameters
    private readonly int mStage1 = 4;   // Young tree phase
    private readonly int mStage2 = 8;   // Mature tree phase
    private readonly int mStage3 = 10;  // Aging phase
    private readonly int mStage4 = 13;  // Dying phase

    private double mTrunkSegLen;                   // Length of trunk segment per phase
    private double mMaxBranchLen;                  // Maximum branch length
    private double mMinBranchLen;                  // Minimum branch length
    private readonly double mBaseAngle = 60;       // Base angle for side branches
    private readonly double mTopBranchAngle = 35;  // Angle for top branches

    // Curve collections
    public Curve mCurCanopy_l;
    public Curve mCurCanopy_r;
    public Curve mCurCanopy;
    public Curve mCurTrunk;
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

  class BranchNode3D {
    public BranchNode3D() {}
    public BranchNode3D(int id, int phase, in Point3d node) {
      mNode = node;

      mID = id;
      mNodePhase = phase;
    }

    public void AddBranchAlong(Vector3d vec) {
      var tmpLen = new Line(mNode, mNode + vec).ToNurbsCurve();
      if (tmpLen != null) {
        tmpLen.Domain = new Interval(0.0, 1.0);
        mBranch.Add(tmpLen);
      }
    }

    /// <summary>
    ///  Iteratively turn off the display from the given nodes
    /// </summary>
    /// <param name="branchRelation"></param>
    /// <param name="allNodes"></param>
    /// <returns></returns>
    public int TurnOff(Dictionary<int, HashSet<int>> branchRelation, List<BranchNode3D> allNodes) {
      int totalAffectedBranch = 0;
      if (flagShow == false) {
        return 0;
      }

      flagShow = false;
      totalAffectedBranch++;

      // recursively toggle all sub branches
      if (branchRelation.ContainsKey(mID)) {
        foreach (var idx in branchRelation[mID]) {
          totalAffectedBranch += allNodes[idx].TurnOff(branchRelation, allNodes);
        }
      }

      return totalAffectedBranch;
    }

    public void ToggleSplitable() {
      flagSplittable = !flagSplittable;
    }

    public Point3d GetPos() {
      return mNode;
    }

    Point3d mNode = new Point3d();
    public int mNodePhase = -1;

    public int mID = -1;
    public bool flagShow = true;
    public bool flagSplittable = false;
    public List<bool> flagBranchSplit = new List<bool>();

    public List<Curve> mBranch { get; set; } = new List<Curve>();
  }

  public class Tree3D {
    public Tree3D() {}

    public Tree3D(Plane pln, double globalScale, double trunkScale, int seed = 0,
                  bool branchRot = false, string id = "") {
      mPln = pln;
      mGScale = globalScale;
      mTScale = trunkScale;

      var baseLen = 10;
      mScaledLen = baseLen * mGScale * mTScale;
      mRnd = new Random(seed);
      mBranchRot = branchRot;

      mMaxSideBranchLen = 0.5 * mScaledLen;
      mMinSideBranchLen = 0.25 * mScaledLen * mTScale / mStage1;

      mId = id;
    }

    public Tree3D Copy() {
      // Create a new instance with the same basic parameters
      Tree3D copy =
          new Tree3D(this.mPln.Clone(),  // Clone the plane
                     this.mGScale, this.mTScale,
                     this.mRnd.Next(),  // Use a new random seed derived from current random
                     this.mBranchRot, this.mId);

      // Copy scalar properties
      copy.mScaledLen = this.mScaledLen;
      copy.mPhase = this.mPhase;
      copy.mNumBranchPerLayer = this.mNumBranchPerLayer;
      copy.mAngleMain = this.mAngleMain;
      copy.mAngleTop = this.mAngleTop;
      copy.mMaxSideBranchLen = this.mMaxSideBranchLen;
      copy.mMinSideBranchLen = this.mMinSideBranchLen;
      copy.mNearestTreeDist = this.mNearestTreeDist;
      copy.mNearestTree = new Point3d(this.mNearestTree);
      copy.mSoloRadius = this.mSoloRadius;

      // Copy stage settings
      copy.mStage1 = this.mStage1;
      copy.mStage2 = this.mStage2;
      copy.mStage3 = this.mStage3;
      copy.mStage4 = this.mStage4;

      // Deep copy of trunk segments
      copy.mTrunkSegments =
          this.mTrunkSegments.Select(line => new Line(new Point3d(line.From), new Point3d(line.To)))
              .ToList();

      // Deep copy of nearest trees
      copy.mNearestTrees = this.mNearestTrees.Select(pt => new Point3d(pt)).ToList();

      // Deep copy of messages
      copy.mMmsg = new List<string>(this.mMmsg);

      // Create a mapping from original node IDs to new node IDs
      Dictionary<int, int> nodeIdMap = new Dictionary<int, int>();

      // Deep copy of all nodes
      Dictionary<int, BranchNode3D> originalNodes = this.mAllNode.ToDictionary(node => node.mID);
      foreach (BranchNode3D originalNode in this.mAllNode) {
        BranchNode3D newNode = new BranchNode3D(originalNode.mID, originalNode.mNodePhase,
                                                new Point3d(originalNode.GetPos()));

        // Copy flags
        newNode.flagShow = originalNode.flagShow;
        newNode.flagSplittable = originalNode.flagSplittable;

        // Deep copy branches
        foreach (Curve branch in originalNode.mBranch) {
          newNode.mBranch.Add(branch.DuplicateCurve());
        }

        // Deep copy flag branch split list
        newNode.flagBranchSplit = new List<bool>(originalNode.flagBranchSplit);

        // Add to the new tree's node list
        copy.mAllNode.Add(newNode);

        // If this is the base node, set it
        if (originalNode.mID == this.mBaseNode?.mID) {
          copy.mBaseNode = newNode;
        }
      }

      // Deep copy of branch relations
      foreach (var kvp in this.mBranchRelation) {
        copy.mBranchRelation[kvp.Key] = new HashSet<int>(kvp.Value);
      }

      // Deep copy of trunk branch nodes
      foreach (BranchNode3D node in this.mTrunkBranchNode) {
        BranchNode3D copyNode = copy.mAllNode.FirstOrDefault(n => n.mID == node.mID);
        if (copyNode != null) {
          copy.mTrunkBranchNode.Add(copyNode);
        }
      }

      // Deep copy of base splitted nodes
      foreach (BranchNode3D node in this.mBaseSplittedNode) {
        BranchNode3D copyNode = copy.mAllNode.FirstOrDefault(n => n.mID == node.mID);
        if (copyNode != null) {
          copy.mBaseSplittedNode.Add(copyNode);
        }
      }

      return copy;
    }

    public void SetNearestDist(double dist) {
      mNearestTreeDist = dist;
    }

    public void SetNearestTrees(List<Point3d> nearbyTrees) {
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
    public bool Generate(int phase, double angleMain, double angleTop, int dupNum = 0) {
      mPhase = phase;
      mAngleMain = angleMain;
      mAngleTop = angleTop;
      mBranchRelation.Clear();

      // guard
      dupNum = Math.Min(dupNum, 3);

      // Stage-based tree growing
      GrowStage1();

      if (mPhase > mStage1) {
        GrowStage2(dupNum);
      }

      if (mPhase > mStage2) {
        GrowStage3();
      }

      if (mPhase > mStage3) {
        GrowStage4();
      }

      // update radius and height
      mSoloRadius = GetRadius();
      mHeight = GetHeight();

      // scale 2D if tree size is too large (> 0.5 nearest tree distance)
      // ForestRescale();

      return true;
    }

    void AddNodeToTree(BranchNode3D curNode, BranchNode3D newNode) {
      mAllNode.Add(newNode);
      if (!mBranchRelation.ContainsKey(curNode.mID))
        mBranchRelation.Add(curNode.mID, new HashSet<int>());

      mBranchRelation[curNode.mID].Add(newNode.mID);
    }

    public void GrowStage1() {
      // auxiliary phase variable
      var auxPhaseS1 = Math.Min(mPhase, mStage1);

      // Grow trunk segments based on the current phase
      double segLen = mScaledLen * mTScale / mStage1;
      while (mTrunkSegments.Count < auxPhaseS1) {
        Point3d startPoint = mTrunkSegments.Count == 0 ? mPln.Origin : mTrunkSegments.Last().To;
        Point3d endPoint = startPoint + mPln.ZAxis * segLen;
        mTrunkSegments.Add(new Line(startPoint, endPoint));
      }

      // Update base node
      if (mBaseNode == null) {
        mBaseNode = new BranchNode3D(0, 0, mPln.Origin);
      }
      mBaseNode.mBranch.Clear();
      mBaseNode.AddBranchAlong(mTrunkSegments.Last().To - mTrunkSegments.First().From);
      mBaseNode.flagBranchSplit.Add(false);
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
      for (int segIdx = 0; segIdx < auxPhaseS1; segIdx++) {
        foreach (double posRatio in branchPositions) {
          if (segIdx == 0 && posRatio == 0.25)
            continue;  // Skip 0.25 for the first segment

          var pt = mTrunkSegments[segIdx].PointAt(posRatio);
          int curBranchLayer = segIdx * 2 + branchPositions.IndexOf(posRatio);

          for (int brNum = 0; brNum < mNumBranchPerLayer; brNum++) {
            var node = new BranchNode3D(mAllNode.Count, segIdx + 1, pt);

            // Calculate branch length based on position and growth
            double branchLen =
                (totalBranchLayer == 1 ? mMinSideBranchLen
                                       : Utils.remap(curBranchLayer, 1, totalBranchLayer,
                                                     bottomBranchLen, mMinSideBranchLen));

            // Rotation in XY-plane
            double horRotRadian = Math.PI * 2 / mNumBranchPerLayer;
            curDir.Rotate(horRotRadian, mPln.ZAxis);

            node.AddBranchAlong(curDir * branchLen);
            node.flagBranchSplit.Add(false);
            mTrunkBranchNode.Add(node);  // add the node to the trunckNode lst

            AddNodeToTree(mBaseNode, node);
          }
          // for the next layer, rotate vertically as the layers goes up
          var verRotAxis = Vector3d.CrossProduct(curDir, mPln.ZAxis);
          curDir.Rotate(verAngleIncrement, verRotAxis);

          // also rotate the starting position so that two layers don't overlap
          if (mBranchRot) {
            curDir.Rotate(Math.PI / mNumBranchPerLayer, mPln.ZAxis);
          }
        }
      }  // end of iteratively add side branches

      // we need to add a virtual branch on the top to match the same branching mechanism as the
      // side branches
      var topNode = new BranchNode3D(mAllNode.Count, auxPhaseS1, mBaseNode.mBranch[0].PointAtEnd);
      topNode.AddBranchAlong(mPln.ZAxis * 0.01);
      topNode.flagBranchSplit.Add(true);
      topNode.ToggleSplitable();

      AddNodeToTree(mBaseNode, topNode);
    }

    public void GrowStage2(int dupNum = 0) {
      // auxiliary phase variable
      var auxPhaseS2 = Math.Min(mPhase, mStage2);

      // ! phase 5-10: branching phase
      var splitInitLen = mScaledLen * 0.2;

      // Select nodes to branch: top nodes from previous phase and selected side branches
      for (int curPhase = mStage1 + 1; curPhase <= auxPhaseS2; curPhase++) {
        // Continue to grow branch emerged in Stage 1
        var addedPhase = curPhase - mStage1;
        var lenIncrementPerPhase = (mMaxSideBranchLen - mMinSideBranchLen) / mStage1;
        foreach (var node in mTrunkBranchNode) {
          if (!mBranchRelation.ContainsKey(node.mID)) {
            var tmpLst = new List<Curve>();
            foreach (var br in node.mBranch) {
              var dir = br.PointAtEnd - br.PointAtStart;
              dir.Unitize();
              var increLen = addedPhase * lenIncrementPerPhase;
              var len = Math.Min(mMaxSideBranchLen, br.GetLength() + increLen);

              tmpLst.Add(new Line(br.PointAtStart, dir * len).ToNurbsCurve());
            };
            node.mBranch = tmpLst;
          }
        }

        // for each end node, branch out several new branches
        var startNodeId = mAllNode.Count;
        var nodesToSplit = mAllNode.Where(node => node.flagSplittable == true).ToList();

        // Select additional side branches to grow (only the 1st phase of stage 2)
        if (curPhase == mStage1 + 1) {
          var sideNodeToBranch = SelectTopUnbranchedNodes(dupNum);
          sideNodeToBranch.ForEach(x => x.ToggleSplitable());
          nodesToSplit.AddRange(sideNodeToBranch);
        }

        foreach (var node in nodesToSplit) {
          node.flagBranchSplit = node.mBranch.Select(x => true).ToList();
          // auxilary line from Curve -> Line
          var parentLn = new Line(node.mBranch[0].PointAtStart, node.mBranch[0].PointAtEnd);

          var pt = parentLn.To;
          var initDir = parentLn.Direction;
          initDir.Unitize();

          // get a perpendicular vector to the current direction
          var perpVec = Vector3d.CrossProduct(initDir, (mPln.ZAxis + mPln.XAxis + mPln.YAxis));
          initDir.Rotate(Utils.ToRadian(mAngleTop), perpVec);
          initDir.Rotate(mRnd.NextDouble(), parentLn.Direction);

          var numBranchPerBranch = mRnd.Next(3, 6);
          for (int n = 0; n < numBranchPerBranch; n++) {
            initDir.Rotate(Math.PI * 2 / numBranchPerBranch, parentLn.Direction);

            // AUX: each rotation, anti-gravity growth is applied
            var auxDir = initDir;
            var auxPerpDir = Vector3d.CrossProduct(initDir, mPln.ZAxis);
            auxDir.Rotate(Math.PI * 0.05, auxPerpDir);

            var newLenth = splitInitLen * Math.Pow(0.85, curPhase - mStage1);
            var newNode = new BranchNode3D(startNodeId++, curPhase, pt);

            newNode.AddBranchAlong(auxDir * newLenth);
            newNode.flagBranchSplit.Add(true);  // make sure new node labeled split
            newNode.ToggleSplitable();
            AddNodeToTree(node, newNode);

            // store the root splitted branches
            if (curPhase == mStage1 + 1) {
              mBaseSplittedNode.Add(newNode);
            }
          }

          // after the split, toggle it so that the next iteration will not split it again
          node.ToggleSplitable();
        }
      }
    }

    public void GrowStage3() {
      // auxiliary phase variable
      var auxPhaseS3 = Math.Min(mPhase, mStage3);

      for (int curPhase = mStage2 + 1; curPhase <= auxPhaseS3; curPhase++) {
        int removeNum = (int)(mAllNode.Count * 0.3);
        int accumRm = 0;

        while (accumRm < removeNum) {
          var rmId = mRnd.Next(mAllNode.Count);
          if (mAllNode[rmId].mNodePhase < 7)
            continue;

          accumRm += mAllNode[rmId].TurnOff(mBranchRelation, mAllNode);
        }
      }
    }

    public void GrowStage4() {
      // auxiliary phase variable
      var auxPhaseS4 = Math.Min(mPhase, mStage4);

      // for the final stage, remove all the side branches and several main branches
      foreach (var node in mBaseSplittedNode) {
        if (node.mID % 3 != 0)
          node.TurnOff(mBranchRelation, mAllNode);
      }
    }

    private List<BranchNode3D> SelectTopUnbranchedNodes(int count) {
      // Get all nodes from Stage 1
      var stage1Nodes = mTrunkBranchNode.Where(node => node.mNodePhase <= mStage1).ToList();

      // Sort them by height (Z-coordinate)
      stage1Nodes.Sort((a, b) => b.mBranch[0].PointAtEnd.Z.CompareTo(a.mBranch[0].PointAtEnd.Z));
      var topOnes = stage1Nodes.Take(mNumBranchPerLayer).ToList();

      // randommly pick count Num of branches
      var res = new HashSet<BranchNode3D>();
      while (res.Count < count) {
        var idx = mRnd.Next(topOnes.Count);
        res.Add(topOnes[idx]);
      }

      return res.ToList();
    }

    // Conduct Forest Interaction between trees
    public void ForestInteract() {
      HashSet<int> scaledBranches = new HashSet<int>();

      var scaleBasePhase = 8;
      var branchesInPhase = mAllNode
                                .Where(node => node.mNodePhase == scaleBasePhase &&
                                               !scaledBranches.Contains(node.mID))
                                .ToList();

      foreach (var branch in branchesInPhase) {
        if (scaledBranches.Contains(branch.mID))
          continue;

        double scaleFactor = CalculateScaleFactor(branch);

        // Scale this branch and all its sub-branches if trees nearby affect it
        if (scaleFactor < 1.0) {
          ScaleBranchAndSubBranches(branch, scaleFactor, scaledBranches);
        }
      }
    }

    /// <summary>
    /// calculate the scaling factor for the branch and all sub branches
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    private double CalculateScaleFactor(BranchNode3D node) {
      double openingAngle = Math.PI;
      double nearestTreeDist = double.MaxValue;
      double furthestBranchDist = 0;

      // Calculate the radius to the furthest branch endpoint
      Queue<BranchNode3D> branchQueue = new Queue<BranchNode3D>();
      branchQueue.Enqueue(node);
      HashSet<int> visitedBranches = new HashSet<int>();

      while (branchQueue.Count > 0) {
        var currentBranch = branchQueue.Dequeue();
        if (visitedBranches.Contains(currentBranch.mID))
          continue;

        visitedBranches.Add(currentBranch.mID);

        foreach (var branchCurve in currentBranch.mBranch) {
          double distToEnd = node.GetPos().DistanceTo(branchCurve.PointAtEnd);
          furthestBranchDist = Math.Max(furthestBranchDist, distToEnd);
        }

        // Add sub-branches to the queue
        if (mBranchRelation.ContainsKey(currentBranch.mID)) {
          foreach (var childId in mBranchRelation[currentBranch.mID]) {
            branchQueue.Enqueue(mAllNode[childId]);
          }
        }
      }

      // Calculate the direction of the main branch
      Vector3d branchDir = node.mBranch[0].PointAtEnd - node.mBranch[0].PointAtStart;
      Vector3d branchDir2D = new Vector3d(branchDir.X, branchDir.Y, 0);
      branchDir2D.Unitize();

      // Find the nearest tree within the opening angle
      foreach (var treePt in mNearestTrees) {
        Vector3d dirToTree = treePt - node.GetPos();
        Vector3d treeDir2D = new Vector3d(dirToTree.X, dirToTree.Y, 0);
        double dist = treeDir2D.Length;
        treeDir2D.Unitize();

        double angle = Vector3d.VectorAngle(branchDir2D, treeDir2D);
        angle %= Math.PI;

        if (angle <= openingAngle / 2) {
          if (dist < nearestTreeDist) {
            nearestTreeDist = dist;
          }
        }
      }

      // Calculate scaling factor
      if (furthestBranchDist > nearestTreeDist - mSoloRadius) {
        return Math.Max((nearestTreeDist - mSoloRadius) / furthestBranchDist, 0);
      }

      return 1.0;
    }

    private void ScaleBranchAndSubBranches(BranchNode3D branch, double scaleFactor,
                                           HashSet<int> scaledBranches) {
      Queue<BranchNode3D> branchesToScale = new Queue<BranchNode3D>();
      branchesToScale.Enqueue(branch);

      while (branchesToScale.Count > 0) {
        var currentBranch = branchesToScale.Dequeue();

        if (scaledBranches.Contains(currentBranch.mID))
          continue;

        // Scale the current branch
        foreach (var branchCurve in currentBranch.mBranch) {
          var xform = Transform.Scale(mPln, scaleFactor, scaleFactor, 1);
          branchCurve.Transform(xform);
        }

        scaledBranches.Add(currentBranch.mID);

        // Add sub-branches to the queue
        if (mBranchRelation.ContainsKey(currentBranch.mID)) {
          foreach (var childId in mBranchRelation[currentBranch.mID]) {
            var childNode = mAllNode.First(n => n.mID == childId);
            branchesToScale.Enqueue(childNode);
          }
        }
      }
    }

    public double GetRadius() {
      List<double> rCollection = new List<double>();
      foreach (var node in mAllNode) {
        foreach (var ln in node.mBranch) {
          mPln.RemapToPlaneSpace(ln.PointAtStart, out var ptStart);
          mPln.RemapToPlaneSpace(ln.PointAtEnd, out var ptEnd);

          var distA = Math.Sqrt(ptStart.X * ptStart.X + ptStart.Y * ptStart.Y);
          var distB = Math.Sqrt(ptEnd.X * ptEnd.X + ptEnd.Y * ptEnd.Y);

          rCollection.Add(distA);
          rCollection.Add(distB);
        }
      }
      var maxR = rCollection.Count > 0 ? rCollection.Max() : 0;

      return maxR;
    }

    public double GetHeight() {
      double maxHeight = 0;
      var pln = this.mPln;

      var allBranches = this.GetBranch().Item1;

      foreach (var crv in allBranches.SelectMany(b => b.Value)) {
        pln.RemapToPlaneSpace(crv.PointAtStart, out Point3d start);
        pln.RemapToPlaneSpace(crv.PointAtEnd, out Point3d end);

        double yStart = start.Z;
        double yEnd = end.Z;
        maxHeight = new[] { maxHeight, yStart, yEnd }.Max();
      }
      return maxHeight;
    }

    private void ScaleBranchHierarchy(BranchNode3D node, double scaleFactor) {
      // Scale the current node's branches
      foreach (var branch in node.mBranch) {
        var xform = Transform.Scale(mPln, scaleFactor, scaleFactor, 1);
        branch.Transform(xform);
      }

      // Recursively scale child branches
      if (mBranchRelation.ContainsKey(node.mID)) {
        foreach (var childId in mBranchRelation[node.mID]) {
          var childNode = mAllNode.First(n => n.mID == childId);
          ScaleBranchHierarchy(childNode, scaleFactor);
        }
      }
    }

    /// <summary>
    ///  branch collection based on phase, and splitable
    /// </summary>
    /// <returns></returns>
    public (Dictionary<int, List<Curve>>, Dictionary<int, List<bool>>) GetBranch() {
      var branchCollection = new Dictionary<int, List<Curve>>();
      var branchSplitFlagCollection = new Dictionary<int, List<bool>>();

      foreach (var node in mAllNode) {
        if (!node.flagShow)
          continue;

        if (branchCollection.ContainsKey(node.mNodePhase)) {
          branchCollection[node.mNodePhase].AddRange(node.mBranch);
          branchSplitFlagCollection[node.mNodePhase].AddRange(node.flagBranchSplit);
        } else {
          branchCollection.Add(node.mNodePhase, node.mBranch);
          branchSplitFlagCollection.Add(node.mNodePhase, node.flagBranchSplit);
        }
      }
      return (branchCollection, branchSplitFlagCollection);
    }

    public List<Curve> GetTrunk() {
      return mBaseNode.mBranch;
    }

    public void GetCanopyVolume(out Mesh canopyMesh) {
      var ptCol = new List<Point3d>();
      foreach (var node in mAllNode) {
        if (!node.flagShow)
          continue;

        foreach (var ln in node.mBranch) {
          ptCol.Add(ln.PointAtEnd);
        }
      }
      // add the first pt in brach to extend the volume
      ptCol.Add(mAllNode[0].mBranch[0].PointAtStart);

      var cvxPt =
          ptCol.Select(p => new DefaultVertex { Position = new[] { p.X, p.Y, p.Z } }).ToList();

      canopyMesh = new Mesh();
      var hull = ConvexHull.Create(cvxPt).Result;
      var convexHullVertices = hull.Points.ToArray();

      foreach (var pt in hull.Points) {
        double[] pos = pt.Position;
        canopyMesh.Vertices.Add(new Point3d(pos[0], pos[1], pos[2]));
      }

      foreach (var f in hull.Faces) {
        int a = Array.IndexOf(convexHullVertices, f.Vertices[0]);
        int b = Array.IndexOf(convexHullVertices, f.Vertices[1]);
        int c = Array.IndexOf(convexHullVertices, f.Vertices[2]);
        canopyMesh.Faces.AddFace(a, b, c);
      }
    }

    public void GetTrunckVolume(in int curPhase, out Mesh trunkMesh) {
      var trunk = this.GetTrunk()[0];
      var trunkTop = curPhase > mStage3 ? mAllNode[13].mBranch[0].PointAtStart
                                        : mAllNode[9].mBranch[0].PointAtStart;
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
    public double mHeight;
    public string mId;

    public double mNearestTreeDist = double.MaxValue;
    public Point3d mNearestTree = new Point3d();

    // variables
    public int mStage1 = 4;
    public int mStage2 = 10;
    public int mStage3 = 12;
    public int mStage4 = 13;

    // curve collection
    BranchNode3D mBaseNode;
    public double mSoloRadius;

    public List<Point3d> mNearestTrees = new List<Point3d>();
    public Dictionary<int, HashSet<int>> mBranchRelation = new Dictionary<int, HashSet<int>>();

    // all node for branches, including the base node for trunck and all sub-nodes
    List<BranchNode3D> mAllNode { get; set; } = new List<BranchNode3D>();
    List<BranchNode3D> mTrunkBranchNode { get; set; } = new List<BranchNode3D>();
    List<BranchNode3D> mBaseSplittedNode { get; set; } = new List<BranchNode3D>();

    // all nodes that are attached to the trunck, only for 1st-level branches
    public List<Line> mTrunkSegments { get; private set; } = new List<Line>();
    public List<string> mMmsg { get; set; } = new List<string>();
  }
}
