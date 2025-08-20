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
      mMinBranchLen = height * 0.35;

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

      // Phases 1-4: Just young tree
      GrowStage1();
      // generate tree components according to its growth stage
      if (phase >= mStage1) {
        GrowStage2();
      }
      if (phase >= mStage2) {
        // Phases 5-8: Mature tree with enhanced side branch growth + top branching
        GrowStage3();
      }
      if (phase >= mStage3) {
        // Phases 9-10: Additional top-only bi-branching
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
      GenerateOutlineCurve();
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

    // Stage 2: Mature tree growth - WITH GEOMETRIC BRANCH ASSIGNMENT
    private void GrowStage2() {
      // Calculate how far into stage 2 we are
      int stage2Phase = Math.Min(mCurPhase - mStage1, mStage2 - mStage1);

      // PROGRESSIVE GROWTH: Grow side branches from 60% to 100% of their max length during Stage 2
      if (mCurPhase > mStage1) {  // Starting from phase 5
        // Calculate target progression factor for current phase
        double targetProgressionFactor = Utils.remap(mCurPhase, mStage1 + 1, mStage2, 0.6, 1.0);

        // Extend existing side branches to reach their target length
        for (int i = 0; i < mSideBranch_l.Count; i++) {
          var branch = mSideBranch_l[i];
          var dir = branch.PointAtEnd - branch.PointAtStart;
          dir.Unitize();

          // Calculate this branch's height-based maximum length
          double branchHeight = branch.PointAtStart.Y;
          double normalizedHeight = branchHeight / mHeight;
          double heightFactor = 1.0 - normalizedHeight;
          double maxLengthForThisHeight =
              mMinBranchLen + (mMaxBranchLen - mMinBranchLen) * Math.Pow(heightFactor, 1.2);

          // Calculate target length for this phase
          double targetLength = maxLengthForThisHeight * targetProgressionFactor;

          mSideBranch_l[i] = new Line(branch.PointAtStart, branch.PointAtStart + dir * targetLength)
                                 .ToNurbsCurve();
        }

        for (int i = 0; i < mSideBranch_r.Count; i++) {
          var branch = mSideBranch_r[i];
          var dir = branch.PointAtEnd - branch.PointAtStart;
          dir.Unitize();

          // Calculate this branch's height-based maximum length
          double branchHeight = branch.PointAtStart.Y;
          double normalizedHeight = branchHeight / mHeight;
          double heightFactor = 1.0 - normalizedHeight;
          double maxLengthForThisHeight =
              mMinBranchLen + (mMaxBranchLen - mMinBranchLen) * Math.Pow(heightFactor, 1.2);

          // Calculate target length for this phase
          double targetLength = maxLengthForThisHeight * targetProgressionFactor;

          mSideBranch_r[i] = new Line(branch.PointAtStart, branch.PointAtStart + dir * targetLength)
                                 .ToNurbsCurve();
        }

        // Update combined list
        mSideBranch = mSideBranch_l.Concat(mSideBranch_r).ToList();
      }

      // Create top branches using bi-branching
      Point3d topPoint = mCurTrunk.PointAtEnd;
      Plane topPlane = mPln.Clone();
      topPlane.Origin = topPoint;

      var topBranches = BiBranching(topPlane, stage2Phase);

      // FIXED: Separate left and right branches based on GEOMETRY, not strings
      foreach (var branch in topBranches) {
        if (IsBranchOnLeftSide(branch.Item1)) {
          mSubBranch_l.Add(branch.Item1);
        } else {
          mSubBranch_r.Add(branch.Item1);
        }
      }

      mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();
    }

    // Stage 3: Additional top-only bi-branching - WITH GEOMETRIC BRANCH ASSIGNMENT
    private void GrowStage3() {
      int stage3Phase = Math.Min(mCurPhase - mStage2, mStage3 - mStage2);

      // Find all the tip branches from Stage 2 (branches that don't have children)
      var tipBranches = new List<Curve>();

      // Get all existing top branches
      var allTopBranches = mSubBranch_l.Concat(mSubBranch_r).ToList();

      foreach (var branch in allTopBranches) {
        // Check if this branch is a tip (no other branch starts from its end)
        bool isTip = true;
        foreach (var otherBranch in allTopBranches) {
          if (branch != otherBranch &&
              otherBranch.PointAtStart.DistanceTo(branch.PointAtEnd) < 0.1) {
            isTip = false;
            break;
          }
        }

        if (isTip) {
          tipBranches.Add(branch);
        }
      }

      // Continue branching from each tip branch
      for (int phaseStep = 1; phaseStep <= stage3Phase; phaseStep++) {
        var newBranches = new List<Curve>();

        foreach (var branch in tipBranches) {
          // Create a plane at the tip of this branch
          Point3d branchEnd = branch.PointAtEnd;
          Vector3d branchDirection = branch.PointAtEnd - branch.PointAtStart;
          branchDirection.Unitize();

          // Calculate branch parameters for Stage 3 (smaller than Stage 2)
          double scalingParam = Math.Pow(0.7, phaseStep - 1);
          double vecLen = mHeight * 0.06 * scalingParam;
          double branchAngle = mTopBranchAngle * 0.7 * scalingParam;

          // Create two new branches from this tip
          Vector3d vecA = new Vector3d(branchDirection);
          Vector3d vecB = new Vector3d(branchDirection);

          // Rotate the branches
          vecA.Rotate(Utils.ToRadian(branchAngle), mPln.ZAxis);
          vecB.Rotate(-Utils.ToRadian(branchAngle), mPln.ZAxis);

          // Calculate endpoints
          Point3d endA = branchEnd + vecA * vecLen;
          Point3d endB = branchEnd + vecB * vecLen;

          // Create the new branch curves
          Curve newBranchA = new Line(branchEnd, endA).ToNurbsCurve();
          Curve newBranchB = new Line(branchEnd, endB).ToNurbsCurve();

          // FIXED: Add to appropriate collections based on GEOMETRY, not strings
          if (IsBranchOnLeftSide(newBranchA)) {
            mSubBranch_l.Add(newBranchA);
          } else {
            mSubBranch_r.Add(newBranchA);
          }

          if (IsBranchOnLeftSide(newBranchB)) {
            mSubBranch_l.Add(newBranchB);
          } else {
            mSubBranch_r.Add(newBranchB);
          }

          // Track as new tips for next iteration
          newBranches.Add(newBranchA);
          newBranches.Add(newBranchB);
        }

        // Update tip branches for next iteration
        tipBranches = newBranches;
      }

      // Update the combined collection
      mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();
    }

    // Stage 4: Branch removal - WITH GEOMETRIC BRANCH ASSIGNMENT
    private void GrowStage4() {
      int stage4Phase = Math.Min(mCurPhase - mStage3, mStage4 - mStage3);

      // Create a collection of all branches with hierarchy information
      var allBranches = new List<Tuple<Curve, bool, bool, int>>();

      // Add side branches with metadata - FIXED: Use geometric position check
      for (int i = 0; i < mSideBranch_l.Count; i++) {
        var curve = mSideBranch_l[i];
        bool geometricLeft = IsBranchOnLeftSide(curve);
        allBranches.Add(Tuple.Create(curve, geometricLeft, false, 1));
      }

      for (int i = 0; i < mSideBranch_r.Count; i++) {
        var curve = mSideBranch_r[i];
        bool geometricLeft = IsBranchOnLeftSide(curve);
        allBranches.Add(Tuple.Create(curve, geometricLeft, false, 1));
      }

      // Add top branches with metadata - FIXED: Use geometric position check
      for (int i = 0; i < mSubBranch_l.Count; i++) {
        var curve = mSubBranch_l[i];
        bool geometricLeft = IsBranchOnLeftSide(curve);
        int level = CalculateBranchLevel(curve);
        allBranches.Add(Tuple.Create(curve, geometricLeft, true, level));
      }

      for (int i = 0; i < mSubBranch_r.Count; i++) {
        var curve = mSubBranch_r[i];
        bool geometricLeft = IsBranchOnLeftSide(curve);
        int level = CalculateBranchLevel(curve);
        allBranches.Add(Tuple.Create(curve, geometricLeft, true, level));
      }
      // Function to generate a unique ID for a branch based on its geometry
      Func<Curve, string> getBranchId = (curve) => {
        var start = curve.PointAtStart;
        var end = curve.PointAtEnd;
        return $"{start.X:F3},{start.Y:F3},{start.Z:F3}-{end.X:F3},{end.Y:F3},{end.Z:F3}";
      };

      // Function to find all children of a branch recursively
      Func<Curve, List<Curve>> getChildrenRecursively = null;
      getChildrenRecursively = (parentBranch) => {
        var children = new List<Curve>();
        foreach (var branch in allBranches) {
          if (branch.Item1 != parentBranch &&
              branch.Item1.PointAtStart.DistanceTo(parentBranch.PointAtEnd) < 0.1) {
            children.Add(branch.Item1);
            // Recursively get children of this child
            children.AddRange(getChildrenRecursively(branch.Item1));
          }
        }
        return children;
      };

      // Use random seed for true randomness
      Random rnd = Utils.balRnd;

      // Calculate target percentage for current phase (30% for phase 11, 60% for phase 12)
      double removalPercentage = 0.3 * stage4Phase;
      int targetRemovalCount = (int)(allBranches.Count * removalPercentage);

      // Create a record of which branches to remove (use branch IDs for persistence)
      HashSet<string> branchesToRemove = new HashSet<string>();

      // For phase 12: First restore all branches removed in phase 11
      if (stage4Phase == 2) {
        var phase11Removals = BranchRemovalTracker.GetRemovedBranches(mTreeId);
        foreach (string branchId in phase11Removals) {
          branchesToRemove.Add(branchId);
        }
      }

      // For phase 11: Fresh random selection
      if (stage4Phase == 1) {
        // Clear any previous tracking for this tree to ensure fresh randomness
        BranchRemovalTracker.ClearTree(mTreeId);

        // Separate branches into categories for removal
        var sideBranches =
            allBranches.Where(b => !b.Item3).ToList();               // Not top branch = side branch
        var topBranches = allBranches.Where(b => b.Item3).ToList();  // Top branches

        // For TOP branches: Focus on top 2-3 levels (highest levels, NOT root level)
        var topBranchesByLevel = topBranches.GroupBy(b => b.Item4)
                                     .OrderByDescending(g => g.Key)  // Highest levels first
                                     .ToDictionary(g => g.Key, g => g.ToList());

        // Target top 2-3 levels for TOP branch removal
        var topTargetLevels = topBranchesByLevel.Keys.Take(3).ToList();
        var topBranchesForRemoval = new List<Tuple<Curve, bool, bool, int>>();

        foreach (int level in topTargetLevels) {
          topBranchesForRemoval.AddRange(topBranchesByLevel[level]);
        }

        // Calculate how many branches to remove from each category
        int totalAvailableForRemoval = sideBranches.Count + topBranchesForRemoval.Count;

        // Allocate removal proportionally between side and top branches
        int sideBranchesToRemove =
            (int)(targetRemovalCount * ((double)sideBranches.Count / totalAvailableForRemoval));
        int topBranchesToRemove = targetRemovalCount - sideBranchesToRemove;

        // Remove SIDE branches (every other one)
        int removedCount = 0;
        for (int i = 0; i < sideBranches.Count && removedCount < sideBranchesToRemove; i += 4) {
          var branch = sideBranches[i];
          string branchId = getBranchId(branch.Item1);
          branchesToRemove.Add(branchId);
          BranchRemovalTracker.RecordRemovedBranch(mTreeId, branchId);
          removedCount++;
        }

        // Remove TOP branches (with recursive children removal)
        var shuffledTopBranches = topBranchesForRemoval
                                      .OrderBy(
                                          _ => rnd.NextDouble())
                                      .ToList();
        int topBranchesMarked = 0;

        foreach (var branch in shuffledTopBranches) {
          if (topBranchesMarked >= topBranchesToRemove)
            break;

          string branchId = getBranchId(branch.Item1);
          if (!branchesToRemove.Contains(branchId)) {
            // Mark the parent branch for removal
            branchesToRemove.Add(branchId);
            BranchRemovalTracker.RecordRemovedBranch(mTreeId, branchId);
            topBranchesMarked++;

            // Recursively mark all children for removal
            var children = getChildrenRecursively(branch.Item1);
            foreach (var child in children) {
              string childId = getBranchId(child);
              if (!branchesToRemove.Contains(childId)) {
                branchesToRemove.Add(childId);
                BranchRemovalTracker.RecordRemovedBranch(mTreeId, childId);
              }
            }
          }
        }
      }
      // For phase 12: Add more branches to reach 60% total
      else if (stage4Phase == 2) {
        // Find branches that are NOT already marked for removal
        var availableBranches =
            allBranches.Where(b => !branchesToRemove.Contains(getBranchId(b.Item1))).ToList();

        // Calculate additional branches needed
        int additionalNeeded = targetRemovalCount - branchesToRemove.Count;

        if (additionalNeeded > 0) {
          // Separate available branches
          var availableSideBranches = availableBranches.Where(b => !b.Item3).ToList();
          var availableTopBranches = availableBranches.Where(b => b.Item3).ToList();

          // For available top branches, prioritize higher levels
          var availableTopByLevel = availableTopBranches.GroupBy(b => b.Item4)
                                        .OrderByDescending(g => g.Key)
                                        .ToDictionary(g => g.Key, g => g.ToList());

          var topTargetLevels = availableTopByLevel.Keys.Take(3).ToList();
          var availableTopForRemoval = new List<Tuple<Curve, bool, bool, int>>();

          foreach (int level in topTargetLevels) {
            availableTopForRemoval.AddRange(availableTopByLevel[level]);
          }

          // Randomly select additional branches to remove
          var shuffledTopAvailable = availableTopForRemoval
                                         .OrderBy(
                                             _ => rnd.NextDouble())
                                         .ToList();

          int additionalMarked = 0;
          foreach (var branch in shuffledTopAvailable) {
            if (additionalMarked >= additionalNeeded)
              break;

            string branchId = getBranchId(branch.Item1);
            if (!branchesToRemove.Contains(branchId)) {
              branchesToRemove.Add(branchId);
              BranchRemovalTracker.RecordRemovedBranch(mTreeId, branchId);
              additionalMarked++;

              // If it's a top branch, also remove its children
              if (branch.Item3) {
                var children = getChildrenRecursively(branch.Item1);
                foreach (var child in children) {
                  string childId = getBranchId(child);
                  if (!branchesToRemove.Contains(childId)) {
                    branchesToRemove.Add(childId);
                    BranchRemovalTracker.RecordRemovedBranch(mTreeId, childId);
                  }
                }
              }
            }
          }

          // Side branch additional removal, remove every other ones from the available
          for (int i = 0; i < availableSideBranches.Count; i += 2) {
            var branch = availableSideBranches[i];
            string branchId = getBranchId(branch.Item1);
            if (!branchesToRemove.Contains(branchId)) {
              branchesToRemove.Add(branchId);
              BranchRemovalTracker.RecordRemovedBranch(mTreeId, branchId);
            }
          }
        }
      }

      // At the end, when reconstructing the collections:
      var newSideBranchL = new List<Curve>();
      var newSideBranchR = new List<Curve>();
      var newSubBranchL = new List<Curve>();
      var newSubBranchR = new List<Curve>();

      // Keep branches that weren't marked for removal - FIXED: Use geometric position
      foreach (var branch in allBranches) {
        string branchId = getBranchId(branch.Item1);
        if (branchesToRemove.Contains(branchId))
          continue;

        // Use the geometric position check instead of the potentially wrong flag
        bool isGeometricallyLeft = IsBranchOnLeftSide(branch.Item1);
        bool isTopBranch = branch.Item3;

        if (!isTopBranch) {
          // Side branch
          if (isGeometricallyLeft)
            newSideBranchL.Add(branch.Item1);
          else
            newSideBranchR.Add(branch.Item1);
        } else {
          // Top branch
          if (isGeometricallyLeft)
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

    // Helper method to determine if a branch is on the left side of the plane
    private bool IsBranchOnLeftSide(Curve branch) {
      // Get the branch's endpoint (or midpoint for better accuracy)
      Point3d branchEnd = branch.PointAtEnd;

      // Project the branch endpoint onto the plane
      Point3d projectedPoint = mPln.ClosestPoint(branchEnd);

      // Calculate the vector from plane origin to projected point
      Vector3d vectorFromOrigin = projectedPoint - mPln.Origin;

      // Check the dot product with the plane's X-axis
      // Negative dot product = left side (-X), Positive = right side (+X)
      double dotProduct = Vector3d.Multiply(vectorFromOrigin, mPln.XAxis);

      return dotProduct < 0;  // Left side if negative
    }

    // Helper method to calculate branch level based on hierarchy
    private int CalculateBranchLevel(Curve curve) {
      // For top branches, calculate level based on connection hierarchy
      int level = 2;  // Start at level 2 for first top branches (level 1 is trunk connection)

      // Check if this branch is connected to another top branch (making it a child)
      foreach (var otherBranch in mSubBranch) {
        if (otherBranch != curve && curve.PointAtStart.DistanceTo(otherBranch.PointAtEnd) < 0.1) {
          level = Math.Max(level, CalculateBranchLevel(otherBranch) + 1);
          break;
        }
      }

      return level;
    }

    // RENAMED: Old GrowStage4 becomes GrowStageOnHold (phase 13+)
    private void GrowStageOnHold() {
      int stageOnHoldPhase = mCurPhase - mStage4;

      // For dying phase, add new growth from the base (saplings)
      if (stageOnHoldPhase == 1) {
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
      } else if (stageOnHoldPhase >= 2) {
        // For later phases, grow the saplings
        var selectedBranches = SelectBaseForNewGrowth();

        foreach (var branch in selectedBranches) {
          Plane branchPlane = mPln.Clone();
          branchPlane.Origin = branch.PointAtEnd;

          var sapling = new Tree2D(branchPlane, mHeight / 3.0);
          sapling.Draw(stageOnHoldPhase);  // Grow saplings according to stageOnHoldPhase

          mNewBornBranch.Add(sapling.mCurTrunk);
          mNewBornBranch.AddRange(sapling.mSideBranch);
          mNewBornBranch.AddRange(sapling.mSubBranch);
        }

        // In the final phases, the main tree structure degrades significantly
        if (stageOnHoldPhase >= 3) {
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

          // Split the trunk to show deterioration
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

      // WIDER ANGLES: Increase base angle from 60 to 85 degrees for more open appearance
      double baseAngle = mBaseAngle * (1 - 0.02 * (phase - 1));  // Reduced angle decrease per phase

      for (int i = 0; i < numBranchesPerSide; i++) {
        // Position along trunk (distribute evenly, avoiding very bottom)
        double posRatio = 0.3 + 0.6 * i / Math.Max(1, numBranchesPerSide - 1);
        Point3d branchPoint = mCurTrunk.PointAt(posRatio);

        // HEIGHT-BASED MAX LENGTH: Longer branches near ground, shorter higher up
        double heightFactor = 1.0 - posRatio;  // 1.0 at bottom, 0.0 at top
        double maxLengthForThisHeight =
            mMinBranchLen + (mMaxBranchLen - mMinBranchLen) * Math.Pow(heightFactor, 1.2);

        // STAGE 1 & 2 PROGRESSION: Start small, reach max by end of stage 2
        double progressionFactor;
        if (phase <= mStage1) {
          // Stage 1: 20% to 60% of max length
          progressionFactor = Utils.remap(phase, 1, mStage1, 0.2, 0.6);
        } else {
          // Will be grown further in Stage 2, so keep at 60% for now
          progressionFactor = 0.6;
        }

        double branchLength = maxLengthForThisHeight * progressionFactor;

        // IMPROVED ANGLE CALCULATION: Much more variation based on height
        // Lower branches: more horizontal (closer to 90°)
        // Upper branches: more vertical (closer to 45°)
        double heightBasedAngleFactor = 0.5 + 0.5 * heightFactor;  // 0.5 at top, 1.0 at bottom
        double angle = baseAngle * heightBasedAngleFactor;

        // Add some random variation for more natural look (±5°)
        double randomVariation = (Utils.balRnd.NextDouble() - 0.5) * 10.0;  // -5° to +5°
        angle += randomVariation;

        // Clamp angle to reasonable bounds
        angle = Math.Max(30, Math.Min(90, angle));

        // Left branch
        Vector3d leftDir = new Vector3d(mPln.YAxis);
        leftDir.Rotate(Utils.ToRadian(angle), mPln.ZAxis);
        Curve leftBranch =
            new Line(branchPoint, branchPoint + leftDir * branchLength).ToNurbsCurve();
        mSideBranch_l.Add(leftBranch);

        // Right branch (use same angle for symmetry)
        Vector3d rightDir = new Vector3d(mPln.YAxis);
        rightDir.Rotate(Utils.ToRadian(-angle), mPln.ZAxis);
        Curve rightBranch =
            new Line(branchPoint, branchPoint + rightDir * branchLength).ToNurbsCurve();
        mSideBranch_r.Add(rightBranch);
      }

      // Combine left and right branches
      mSideBranch = mSideBranch_l.Concat(mSideBranch_r).ToList();
    }

    // Generate an improved outline curve for smoother canopy
    private void GenerateOutlineCurve() {
      if (mCurPhase < mStage3) {  // Only generate canopy for non-dying trees
        // Create a list of all branch endpoints with additional smoothing points
        var points = new List<Point3d>();

        // Add trunk top
        points.Add(mCurTrunk.PointAtEnd);

        // Add side branch endpoints with interpolated smoothing points
        foreach (var branch in mSideBranch) {
          points.Add(branch.PointAtEnd);

          // Add intermediate points for smoother canopy outline
          Point3d midPoint = branch.PointAt(0.7);  // 70% along the branch
          points.Add(midPoint);
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

        // Sort points by polar angle around trunk base for smoother outline
        Point3d center = mCurTrunk.PointAtStart;
        var sortedPoints = points
                               .OrderBy(p => {
                                 Vector3d v = p - center;
                                 return Math.Atan2(v.Y, v.X);
                               })
                               .ToList();

        // Apply smoothing filter to reduce sharp angles in canopy outline
        var smoothedPoints = new List<Point3d>();
        for (int i = 0; i < sortedPoints.Count; i++) {
          Point3d prevPoint = sortedPoints[(i - 1 + sortedPoints.Count) % sortedPoints.Count];
          Point3d currentPoint = sortedPoints[i];
          Point3d nextPoint = sortedPoints[(i + 1) % sortedPoints.Count];

          // Apply weighted averaging for smoother transitions
          Point3d smoothed = currentPoint * 0.6 + (prevPoint + nextPoint) * 0.2;
          smoothedPoints.Add(smoothed);
        }

        // Create left and right canopy sections
        var leftPoints = new List<Point3d>();
        var rightPoints = new List<Point3d>();

        // Find the split point (trunk top)
        int splitIndex = smoothedPoints.FindIndex(p => p.DistanceTo(mCurTrunk.PointAtEnd) < 1e-6);

        if (splitIndex >= 0) {
          // Add points to left and right sections
          for (int i = 0; i <= splitIndex; i++) {
            leftPoints.Add(smoothedPoints[i]);
          }

          for (int i = splitIndex; i < smoothedPoints.Count; i++) {
            rightPoints.Add(smoothedPoints[i]);
          }

          // Add trunk base to close the curves
          leftPoints.Add(center);
          rightPoints.Add(center);

          // Create smoothed canopy curves
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
    private readonly int mStage2 = 8;   // Enhanced mature tree phase (5-8)
    private readonly int mStage3 = 10;  // Additional top bi-branching (9-10)
    private readonly int mStage4 = 12;  // Branch removal phase (11-12)
                                        // Stage OnHold: Phase 13+ (available for future features)

    private double mTrunkSegLen;                   // Length of trunk segment per phase
    private double mMaxBranchLen;                  // Maximum branch length
    private double mMinBranchLen;                  // Minimum branch length
    private readonly double mBaseAngle = 95;       // Base angle for side branches
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
