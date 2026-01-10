using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using MIConvexHull;

namespace BeingAliveLanguage {
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
      
      // The ratio between top and bottom max branch lengths (top is this fraction of bottom)
      mBranchLenTaperRatio = 0.6;

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
      copy.mBranchLenTaperRatio = this.mBranchLenTaperRatio;
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
      
      // Calculate the total angle in radians
      var totalAngleRad = Utils.ToRadian(mAngleMain);
      
      // Use a base angle ratio that ensures bottom branches also fold significantly
      // When angleMain is at max (90), bottom branches should start at ~30-40% of the way
      var baseAngleRatio = 0.35;  // Bottom layer starts at 35% of total angle
      var baseAngle = totalAngleRad * baseAngleRatio;
      
      // Initial rotation for the first (bottom) layer
      curDir.Rotate(baseAngle, mPln.XAxis);

      // Calculate branch length for Stage 1
      // Stage 1 should reach ~60% of the final max length
      // This leaves room for gradual growth in phases 5-10
      double stage1MaxRatio = 0.6;  // Stage 1 reaches 60% of final max
      double stage1MaxLen = mMaxSideBranchLen * stage1MaxRatio;
      double stage1MinLen = mMinSideBranchLen;
      
      double branchLenIncrement = (stage1MaxLen - stage1MinLen) / mStage1;
      var bottomBranchLen = stage1MinLen + (auxPhaseS1 * branchLenIncrement);

      // Track current layer for gradual angle calculation
      int layerCount = 0;
      
      // Stage 1 taper ratio - more aggressive than final taper to create pyramid shape
      // Top layer branches are much shorter than bottom to leave room for trunk tip branching
      double stage1TaperRatio = 0.3;  // Top branches are 30% of bottom (more aggressive than final 60%)

      // Calculate branch position on the trunk
      for (int segIdx = 0; segIdx < auxPhaseS1; segIdx++) {
        foreach (double posRatio in branchPositions) {
          if (segIdx == 0 && posRatio == 0.25)
            continue;  // Skip 0.25 for the first segment

          var pt = mTrunkSegments[segIdx].PointAt(posRatio);
          int curBranchLayer = segIdx * 2 + branchPositions.IndexOf(posRatio);

          for (int brNum = 0; brNum < mNumBranchPerLayer; brNum++) {
            var node = new BranchNode3D(mAllNode.Count, segIdx + 1, pt);

            // Apply aggressive taper for Stage 1: create pyramid/triangle shape
            // Bottom branches are full length, top branches are much shorter
            double layerRatio = (totalBranchLayer == 1) ? 0.0 
                : (double)curBranchLayer / (totalBranchLayer - 1);
            double layerTaperFactor = 1.0 - (1.0 - stage1TaperRatio) * layerRatio;
            
            double branchLen = bottomBranchLen * layerTaperFactor;

            // Rotation in XY-plane
            double horRotRadian = Math.PI * 2 / mNumBranchPerLayer;
            curDir.Rotate(horRotRadian, mPln.ZAxis);

            node.AddBranchAlong(curDir * branchLen);
            node.flagBranchSplit.Add(false);
            mTrunkBranchNode.Add(node);  // add the node to the trunckNode lst

            AddNodeToTree(mBaseNode, node);
          }
          
          // Calculate gradual angle increment for the next layer
          // Use quadratic easing: layers higher up get progressively larger increments
          // This distributes the remaining angle (1 - baseAngleRatio) across layers
          layerCount++;
          if (layerCount < totalBranchLayer) {
            // Remaining angle to distribute
            var remainingAngle = totalAngleRad * (1.0 - baseAngleRatio);
            
            // Use quadratic progression: increment grows as we go up
            // Sum of (1 + 2 + ... + n) = n*(n+1)/2, so normalize by this
            double sumOfWeights = totalBranchLayer * (totalBranchLayer + 1) / 2.0;
            double currentWeight = layerCount + 1;  // Weight increases with layer
            double verAngleIncrement = remainingAngle * currentWeight / sumOfWeights;
            
            // Rotate vertically for the next layer
            var verRotAxis = Vector3d.CrossProduct(curDir, mPln.ZAxis);
            curDir.Rotate(verAngleIncrement, verRotAxis);
          }

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

    /// <summary>
    /// Calculate the target branch length for a given node based on its height and current phase.
    /// This provides a unified length calculation across all stages for gradual growth.
    /// </summary>
    /// <param name="node">The branch node</param>
    /// <param name="currentPhase">The current growth phase</param>
    /// <returns>Target branch length for this node at this phase</returns>
    private double GetTargetBranchLen(BranchNode3D node, int currentPhase) {
      // Get the height ratio of this node (0 = bottom, 1 = top)
      double nodeHeight = 0;
      double trunkHeight = mScaledLen * mTScale;
      
      if (node.mBranch.Count > 0) {
        mPln.RemapToPlaneSpace(node.mBranch[0].PointAtStart, out Point3d localPt);
        nodeHeight = localPt.Z;
      }
      double heightRatio = Math.Clamp(nodeHeight / trunkHeight, 0.0, 1.0);
      
      // Taper transitions from aggressive (Stage 1) to final (Stage 3)
      // Stage 1 taper: 0.3 (pyramid shape)
      // Final taper: 0.6 (more uniform mature tree)
      double stage1TaperRatio = 0.3;
      double finalTaperRatio = mBranchLenTaperRatio;  // 0.6
      
      // Calculate taper factor based on phase
      double taperFactor;
      if (currentPhase <= mStage1) {
        // Phase 1-4: use aggressive Stage 1 taper
        taperFactor = 1.0 - (1.0 - stage1TaperRatio) * heightRatio;
      } else if (currentPhase <= mStage3) {
        // Phase 5-10: transition from Stage 1 taper to final taper
        double phasesInTransition = mStage3 - mStage1;
        double transitionProgress = (double)(currentPhase - mStage1) / phasesInTransition;
        // Interpolate taper ratio from stage1 to final
        double currentTaperRatio = stage1TaperRatio + (finalTaperRatio - stage1TaperRatio) * transitionProgress;
        taperFactor = 1.0 - (1.0 - currentTaperRatio) * heightRatio;
      } else {
        // Phase 11-12: use final taper
        taperFactor = 1.0 - (1.0 - finalTaperRatio) * heightRatio;
      }
      
      // Calculate the final max length for this node (at phase 10)
      double finalMaxLen = mMaxSideBranchLen * (1.0 - (1.0 - finalTaperRatio) * heightRatio);
      
      // Calculate growth progress based on phase
      // Phase 1-4: grow from minLen to 60% of finalMaxLen
      // Phase 5-10: grow from 60% to 100% of finalMaxLen (with front-loaded curve)
      // Phase 11-12: no growth (dying phase)
      
      double stage1MaxRatio = 0.6;  // Stage 1 reaches 60% of final max
      double minLen = mMinSideBranchLen * taperFactor;
      double stage1MaxLen = finalMaxLen * stage1MaxRatio;
      
      double targetLen;
      
      if (currentPhase <= mStage1) {
        // Phase 1-4: grow from minLen toward stage1MaxLen with Stage 1 taper
        double progress = (double)currentPhase / mStage1;
        // Apply Stage 1 taper to the target length
        double stage1FinalMaxLen = mMaxSideBranchLen * (1.0 - (1.0 - stage1TaperRatio) * heightRatio);
        double stage1Target = stage1FinalMaxLen * stage1MaxRatio;
        targetLen = minLen + (stage1Target - minLen) * progress;
      } else if (currentPhase <= mStage3) {
        // Phase 5-10: grow from stage1MaxLen toward finalMaxLen
        // Use front-loaded growth curve (square root) so branches grow faster early
        double phasesInMatureGrowth = mStage3 - mStage1;  // 6 phases (5,6,7,8,9,10)
        double linearProgress = (double)(currentPhase - mStage1) / phasesInMatureGrowth;
        // Square root curve: faster growth in early phases, slower as it approaches max
        double progress = Math.Sqrt(linearProgress);
        
        // Calculate what the length was at end of Stage 1
        double stage1FinalMaxLen = mMaxSideBranchLen * (1.0 - (1.0 - stage1TaperRatio) * heightRatio);
        double stage1EndLen = stage1FinalMaxLen * stage1MaxRatio;
        
        targetLen = stage1EndLen + (finalMaxLen - stage1EndLen) * progress;
      } else {
        // Phase 11-12: no growth, stay at phase 10 level
        targetLen = finalMaxLen;
      }
      
      return targetLen;
    }

    public void GrowStage2(int dupNum = 0) {
      // auxiliary phase variable
      var auxPhaseS2 = Math.Min(mPhase, mStage2);

      // ! phase 5-8: branching phase
      var splitInitLen = mScaledLen * 0.2;

      // Select nodes to branch: top nodes from previous phase and selected side branches
      for (int curPhase = mStage1 + 1; curPhase <= auxPhaseS2; curPhase++) {
        // Continue to grow branches emerged in Stage 1
        foreach (var node in mTrunkBranchNode) {
          var tmpLst = new List<Curve>();
          // Get the target length for this node at this phase
          double targetLen = GetTargetBranchLen(node, curPhase);
          
          foreach (var br in node.mBranch) {
            var currentLen = br.GetLength();
            var dir = br.PointAtEnd - br.PointAtStart;
            dir.Unitize();
            
            // Grow toward target, but never shrink
            var newLen = Math.Max(currentLen, targetLen);

            tmpLst.Add(new Line(br.PointAtStart, dir * newLen).ToNurbsCurve());
          };
          node.mBranch = tmpLst;
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
      var dupNum = 2;

      // auxiliary phase variable
      var auxPhaseS3 = Math.Min(mPhase, mStage3);

      // ! phase 9-10: mature phase - side branches continue to grow
      var splitInitLen = mScaledLen * 0.2;

      // Select nodes to branch: top nodes from previous phase and selected side branches
      for (int curPhase = mStage2 + 1; curPhase <= auxPhaseS3; curPhase++) {
        // Continue to grow ALL trunk branches during mature phases
        foreach (var node in mTrunkBranchNode) {
          var tmpLst = new List<Curve>();
          // Get the target length for this node at this phase
          double targetLen = GetTargetBranchLen(node, curPhase);
          
          foreach (var br in node.mBranch) {
            var currentLen = br.GetLength();
            var dir = br.PointAtEnd - br.PointAtStart;
            dir.Unitize();
            
            // Grow toward target, but never shrink
            var newLen = Math.Max(currentLen, targetLen);

            tmpLst.Add(new Line(br.PointAtStart, dir * newLen).ToNurbsCurve());
          };
          node.mBranch = tmpLst;
        }

        // for each end node, branch out several new branches
        var startNodeId = mAllNode.Count;
        var nodesToSplit = mAllNode.Where(node => node.flagSplittable == true).ToList();

        // Select additional side branches to grow (only the 1st phase of stage 3)
        if (curPhase == mStage2 + 1) {
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
          }

          // after the split, toggle it so that the next iteration will not split it again
          node.ToggleSplitable();
        }
      }
      //// auxiliary phase variable
      // var auxPhaseS3 = Math.Min(mPhase, mStage3);

      // for (int curPhase = mStage2 + 1; curPhase <= auxPhaseS3; curPhase++) {
      //   int removeNum = (int)(mAllNode.Count * 0.3);
      //   int accumRm = 0;

      //  while (accumRm < removeNum) {
      //    var rmId = mRnd.Next(mAllNode.Count);
      //    if (mAllNode[rmId].mNodePhase < 7)
      //      continue;

      //    accumRm += mAllNode[rmId].TurnOff(mBranchRelation, mAllNode);
      //  }
      //}
    }

    public void GrowStage4() {
      var auxPhaseS4 = Math.Min(mPhase, mStage4);

      for (int curPhase = mStage3 + 1; curPhase <= auxPhaseS4; curPhase++) {
        int removeNum = (int)(mAllNode.Count * 0.4);
        int accumRm = 0;

        while (accumRm < removeNum) {
          var rmId = mRnd.Next(mAllNode.Count);
          if (mAllNode[rmId].mNodePhase < 7)
            continue;

          accumRm += mAllNode[rmId].TurnOff(mBranchRelation, mAllNode);
        }
      }
      // auxiliary phase variable
      // var auxPhaseS4 = Math.Min(mPhase, mStage4);

      // for the final stage, remove all the side branches and several main branches
      // foreach (var node in mBaseSplittedNode) {
      //  if (node.mID % 3 != 0)
      //    node.TurnOff(mBranchRelation, mAllNode);
      //}
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

      // Start scaling from phase 6 (mature branching phase)
      var scaleBasePhase = 6;
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
      // Use 135-degree cone to determine if neighbor affects this branch
      double openingAngle = Math.PI;  // 135 degrees total (67.5 degrees each side)
      double nearestTreeDist = double.MaxValue;
      double furthestBranchDist = 0;

      // Get tree center (origin of tree plane)
      Point3d treeCenter = mPln.Origin;

      // Calculate the maximum reach of this branch hierarchy from tree center (in XY plane)
      Queue<BranchNode3D> branchQueue = new Queue<BranchNode3D>();
      branchQueue.Enqueue(node);
      HashSet<int> visitedBranches = new HashSet<int>();

      while (branchQueue.Count > 0) {
        var currentBranch = branchQueue.Dequeue();
        if (visitedBranches.Contains(currentBranch.mID))
          continue;

        visitedBranches.Add(currentBranch.mID);

        foreach (var branchCurve in currentBranch.mBranch) {
          // Calculate 2D distance from tree center to branch endpoint
          Vector3d toEnd = branchCurve.PointAtEnd - treeCenter;
          double dist2D = Math.Sqrt(toEnd.X * toEnd.X + toEnd.Y * toEnd.Y);
          furthestBranchDist = Math.Max(furthestBranchDist, dist2D);
        }

        // Add sub-branches to the queue
        if (mBranchRelation.ContainsKey(currentBranch.mID)) {
          foreach (var childId in mBranchRelation[currentBranch.mID]) {
            var childNode = mAllNode.FirstOrDefault(n => n.mID == childId);
            if (childNode != null) {
              branchQueue.Enqueue(childNode);
            }
          }
        }
      }

      // Calculate the direction of the main branch from tree center (in 2D)
      Vector3d branchDir = node.mBranch[0].PointAtEnd - treeCenter;
      Vector3d branchDir2D = new Vector3d(branchDir.X, branchDir.Y, 0);
      if (branchDir2D.Length < 0.001) {
        return 1.0;  // Branch is essentially vertical, no horizontal scaling needed
      }
      branchDir2D.Unitize();

      // Find the nearest tree and the angle to it
      double smallestAngle = double.MaxValue;

      foreach (var treePt in mNearestTrees) {
        // Direction from tree center to neighbor (in 2D)
        Vector3d dirToTree = treePt - treeCenter;
        Vector3d treeDir2D = new Vector3d(dirToTree.X, dirToTree.Y, 0);
        double dist = treeDir2D.Length;

        if (dist < 0.001)
          continue;  // Skip if neighbor is at same position
        treeDir2D.Unitize();

        // Calculate angle between branch direction and direction to neighbor
        double angle = Vector3d.VectorAngle(branchDir2D, treeDir2D);

        // Check if neighbor is within the cone of influence for this branch
        if (angle <= openingAngle / 2) {
          if (dist < nearestTreeDist) {
            nearestTreeDist = dist;
            smallestAngle = angle;
          }
        }
      }

      // If no neighbor tree affects this branch, no scaling needed
      if (nearestTreeDist == double.MaxValue) {
        return 1.0;
      }

      // Calculate available space: half the distance to neighbor (meeting point between two trees)
      // This ensures branches scale properly even when trees overlap
      double availableSpace = nearestTreeDist * 0.4;

      // Ensure minimum available space to prevent division issues
      double minAvailableSpace = mSoloRadius * 0.1;  // At least 10% of own radius
      availableSpace = Math.Max(availableSpace, minAvailableSpace);

      // Calculate the base scale factor based on space
      double baseScaleFactor = 1.0;
      if (furthestBranchDist > availableSpace) {
        // Scale down to fit in available space, with minimum of 10%
        baseScaleFactor = Math.Max(availableSpace / furthestBranchDist, 0.1);
      }

      // If no scaling needed based on space, return early
      if (baseScaleFactor >= 1.0) {
        return 1.0;
      }

      // Apply gradient based on angle:
      // - At angle = 0 (directly facing neighbor): apply full scale factor
      // - At angle = openingAngle/2 (edge of cone): blend toward 1.0 (no scaling)
      // Use smooth cosine interpolation for natural falloff
      double halfAngle = openingAngle / 2;
      double angleRatio = smallestAngle / halfAngle;  // 0 at center, 1 at edge

      // Smooth falloff using cosine curve (eases in and out)
      double blendFactor = 0.5 * (1.0 - Math.Cos(angleRatio * Math.PI));  // 0 at center, 1 at edge

      // Interpolate between baseScaleFactor (at center) and 1.0 (at edge)
      double finalScaleFactor = baseScaleFactor + blendFactor * (1.0 - baseScaleFactor);

      return finalScaleFactor;
    }

    private void ScaleBranchAndSubBranches(BranchNode3D branch, double scaleFactor,
                                           HashSet<int> scaledBranches) {
      // Process branches level by level (BFS) to ensure parents are processed before children
      // Store the new endpoint position for each scaled branch so children can attach correctly
      Dictionary<int, Point3d> newEndpoints = new Dictionary<int, Point3d>();

      Queue<(BranchNode3D node, int parentId)> branchQueue = new Queue<(BranchNode3D, int)>();
      branchQueue.Enqueue((branch, -1));  // -1 means no parent (this is the root of scaling)

      while (branchQueue.Count > 0) {
        var (currentBranch, parentId) = branchQueue.Dequeue();

        if (scaledBranches.Contains(currentBranch.mID))
          continue;

        // Process each curve in this branch node
        for (int i = 0; i < currentBranch.mBranch.Count; i++) {
          var branchCurve = currentBranch.mBranch[i];
          Point3d originalStart = branchCurve.PointAtStart;
          Point3d originalEnd = branchCurve.PointAtEnd;

          // Determine the new start point
          Point3d newStart;
          if (parentId >= 0 && newEndpoints.ContainsKey(parentId)) {
            // Attach to parent's new endpoint
            newStart = newEndpoints[parentId];
          } else {
            // Root branch or no parent info - keep original start
            newStart = originalStart;
          }

          // Calculate original branch vector and scale it
          Vector3d originalVec = originalEnd - originalStart;
          Vector3d scaledVec = originalVec * scaleFactor;

          // Calculate the new endpoint
          Point3d newEnd = newStart + scaledVec;

          // Create new scaled curve
          Line scaledLine = new Line(newStart, newEnd);
          currentBranch.mBranch[i] = scaledLine.ToNurbsCurve();
          currentBranch.mBranch[i].Domain = new Interval(0.0, 1.0);

          // Store the new endpoint for children to use
          newEndpoints[currentBranch.mID] = newEnd;
        }

        scaledBranches.Add(currentBranch.mID);

        // Add child branches to the queue
        if (mBranchRelation.ContainsKey(currentBranch.mID)) {
          foreach (var childId in mBranchRelation[currentBranch.mID]) {
            var childNode = mAllNode.FirstOrDefault(n => n.mID == childId);
            if (childNode != null) {
              branchQueue.Enqueue((childNode, currentBranch.mID));
            }
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
          // Create NEW lists instead of adding references to node.mBranch directly
          // This prevents mutation of the original node data
          branchCollection.Add(node.mNodePhase, new List<Curve>(node.mBranch));
          branchSplitFlagCollection.Add(node.mNodePhase, new List<bool>(node.flagBranchSplit));
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
    public double mBranchLenTaperRatio;  // Ratio of top branch max length to bottom branch max length
    public double mHeight;
    public string mId;

    public double mNearestTreeDist = double.MaxValue;
    public Point3d mNearestTree = new Point3d();

    // variables
    public int mStage1 = 4;
    public int mStage2 = 8;
    public int mStage3 = 10;
    public int mStage4 = 12;

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
