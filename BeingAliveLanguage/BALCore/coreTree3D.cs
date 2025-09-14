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

  public Tree3D(Plane pln,
                double globalScale,
                double trunkScale,
                int seed = 0,
                bool branchRot = false,
                string id = "") {
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
    Tree3D copy = new Tree3D(this.mPln.Clone(),  // Clone the plane
                             this.mGScale,
                             this.mTScale,
                             this.mRnd.Next(),  // Use a new random seed derived from current random
                             this.mBranchRot,
                             this.mId);

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
      BranchNode3D newNode = new BranchNode3D(
          originalNode.mID, originalNode.mNodePhase, new Point3d(originalNode.GetPos()));

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
              (totalBranchLayer == 1
                   ? mMinSideBranchLen
                   : Utils.remap(
                         curBranchLayer, 1, totalBranchLayer, bottomBranchLen, mMinSideBranchLen));

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
    var dupNum = 2;

    // auxiliary phase variable
    var auxPhaseS3 = Math.Min(mPhase, mStage3);

    // ! phase 5-10: branching phase
    var splitInitLen = mScaledLen * 0.2;

    // Select nodes to branch: top nodes from previous phase and selected side branches
    for (int curPhase = mStage2 + 1; curPhase <= auxPhaseS3; curPhase++) {
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

    var scaleBasePhase = 8;
    var branchesInPhase =
        mAllNode
            .Where(node => node.mNodePhase == scaleBasePhase && !scaledBranches.Contains(node.mID))
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

  private void
  ScaleBranchAndSubBranches(BranchNode3D branch, double scaleFactor, HashSet<int> scaledBranches) {
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
