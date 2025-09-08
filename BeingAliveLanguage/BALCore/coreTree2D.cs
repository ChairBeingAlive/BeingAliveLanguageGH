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
    mMaxBranchLen = height * 0.7;
    mMinBranchLen = height * 0.3;

    // Generate a unique identifier for this tree instance based on position and properties
    mTreeId = GenerateTreeId();
  }

  private string GenerateTreeId() {
    // Create a unique identifier based on tree position and properties
    return $"{mPln.Origin.X:F2},{mPln.Origin.Y:F2},{mPln.Origin.Z:F2}_{mHeight:F2}";
  }

  // draw the trees
  public (bool, string) GrowToPhase(int phase) {
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

  // Stage 2: Mature tree growth - FIXED to maintain Phase 8 length for later phases
  private void GrowStage2() {
    // Calculate how far into stage 2 we are
    int stage2Phase = Math.Min(mCurPhase - mStage1, mStage2 - mStage1);

    // PROGRESSIVE GROWTH: Grow side branches from 60% to 100% of their max length during Stage 2
    // MAINTAIN PHASE 8 LENGTH FOR PHASES 9+
    if (mCurPhase > mStage1) {  // Apply to all phases 5+
      // Calculate target progression factor - but cap at phase 8's progression
      int effectivePhase = Math.Min(mCurPhase, mStage2);  // Cap at phase 8
      double targetProgressionFactor = Utils.remap(effectivePhase, mStage1 + 1, mStage2, 0.6, 1.0);

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

        // Calculate target length for this phase (capped at phase 8's length)
        double targetLength = maxLengthForThisHeight * targetProgressionFactor;

        mSideBranch_l[i] =
            new Line(branch.PointAtStart, branch.PointAtStart + dir * targetLength).ToNurbsCurve();
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

        // Calculate target length for this phase (capped at phase 8's length)
        double targetLength = maxLengthForThisHeight * targetProgressionFactor;

        mSideBranch_r[i] =
            new Line(branch.PointAtStart, branch.PointAtStart + dir * targetLength).ToNurbsCurve();
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
  }  // Stage 3: Additional top-only bi-branching - WITH GEOMETRIC BRANCH ASSIGNMENT

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
        if (branch != otherBranch && otherBranch.PointAtStart.DistanceTo(branch.PointAtEnd) < 0.1) {
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

  // Stage 4: Branch removal - SIMPLIFIED DETERMINISTIC VERSION
  private void GrowStage4() {
    int stage4Phase = Math.Min(mCurPhase - mStage3, mStage4 - mStage3);

    // DETERMINISTIC SIDE BRANCH REMOVAL - No complex tracking needed
    if (stage4Phase >= 1) {
      // Phase 11: Remove 6 side branches deterministically
      // Phase 12: Remove 10 side branches deterministically (6 + 4 more)
      int sideBranchesToRemove = (stage4Phase == 1) ? 6 : 10;

      ApplyDeterministicSideBranchRemoval(sideBranchesToRemove);
    }

    // TOP BRANCH REMOVAL - Keep existing logic but simplified
    ApplyTopBranchRemoval(stage4Phase);
  }

  // Apply deterministic side branch removal based on branch indices (Phase 11-12)
  private void ApplyDeterministicSideBranchRemoval(int totalToRemove) {
    // Collect all side branches with their original indices for deterministic removal
    var allSideBranches = new List<Tuple<Curve, int, bool>>();  // Curve, OriginalIndex, IsLeft

    // Add left side branches with their indices
    for (int i = 0; i < mSideBranch_l.Count; i++) {
      allSideBranches.Add(Tuple.Create(mSideBranch_l[i], i, true));
    }

    // Add right side branches with their indices
    for (int i = 0; i < mSideBranch_r.Count; i++) {
      allSideBranches.Add(Tuple.Create(mSideBranch_r[i], i, false));
    }

    // DETERMINISTIC REMOVAL PATTERN
    // Remove branches based on a fixed pattern to ensure consistency
    var branchesToRemove = new HashSet<Tuple<int, bool>>();  // Index, IsLeft

    if (totalToRemove == 6) {
      // Phase 11: Remove 6 branches (3 from each side)
      // Remove indices: 1, 3, 5 from each side (every other branch, skipping first)
      branchesToRemove.Add(Tuple.Create(1, true));   // Left side, index 1
      branchesToRemove.Add(Tuple.Create(3, true));   // Left side, index 3
      branchesToRemove.Add(Tuple.Create(6, true));   // Left side, index 5
      branchesToRemove.Add(Tuple.Create(0, false));  // Right side, index 1
      branchesToRemove.Add(Tuple.Create(2, false));  // Right side, index 3
      branchesToRemove.Add(Tuple.Create(7, false));  // Right side, index 5
    } else if (totalToRemove == 10) {
      // Phase 12: Remove 10 branches (5 from each side)
      // Remove previous 6 + 4 more: indices 0, 2, 4, 6 from each side
      branchesToRemove.Add(Tuple.Create(0, true));
      branchesToRemove.Add(Tuple.Create(1, true));
      branchesToRemove.Add(Tuple.Create(3, true));
      branchesToRemove.Add(Tuple.Create(5, true));
      branchesToRemove.Add(Tuple.Create(6, true));
      branchesToRemove.Add(Tuple.Create(0, false));
      branchesToRemove.Add(Tuple.Create(2, false));
      branchesToRemove.Add(Tuple.Create(4, false));
      branchesToRemove.Add(Tuple.Create(5, false));
      branchesToRemove.Add(Tuple.Create(7, false));
    }

    // Apply the removal pattern
    var newSideBranchL = new List<Curve>();
    var newSideBranchR = new List<Curve>();

    foreach (var branchData in allSideBranches) {
      var curve = branchData.Item1;
      var index = branchData.Item2;
      var isLeft = branchData.Item3;

      // Keep branch if it's not in the removal set
      if (!branchesToRemove.Contains(Tuple.Create(index, isLeft))) {
        if (isLeft) {
          newSideBranchL.Add(curve);
        } else {
          newSideBranchR.Add(curve);
        }
      }
    }

    // Update side branch collections
    mSideBranch_l = newSideBranchL;
    mSideBranch_r = newSideBranchR;
    mSideBranch = mSideBranch_l.Concat(mSideBranch_r).ToList();
  }

  // Apply top branch removal with CORRECTED hierarchical logic - DON'T REMOVE LEVEL 2
  // (trunk-attached)
  private void ApplyTopBranchRemoval(int stage4Phase) {
    if (mSubBranch.Count == 0)
      return;  // No top branches to remove

    // Create a collection of top branches with hierarchy information
    var allTopBranches = new List<Tuple<Curve, bool, int>>();  // Curve, isLeft, level

    // Add top branches with calculated levels
    for (int i = 0; i < mSubBranch_l.Count; i++) {
      var curve = mSubBranch_l[i];
      bool geometricLeft = IsBranchOnLeftSide(curve);
      int level = CalculateBranchLevel(curve);
      allTopBranches.Add(Tuple.Create(curve, geometricLeft, level));
    }

    for (int i = 0; i < mSubBranch_r.Count; i++) {
      var curve = mSubBranch_r[i];
      bool geometricLeft = IsBranchOnLeftSide(curve);
      int level = CalculateBranchLevel(curve);
      allTopBranches.Add(Tuple.Create(curve, geometricLeft, level));
    }

    // Function to generate a unique ID for a branch based on its geometry
    Func<Curve, string> getBranchId = (curve) => {
      var start = curve.PointAtStart;
      var end = curve.PointAtEnd;
      return $"{start.X:F3},{start.Y:F3},{start.Z:F3}-{end.X:F3},{end.Y:F3},{end.Z:F3}";
    };

    // Function to find all descendants of a branch recursively
    Func<Curve, List<Curve>> getAllDescendants = null;
    getAllDescendants = (parentBranch) => {
      var descendants = new List<Curve>();
      foreach (var branch in allTopBranches) {
        if (branch.Item1 != parentBranch &&
            branch.Item1.PointAtStart.DistanceTo(parentBranch.PointAtEnd) < 0.1) {
          descendants.Add(branch.Item1);
          // Recursively get all descendants of this child
          descendants.AddRange(getAllDescendants(branch.Item1));
        }
      }
      return descendants;
    };

    // Use random seed for top branch removal
    Random rnd = Utils.balRnd;

    // Create a record of which top branches to remove
    HashSet<string> topBranchesToRemove = new HashSet<string>();

    // For phase 12: First restore all branches removed in phase 11
    if (stage4Phase == 2) {
      var phase11Removals = BranchRemovalTracker.GetRemovedBranches(mTreeId);
      foreach (string branchId in phase11Removals) {
        topBranchesToRemove.Add(branchId);
      }
    }

    // For phase 11: Remove 30% focusing on highest + second highest levels (EXCLUDE LEVEL 2)
    if (stage4Phase == 1) {
      BranchRemovalTracker.ClearTree(mTreeId);

      // Group branches by level and identify highest and second highest (EXCLUDE LEVEL 2)
      var branchesByLevel = allTopBranches
                                .Where(b => b.Item3 > 2)  // EXCLUDE LEVEL 2 (trunk-attached)
                                .GroupBy(b => b.Item3)
                                .OrderByDescending(g => g.Key)
                                .ToDictionary(g => g.Key, g => g.ToList());

      var levels = branchesByLevel.Keys.ToList();
      if (levels.Count >= 2) {
        int highestLevel = levels[0];
        int secondHighestLevel = levels[1];

        // Get branches from highest and second highest levels (both are > level 2)
        var targetBranches = new List<Tuple<Curve, bool, int>>();
        targetBranches.AddRange(branchesByLevel[highestLevel]);
        targetBranches.AddRange(branchesByLevel[secondHighestLevel]);

        // Calculate 30% of total branches (including level 2 in the count, but not in removal)
        int targetRemovalCount = (int)(allTopBranches.Count * 0.30);

        // Randomly select parent branches to remove (with all their descendants)
        var shuffledTargets = targetBranches
                                  .OrderBy(
                                      _ => rnd.NextDouble())
                                  .ToList();

        int removedCount = 0;
        foreach (var parentBranch in shuffledTargets) {
          if (removedCount >= targetRemovalCount)
            break;

          string parentId = getBranchId(parentBranch.Item1);
          if (!topBranchesToRemove.Contains(parentId)) {
            // Remove parent
            topBranchesToRemove.Add(parentId);
            BranchRemovalTracker.RecordRemovedBranch(mTreeId, parentId);
            removedCount++;

            // Remove ALL descendants
            var descendants = getAllDescendants(parentBranch.Item1);
            foreach (var descendant in descendants) {
              string descendantId = getBranchId(descendant);
              if (!topBranchesToRemove.Contains(descendantId)) {
                topBranchesToRemove.Add(descendantId);
                BranchRemovalTracker.RecordRemovedBranch(mTreeId, descendantId);
                removedCount++;
              }
            }
          }
        }
      }
    }
    // For phase 12: Add another 30% total, focusing on levels 3-4 (NOT 2-3)
    else if (stage4Phase == 2) {
      // Find available branches not already removed (EXCLUDE LEVEL 2)
      var availableBranches =
          allTopBranches
              .Where(b => !topBranchesToRemove.Contains(getBranchId(b.Item1)) && b.Item3 > 2)
              .ToList();

      // Calculate total target (60% total, minus what's already removed)
      int totalTarget = (int)(allTopBranches.Count * 0.60);
      int additionalNeeded = totalTarget - topBranchesToRemove.Count;

      if (additionalNeeded > 0) {
        // Focus on levels 3-4 for phase 12 (NOT 2-3, since we exclude level 2)
        var level3and4Branches =
            availableBranches.Where(b => b.Item3 == 3 || b.Item3 == 4).ToList();

        // If not enough in 3-4, expand to any level > 2
        if (level3and4Branches.Count < 2) {
          level3and4Branches = availableBranches.ToList();
        }

        // Randomly select 1-2 parent branches from levels 3-4+
        var shuffledLevel3and4 = level3and4Branches
                                     .OrderBy(
                                         _ => rnd.NextDouble())
                                     .ToList();

        int parentBranchesToRemove =
            Math.Min(2,
                     Math.Min(shuffledLevel3and4.Count,
                              (additionalNeeded + 5) / 6));  // Estimate parent count needed

        int removedCount = 0;
        foreach (var parentBranch in shuffledLevel3and4.Take(parentBranchesToRemove)) {
          if (removedCount >= additionalNeeded)
            break;

          string parentId = getBranchId(parentBranch.Item1);
          if (!topBranchesToRemove.Contains(parentId)) {
            // Remove parent
            topBranchesToRemove.Add(parentId);
            BranchRemovalTracker.RecordRemovedBranch(mTreeId, parentId);
            removedCount++;

            // Remove ALL descendants
            var descendants = getAllDescendants(parentBranch.Item1);
            foreach (var descendant in descendants) {
              string descendantId = getBranchId(descendant);
              if (!topBranchesToRemove.Contains(descendantId)) {
                topBranchesToRemove.Add(descendantId);
                BranchRemovalTracker.RecordRemovedBranch(mTreeId, descendantId);
                removedCount++;
              }
            }
          }
        }
      }
    }

    // Apply top branch removal
    var newSubBranchL = new List<Curve>();
    var newSubBranchR = new List<Curve>();

    foreach (var branch in allTopBranches) {
      string branchId = getBranchId(branch.Item1);
      if (topBranchesToRemove.Contains(branchId))
        continue;

      bool isGeometricallyLeft = IsBranchOnLeftSide(branch.Item1);
      if (isGeometricallyLeft) {
        newSubBranchL.Add(branch.Item1);
      } else {
        newSubBranchR.Add(branch.Item1);
      }
    }

    // Update top branch collections
    mSubBranch_l = newSubBranchL;
    mSubBranch_r = newSubBranchR;
    mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();
  }  // Helper method to determine if a branch is on the left side of the plane
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
        sapling.GrowToPhase(1);  // Start with phase 1

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
        sapling.GrowToPhase(stageOnHoldPhase);  // Grow saplings according to stageOnHoldPhase

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

        mCurTrunk = new PolylineCurve(new List<Point3d> { leftTrunk.PointAtStart,
                                                          leftTrunk.PointAtEnd,
                                                          rightTrunk.PointAtEnd,
                                                          rightTrunk.PointAtStart });
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

    // WIDER ANGLES: Increase base angle from 60 to 95 degrees for more open appearance
    double baseAngle = mBaseAngle * (1 - 0.02 * (phase - 1));  // Reduced angle decrease per phase

    for (int i = 0; i < numBranchesPerSide; i++) {
      // Position along trunk (distribute evenly, avoiding very bottom)
      double posRatio = 0.3 + 0.6 * i / Math.Max(1, numBranchesPerSide - 1);
      Point3d branchPoint = mCurTrunk.PointAt(posRatio);

      // HEIGHT-BASED MAX LENGTH: Longer branches near ground, shorter higher up
      double heightFactor = Math.Pow(1.0 - posRatio, 0.7);  // 1.0 at bottom, 0.0 at top
      double maxLengthForThisHeight =
          mMinBranchLen + (mMaxBranchLen - mMinBranchLen) * Math.Pow(heightFactor, 2.5);

      // ENHANCED CURVED SILHOUETTE: More aggressive reduction for upper branches
      double curvedLengthFactor = 0.7 + 0.3 * Math.Sin(posRatio * Math.PI);
      maxLengthForThisHeight *= curvedLengthFactor;

      //// STAGE 1 & 2 PROGRESSION: Start small, reach max by end of stage 2
      double progressionFactor = 0.5;
      if (phase <= mStage1) {
        // Stage 1: 20% to 60% of max length
        progressionFactor = Utils.remap(phase, 1, mStage1, 0.2, progressionFactor);
      }

      double branchLength = maxLengthForThisHeight * progressionFactor;

      // IMPROVED ANGLE CALCULATION: Much more variation based on height
      // Lower branches: more horizontal (closer to 90°)
      // Upper branches: more vertical (closer to 45°)
      double heightBasedAngleFactor = 0.5 + 0.5 * heightFactor;  // 0.5 at top, 1.0 at bottom
      double angle = baseAngle * heightBasedAngleFactor;

      // RANDOM VARIATION ONLY DURING INITIAL GENERATION - NOT AFTER PHASE 8
      // Add some random variation for more natural look during initial creation only
      double randomVariation = (Utils.balRnd.NextDouble() - 0.5) * 10.0;  // -5° to +5°
      angle += randomVariation;

      // Clamp angle to reasonable bounds
      angle = Math.Max(30, Math.Min(90, angle));

      // Left branch
      Vector3d leftDir = new Vector3d(mPln.YAxis);
      leftDir.Rotate(Utils.ToRadian(angle), mPln.ZAxis);
      Curve leftBranch = new Line(branchPoint, branchPoint + leftDir * branchLength).ToNurbsCurve();
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

  // SIMPLIFIED: Generate phase-specific canopy outline curves - TWO ARCS ONLY
  private void GenerateOutlineCurve() {
    // Always clear existing canopy
    mCurCanopy = null;
    mCurCanopy_l = null;
    mCurCanopy_r = null;

    if (mCurPhase <= mStage3) {
      // PHASES 1-8: Arcs from bottom side branch tips
      GenerateCanopyArcs();
    }
    // Phase 10+ (mStage3+): No canopy generated (dying trees)
  }

  // COMBINED: Generate canopy arcs based on current phase
  private void GenerateCanopyArcs() {
    Point3d leftTip, rightTip;
    Point3d meetingPoint;

    if (mCurPhase <= mStage1) {
      // PHASES 1-4: Use side branch tips
      if (mSideBranch_l.Count == 0 || mSideBranch_r.Count == 0)
        return;

      // Find the bottom (longest) branches - these should be at index 0
      leftTip =
          mSideBranch_l.First().PointAtEnd + mSideBranch_l.First().TangentAtEnd * 0.05 * mHeight;
      rightTip =
          mSideBranch_r.First().PointAtEnd + mSideBranch_r.First().TangentAtEnd * 0.05 * mHeight;

      // Meeting point above trunk top for young trees (fixed height)
      Point3d trunkTop = mCurTrunk.PointAtEnd;
      double arcHeight = mHeight * 0.1;
      meetingPoint = trunkTop + mPln.YAxis * arcHeight;
    } else {
      // PHASES 5+: Use top branch tips
      if (mSubBranch_l.Count == 0 || mSubBranch_r.Count == 0)
        return;

      // Find the outermost top branch tips
      leftTip = FindOutermostTopBranchTip(mSubBranch_l) +
                mSideBranch_l.Last().TangentAtEnd * 0.05 * mHeight;
      rightTip = FindOutermostTopBranchTip(mSubBranch_r) +
                 mSideBranch_r.Last().TangentAtEnd * 0.05 * mHeight;

      // DYNAMIC MEETING POINT: Find the highest point of all top branches
      double highestY = Math.Max(leftTip.Y, rightTip.Y);

      // Check all top branches to find the absolute highest point
      foreach (var branch in mSubBranch) {
        double branchHighestY = branch.PointAtEnd.Y;
        if (branchHighestY > highestY) {
          highestY = branchHighestY;
        }
      }

      // Meeting point above the highest top branch with additional height
      double additionalHeight = mHeight * 0.05;  // 5% of tree height above highest branch
      meetingPoint = new Point3d(mPln.Origin.X, highestY + additionalHeight, mPln.Origin.Z);
    }

    // Create left arc (from left tip to meeting point)
    mCurCanopy_l = CreateArc(leftTip, meetingPoint, true);

    // Create right arc (from right tip to meeting point)
    mCurCanopy_r = CreateArc(rightTip, meetingPoint, false);

    // Join the arcs to form complete canopy
    if (mCurCanopy_l != null && mCurCanopy_r != null) {
      var joined = Curve.JoinCurves(new List<Curve> { mCurCanopy_l, mCurCanopy_r }, 0.02);
      if (joined.Length > 0) {
        mCurCanopy = joined[0];
      }
    }
  }

  // Helper method to find the outermost (furthest from center) top branch tip
  private Point3d FindOutermostTopBranchTip(List<Curve> branches) {
    Point3d outermostTip = branches[0].PointAtEnd;
    double maxDistance = Math.Abs(Vector3d.Multiply(outermostTip - mPln.Origin, mPln.XAxis));

    foreach (var branch in branches) {
      Point3d tip = branch.PointAtEnd;
      double distance = Math.Abs(Vector3d.Multiply(tip - mPln.Origin, mPln.XAxis));
      if (distance > maxDistance) {
        maxDistance = distance;
        outermostTip = tip;
      }
    }

    return outermostTip;
  }

  // Helper method to create a single arc between two points
  private Curve CreateArc(Point3d startPoint, Point3d endPoint, bool isLeftSide) {
    try {
      // Calculate the midpoint
      Point3d midPoint = (startPoint + endPoint) * 0.5;

      // Create a control point for the arc curvature
      // Offset the midpoint perpendicular to the line between start and end
      Vector3d lineDirection = endPoint - startPoint;
      lineDirection.Unitize();

      // Create perpendicular vector in the plane
      Vector3d perpendicular = Vector3d.CrossProduct(lineDirection, mPln.ZAxis);
      perpendicular.Unitize();
      perpendicular *= isLeftSide ? -1 : 1;

      // Offset the midpoint to create arc curvature
      double arcDepth = startPoint.DistanceTo(endPoint) * 0.125;  // 30% of chord length
      Point3d controlPoint = midPoint + perpendicular * arcDepth;

      // Create a 3-point arc using start, control, and end points
      Arc arc = new Arc(startPoint, controlPoint, endPoint);
      return arc.ToNurbsCurve();
    } catch {
      // Fallback to straight line if arc creation fails
      return new Line(startPoint, endPoint).ToNurbsCurve();
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
