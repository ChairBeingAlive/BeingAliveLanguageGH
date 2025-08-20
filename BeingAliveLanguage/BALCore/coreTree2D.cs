using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace BeingAliveLanguage {
  public class BranchRemovalTracker {
    private static readonly Dictionary<string, HashSet<string>> _treeRemovals =
        new Dictionary<string, HashSet<string>>();
    private static readonly object _lock = new object();

    public static void RecordRemovedBranch(string treeId, string branchId) {
      lock (_lock) {
        if (!_treeRemovals.ContainsKey(treeId)) {
          _treeRemovals[treeId] = new HashSet<string>();
        }
        _treeRemovals[treeId].Add(branchId);
      }
    }

    public static bool IsBranchRemoved(string treeId, string branchId) {
      lock (_lock) {
        return _treeRemovals.ContainsKey(treeId) && _treeRemovals[treeId].Contains(branchId);
      }
    }

    public static HashSet<string> GetRemovedBranches(string treeId) {
      lock (_lock) {
        return _treeRemovals.ContainsKey(treeId) ? new HashSet<string>(_treeRemovals[treeId])
                                                 : new HashSet<string>();
      }
    }

    public static void ClearTree(string treeId) {
      lock (_lock) {
        _treeRemovals.Remove(treeId);
      }
    }

    public static void ClearAll() {
      lock (_lock) {
        _treeRemovals.Clear();
      }
    }
  }

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

      // Generate a unique identifier for this tree instance based on position and properties
      mTreeId = GenerateTreeId();
    }

    private string GenerateTreeId() {
      // Create a unique identifier based on tree position and properties
      return $"{mPln.Origin.X:F2},{mPln.Origin.Y:F2},{mPln.Origin.Z:F2}_{mHeight:F2}";
    }

    // draw the trees
    public (bool, string) Draw(int phase) {
      // record current phase
      mCurPhase = phase;

      // validate input parameters
      if (mHeight <= 0)
        return (false, "The height of the tree needs to be > 0.");
      if (phase > mStage4 || phase <= 0)
        return (false, "Phase out of range ([1, 13] for tree).");

      // clear previous data
      ClearTreeData();

      // generate tree components according to its growth stage
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
      // Calculate how far into stage 2 we are, capped at the maximum stage 2 length
      int stage2Phase = Math.Min(mCurPhase - mStage1 + 1, mStage2 - mStage1 + 1);

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
      var allBranches =
          new List<Tuple<Curve, bool, List<int>, bool>>();  // Curve, isLeft, childIndices,
                                                            // isTopBranch

      // Add side branches with metadata
      for (int i = 0; i < mSideBranch_l.Count; i++) {
        var curve = mSideBranch_l[i];
        allBranches.Add(Tuple.Create(curve, true, new List<int>(), false));
      }

      for (int i = 0; i < mSideBranch_r.Count; i++) {
        var curve = mSideBranch_r[i];
        allBranches.Add(Tuple.Create(curve, false, new List<int>(), false));
      }

      // Add top branches with metadata
      int sideBranchCount = allBranches.Count;
      for (int i = 0; i < mSubBranch_l.Count; i++) {
        var curve = mSubBranch_l[i];
        allBranches.Add(Tuple.Create(curve, true, new List<int>(), true));
      }

      for (int i = 0; i < mSubBranch_r.Count; i++) {
        var curve = mSubBranch_r[i];
        allBranches.Add(Tuple.Create(curve, false, new List<int>(), true));
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

      // Function to generate a unique ID for a branch based on its geometry
      Func<Curve, string> getBranchId = (curve) => {
        var start = curve.PointAtStart;
        var end = curve.PointAtEnd;
        return $"{start.X:F3},{start.Y:F3},{start.Z:F3}-{end.X:F3},{end.Y:F3},{end.Z:F3}";
      };

      // Use random seed for true randomness
      Random rnd = Utils.balRnd;

      // Separate branches by type and side
      var leftTopBranches = new List<int>();
      var rightTopBranches = new List<int>();
      var leftSideBranches = new List<int>();
      var rightSideBranches = new List<int>();

      for (int i = 0; i < allBranches.Count; i++) {
        bool isLeft = allBranches[i].Item2;
        bool isTopBranch = allBranches[i].Item4;

        if (isTopBranch) {
          if (isLeft)
            leftTopBranches.Add(i);
          else
            rightTopBranches.Add(i);
        } else {
          if (isLeft)
            leftSideBranches.Add(i);
          else
            rightSideBranches.Add(i);
        }
      }

      // Calculate target percentage for current phase (10% per phase - cumulative)
      double removalPercentage = 0.1 * stage3Phase;
      int targetRemovalCount = (int)(allBranches.Count * removalPercentage);

      // Create a record of which branches to remove
      HashSet<int> branchesToRemove = new HashSet<int>();

      // Function to recursively mark branches for removal
      void MarkBranchAndChildren(int branchIndex) {
        if (branchesToRemove.Contains(branchIndex))
          return;

        branchesToRemove.Add(branchIndex);
        string branchId = getBranchId(allBranches[branchIndex].Item1);
        BranchRemovalTracker.RecordRemovedBranch(mTreeId, branchId);  // Track globally

        // Recursively mark child branches
        foreach (int childIndex in allBranches[branchIndex].Item3) {
          MarkBranchAndChildren(childIndex);
        }
      }

      // Check if we're in phase 10 and have previously removed branches
      bool hasPhase9Removals = false;
      if (stage3Phase == 2) {
        // Check if we have any branches removed in phase 9
        for (int i = 0; i < allBranches.Count; i++) {
          string branchId = getBranchId(allBranches[i].Item1);
          if (BranchRemovalTracker.IsBranchRemoved(mTreeId, branchId)) {
            branchesToRemove.Add(i);
            hasPhase9Removals = true;
          }
        }
      }

      // For phase 9: Always do random selection (fresh start)
      if (stage3Phase == 1) {
        // Clear any previous tracking for this tree to ensure fresh randomness
        BranchRemovalTracker.ClearTree(mTreeId);

        // Shuffle each branch group randomly
        Func<List<int>, List<int>> shuffleList = (list) => list.OrderBy(
                                                                   _ => rnd.NextDouble())
                                                               .ToList();

        var shuffledLeftTop = shuffleList(leftTopBranches);
        var shuffledRightTop = shuffleList(rightTopBranches);
        var shuffledLeftSide = shuffleList(leftSideBranches);
        var shuffledRightSide = shuffleList(rightSideBranches);

        // Balance removal across all four categories for 10% total
        int leftTopToRemove = Math.Min(shuffledLeftTop.Count / 2, targetRemovalCount / 4);
        int rightTopToRemove = Math.Min(shuffledRightTop.Count / 2, targetRemovalCount / 4);
        int leftSideToRemove = Math.Min(shuffledLeftSide.Count / 2,
                                        targetRemovalCount - leftTopToRemove - rightTopToRemove);
        int rightSideToRemove =
            targetRemovalCount - leftTopToRemove - rightTopToRemove - leftSideToRemove;

        // Helper to mark branches while respecting total count
        void MarkBranchesUpToCount(List<int> branches, int count) {
          int marked = 0;
          foreach (int idx in branches) {
            if (marked >= count)
              break;
            if (!branchesToRemove.Contains(idx)) {
              int countBefore = branchesToRemove.Count;
              MarkBranchAndChildren(idx);
              marked += (branchesToRemove.Count - countBefore);
            }
          }
        }

        // Mark branches from each category
        MarkBranchesUpToCount(shuffledLeftTop, leftTopToRemove);
        MarkBranchesUpToCount(shuffledRightTop, rightTopToRemove);
        MarkBranchesUpToCount(shuffledLeftSide, leftSideToRemove);
        MarkBranchesUpToCount(shuffledRightSide, rightSideToRemove);
      }
      // For phase 10: Add more branches on top of phase 9 removals
      else if (stage3Phase == 2) {
        if (!hasPhase9Removals) {
          // This shouldn't happen, but if phase 9 was never run, treat it as phase 9
          BranchRemovalTracker.ClearTree(mTreeId);
          stage3Phase = 1;  // Fallback to phase 9 logic

          // Repeat phase 9 logic
          Func<List<int>, List<int>> shuffleList = (list) => list.OrderBy(
                                                                     _ => rnd.NextDouble())
                                                                 .ToList();
          var shuffledLeftTop = shuffleList(leftTopBranches);
          var shuffledRightTop = shuffleList(rightTopBranches);
          var shuffledLeftSide = shuffleList(leftSideBranches);
          var shuffledRightSide = shuffleList(rightSideBranches);

          int phase9Target = (int)(allBranches.Count * 0.1);
          int leftTopToRemove = Math.Min(shuffledLeftTop.Count / 2, phase9Target / 4);
          int rightTopToRemove = Math.Min(shuffledRightTop.Count / 2, phase9Target / 4);
          int leftSideToRemove = Math.Min(shuffledLeftSide.Count / 2,
                                          phase9Target - leftTopToRemove - rightTopToRemove);
          int rightSideToRemove =
              phase9Target - leftTopToRemove - rightTopToRemove - leftSideToRemove;

          void MarkBranchesUpToCount(List<int> branches, int count) {
            int marked = 0;
            foreach (int idx in branches) {
              if (marked >= count)
                break;
              if (!branchesToRemove.Contains(idx)) {
                int countBefore = branchesToRemove.Count;
                MarkBranchAndChildren(idx);
                marked += (branchesToRemove.Count - countBefore);
              }
            }
          }

          MarkBranchesUpToCount(shuffledLeftTop, leftTopToRemove);
          MarkBranchesUpToCount(shuffledRightTop, rightTopToRemove);
          MarkBranchesUpToCount(shuffledLeftSide, leftSideToRemove);
          MarkBranchesUpToCount(shuffledRightSide, rightSideToRemove);
        }

        // Now add additional removals for phase 10 (to reach 20% total)
        var availableBranches = new List<int>();
        for (int i = 0; i < allBranches.Count; i++) {
          if (!branchesToRemove.Contains(i)) {
            availableBranches.Add(i);
          }
        }

        // Randomly shuffle available branches
        availableBranches = availableBranches
                                .OrderBy(
                                    _ => rnd.NextDouble())
                                .ToList();

        // Mark additional branches to reach our 20% target
        int additionalNeeded = targetRemovalCount - branchesToRemove.Count;
        int marked = 0;
        foreach (int idx in availableBranches) {
          if (marked >= additionalNeeded)
            break;

          int countBefore = branchesToRemove.Count;
          MarkBranchAndChildren(idx);
          marked += (branchesToRemove.Count - countBefore);
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
        bool isTopBranch = branch.Item4;

        if (!isTopBranch) {
          // Side branch
          if (isLeft)
            newSideBranchL.Add(branch.Item1);
          else
            newSideBranchR.Add(branch.Item1);
        } else {
          // Top branch
          if (isLeft)
            newSubBranchL.Add(branch.Item1);
          else
            newSubBranchR.Add(branch.Item1);
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

    // Optional: Method to clear this tree's removal history (useful for reset)
    public void ClearRemovalHistory() {
      BranchRemovalTracker.ClearTree(mTreeId);
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
          int idx = Utils.balRnd.Next(lowerBranches.Count);
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

    // tree core param
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

    // curve collection
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

    // Tree identifier for persistent tracking
    private string mTreeId;
  }

}
