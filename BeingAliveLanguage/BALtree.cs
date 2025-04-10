using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using BeingAliveLanguage.BalCore;

namespace BeingAliveLanguage
{
  /// <summary>
  /// 2D version of the tree component.
  /// </summary>
  public class BALtreeRaimbault : GH_Component
  {
    public BALtreeRaimbault()
    : base("Tree_Raimbault", "balTree_Raimbault",
          "Generate the BAL tree using Raimbault's architectural model.",
          "BAL", "03::plant")
    { }

    string modeUnitary = "non-unitary";
    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTree);
    public override Guid ComponentGuid => new Guid("930148B1-014A-43AA-845C-FB0C711D6AA0");

    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
      pManager.AddPlaneParameter("Plane", "P", "Base plane(s) where the tree(s) is drawn.", GH_ParamAccess.list, Plane.WorldXY);
      pManager.AddNumberParameter("Height", "H", "Height of the tree.", GH_ParamAccess.list);
      pManager.AddIntegerParameter("Phase", "phase", "Phase of the tree.", GH_ParamAccess.list);
    }

    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
      pManager.AddCurveParameter("Circumference", "C", "Circumference Ellipses that controls the boundary of the tree.", GH_ParamAccess.list);
      pManager.AddCurveParameter("Trunk", "T", "Tree trunk curves.", GH_ParamAccess.tree);
      pManager.AddCurveParameter("Canopy", "C", "Tree canopy curves.", GH_ParamAccess.tree);
      pManager.AddCurveParameter("SideBranch", "SB", "Tree side branch curves.", GH_ParamAccess.tree);
      pManager.AddCurveParameter("TopBranch", "TB", "Tree top branch curves.", GH_ParamAccess.tree);
      pManager.AddCurveParameter("BabyBranch", "BB", "Tree baby branch at dying phase curves.", GH_ParamAccess.tree);

      pManager.AddGenericParameter("TreeInfo", "Tinfo", "Information about the tree.", GH_ParamAccess.list);
      //pManager.AddCurveParameter("Debug", "debug", "Debug curves.", GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var plnLst = new List<Rhino.Geometry.Plane>();
      if (!DA.GetDataList("Plane", plnLst))
      { return; }

      var hLst = new List<double>();
      if (!DA.GetDataList("Height", hLst))
      { return; }

      var phLst = new List<int>();
      if (!DA.GetDataList("Phase", phLst))
      { return; }

      var circ = new List<Curve>();
      var canopy = new List<Curve>();
      var trunk = new List<Curve>();
      var sideB = new List<Curve>();
      var topB = new List<Curve>();
      var babyB = new List<Curve>();
      var tInfoLst = new List<TreeProperty>();

      //! 1. determine horizontal scaling factor of the trees
      var tscal = new List<Tuple<double, double>>();
      var distLst = new List<double>();
      var treeCol = new List<Tree>();

      if (plnLst.Count == 0)
        return;
      else if (plnLst.Count > 1)
      {
        if (hLst.Count == 1)
          hLst = Enumerable.Repeat(hLst[0], plnLst.Count).ToList();
        else if (hLst.Count != plnLst.Count)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Height # does not match Plane #, please check.");
        }

        if (phLst.Count == 1)
          phLst = Enumerable.Repeat(phLst[0], plnLst.Count).ToList();
        else if (phLst.Count != plnLst.Count)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Phase # does not match Plane #, please check.");
        }

        // ! sort root location 
        plnLst.Sort((pln0, pln1) =>
        {
          Vector3d res = pln0.Origin - pln1.Origin;
          if (Math.Abs(res[0]) > 1e-5)
            return pln0.OriginX.CompareTo(pln1.OriginX);
          else if (Math.Abs(res[1]) > 1e-5)
            return pln0.OriginY.CompareTo(pln1.OriginY);
          else // align on z axis or overlap, use the same criteria
            return pln0.OriginZ.CompareTo(pln1.OriginZ);
        });

        // after list length check:
        for (int i = 0; i < plnLst.Count - 1; i++)
        {
          var dis = Math.Abs(plnLst[i].Origin.DistanceTo(plnLst[i + 1].Origin));
          distLst.Add(dis);
        }

        //! 2. draw the trees, collect tree width
        var widCol = new List<double>();
        foreach (var (pln, i) in plnLst.Select((pln, i) => (pln, i)))
        {
          var t = new Tree(pln, hLst[i], modeUnitary == "unitary");
          var res = t.Draw(phLst[i]);

          if (!res.Item1)
          {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, res.Item2);
            return;
          }

          treeCol.Add(t);
          widCol.Add(t.CalWidth());
        }

        //! 3. calculate scaling factor between trees
        var inbetweenScale = new List<double>();
        for (int i = 0; i < widCol.Count - 1; i++)
        {
          inbetweenScale.Add(Math.Min(1, distLst[i] / ((widCol[i] + widCol[i + 1]) * 0.5)));
        }

        //! 4. generate scaling Tuple for each tree
        tscal.Add(Tuple.Create(1.0, inbetweenScale[0]));
        for (int i = 0; i < inbetweenScale.Count - 1; i++)
        {
          tscal.Add(Tuple.Create(inbetweenScale[i], inbetweenScale[i + 1]));
        }
        tscal.Add(Tuple.Create(inbetweenScale.Last(), 1.0));
      }
      else if (plnLst.Count == 1) // special case: only one tree
      {
        tscal.Add(Tuple.Create(1.0, 1.0));
        treeCol.Add(new Tree(plnLst[0], hLst[0], modeUnitary == "unitary"));
        var res = treeCol.Last().Draw(phLst[0]);

        if (!res.Item1)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, res.Item2);
          return;
        }
      }


      //! 5. scale each tree and output
      foreach (var (t, i) in treeCol.Select((t, i) => (t, i)))
      {
        t.Scale(tscal[i]);

        // output the curves 
        trunk.Add(t.mCurTrunk);
        canopy.Add(t.mCurCanopy);
        circ.AddRange(t.mCircCol);
        sideB.AddRange(t.mSideBranch);
        topB.AddRange(t.mSubBranch);
        babyB.AddRange(t.mNewBornBranch);
      }

      //! 6. compose tree info for downstream compoment usage
      foreach (var t in treeCol)
      {
        tInfoLst.Add(new TreeProperty(t.mPln, t.mCurPhase, t.mHeight, t.mRadius, 1));
      }

      DA.SetDataList("Circumference", circ);
      DA.SetDataList("Canopy", canopy);
      DA.SetDataList("Trunk", trunk);
      DA.SetDataList("SideBranch", sideB);
      DA.SetDataList("TopBranch", topB);
      DA.SetDataList("BabyBranch", babyB);
      DA.SetDataList("TreeInfo", tInfoLst);
    }


    private bool CheckMode(string _modeCheck) => modeUnitary == _modeCheck;
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
      base.AppendAdditionalMenuItems(menu);
      Menu_AppendSeparator(menu);
      Menu_AppendItem(menu, "Tree Type:", (sender, e) => { }, false).Font = GH_FontServer.StandardItalic;
      Menu_AppendItem(menu, " Unitary", (sender, e) => Menu.SelectMode(this, sender, e, ref modeUnitary, "unitary"), true, CheckMode("unitary"));
      Menu_AppendItem(menu, " Non-Unitary", (sender, e) => Menu.SelectMode(this, sender, e, ref modeUnitary, "non-unitary"), true, CheckMode("non-unitary"));
    }

    public override bool Write(GH_IWriter writer)
    {
      if (modeUnitary != "")
        writer.SetString("modeUnitary", modeUnitary);
      return base.Write(writer);
    }
    public override bool Read(GH_IReader reader)
    {
      if (reader.ItemExists("modeUnitary"))
        modeUnitary = reader.GetString("modeUnitary");

      Message = reader.GetString("modeUnitary").ToUpper();

      return base.Read(reader);
    }
  }


  /// <summary>
  /// The 3D version of the tree component.
  /// </summary>
  /// <summary>
  /// The first part of the 3D tree component - generates tree objects.
  /// </summary>
  public class BALtreeDrenouComposer : GH_Component
  {
    public BALtreeDrenouComposer()
    : base("Tree_Drenou_Composer", "balTree_DrenouCom",
          "Compose tree objects using Drenou's architectural model.",
          "BAL", "03::plant")
    { }

    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTreeComposer);
    public override Guid ComponentGuid => new Guid("23af7e5d-7c82-48e6-9c7a-fb7d36e8451f");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddPlaneParameter("Plane", "P", "Base plane(s) where the tree(s) is drawn.", GH_ParamAccess.item, Plane.WorldXY);
      pManager.AddNumberParameter("GlobalScale", "globalS", "Global scale of the tree.", GH_ParamAccess.item, 1);
      pManager.AddNumberParameter("TrunkScale", "trunkS", "Trunk scale of the tree.", GH_ParamAccess.item, 1);
      pManager.AddNumberParameter("SpreadAngleMain", "angMain", "Spread angle of the primary tree branches.", GH_ParamAccess.item, 50);
      pManager.AddNumberParameter("SpreadAngleTop", "angTop", "Spread angle of the secontary tree branches (the top part).", GH_ParamAccess.item, 35);
      pManager.AddIntegerParameter("Phase", "phase", "Phase of the tree's growth.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Seed", "seed", "Seed for random number to varify the tree shape.", GH_ParamAccess.item, 0);
      pManager.AddBooleanParameter("BranchRotation", "brRot", "Whether to rotate the branches sequentially.", GH_ParamAccess.item, false);
      pManager.AddIntegerParameter("DuplicateNumber", "dupNum", "[0-3] Number of top side branches for duplicate branching.", GH_ParamAccess.item, 0);
      pManager.AddTextParameter("Id", "id", "An optional id of the tree or tree group, used for selecting trees in the same group in the renderer.", GH_ParamAccess.item, "");
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Tree Objects", "Tree_Out", "Tree objects that can be passed to renderer or interaction components.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      #region Input Check
      var pln = new Plane();
      if (!DA.GetData("Plane", ref pln))
        return;

      double gScale = 1;
      if (!DA.GetData("GlobalScale", ref gScale))
        return;
      if (gScale <= 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Global scale should be positive.");
        return;
      }

      double tScale = 1;
      if (!DA.GetData("TrunkScale", ref tScale))
        return;
      if (tScale <= 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Trunk scale should be positive.");
        return;
      }

      double angMain = 0;
      if (!DA.GetData("SpreadAngleMain", ref angMain))
      { return; }

      if (angMain < 0 || angMain > 90)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Angle should be within [0, 90].");
        return;
      }

      double angTop = 0;
      if (!DA.GetData("SpreadAngleTop", ref angTop))
      { return; }

      if (angTop < 0 || angTop > 90)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Angle should be within [0, 90].");
        return;
      }

      int phase = 1;
      if (!DA.GetData("Phase", ref phase))
      { return; }

      if (phase < 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Phase should be non-negative.");
        return;
      }

      int seed = 1;
      if (!DA.GetData("Seed", ref seed))
      { return; }

      if (seed < 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Seed should be non-negative.");
        return;
      }

      bool brRot = false;
      if (!DA.GetData("BranchRotation", ref brRot))
      { return; }

      int dupNum = 1;
      if (!DA.GetData("DuplicateNumber", ref dupNum))
      { return; }
      if (dupNum < 0 || dupNum > 3)
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "DuplicateNumber is out of range [0, 3], please check.");

      string id = "";
      if (!DA.GetData("Id", ref id))
      { return; }

      #endregion

      // Generate tree objects
      var curTree = new Tree3D(pln, gScale, tScale, seed, brRot, id);
      curTree.Generate(phase, angMain, angTop, dupNum);

      var treeObjects = new Tree3DWrapper(curTree);
      DA.SetData("Tree Objects", treeObjects);
    }
  }


  /// <summary>
  /// Wrapper class for Tree3D to hold additional information
  /// </summary>
  public class Tree3DWrapper : IGH_Goo
  {
    public Tree3D Tree { get; private set; }
    public int Phase { get; private set; }
    public double Height { get; private set; }
    public double Radius { get; private set; }

    public Tree3DWrapper()
    {
      Tree = null;
      Phase = 0;
      Height = 0;
      Radius = 0;
    }

    public Tree3DWrapper(Tree3D tree)
    {
      Tree = tree;
      Phase = tree.mPhase;
      Height = tree.mHeight;
      Radius = tree.mSoloRadius;
    }

    #region IGH_Goo implementation
    public bool IsValid => Tree != null;
    public string IsValidWhyNot => Tree == null ? "Tree object is null" : string.Empty;
    public string TypeName => "Tree3D Wrapper";
    public string TypeDescription => "Wrapper for Tree3D objects with phase information";

    public IGH_Goo Duplicate()
    {
      return new Tree3DWrapper(Tree);
    }

    public bool CastFrom(object source)
    {
      if (source is Tree3D tree)
      {
        Tree = tree;
        return true;
      }
      if (source is Tree3DWrapper wrapper)
      {
        Tree = wrapper.Tree;
        Phase = wrapper.Phase;
        return true;
      }
      return false;
    }

    public bool CastTo<T>(out T target)
    {
      if (typeof(T) == typeof(Tree3D))
      {
        target = (T)(object)Tree;
        return true;
      }
      target = default;
      return false;
    }

    public object ScriptVariable()
    {
      return Tree;
    }

    public override string ToString()
    {
      return $"Tree3D (Phase: {Phase})";
    }

    public IGH_GooProxy EmitProxy()
    {
      throw new NotImplementedException();
    }

    public bool Write(GH_IWriter writer)
    {
      throw new NotImplementedException();
    }

    public bool Read(GH_IReader reader)
    {
      throw new NotImplementedException();
    }
    #endregion
  }


  /// <summary>
  /// The second part of the 3D tree component - renders tree objects.
  /// </summary>
  public class BALtreeDrenouRenderer : GH_Component
  {
    public BALtreeDrenouRenderer()
    : base("Tree_Drenou_Renderer", "balTree_DrenouRender",
          "Render tree objects generated in Drenou's architectural model.",
          "BAL", "03::plant")
    { }

    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTreeRenderer);
    public override Guid ComponentGuid => new Guid("fb920ed6-7c50-466b-8645-04eb5658a7f1");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGenericParameter("Tree Objects", "Tree_In", "Tree objects generated by to render.", GH_ParamAccess.tree);
      pManager.AddTextParameter("Id", "id", "An optional id of the tree or tree group, used for selecting trees in the renderer.", GH_ParamAccess.item, "");
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddCurveParameter("Trunk", "T", "Tree trunk curves.", GH_ParamAccess.tree);
      pManager.AddCurveParameter("SingleBranch", "SB", "Tree side branch curves (non-split).", GH_ParamAccess.tree);
      pManager.AddCurveParameter("SplitBranch", "TB", "Tree top branch and duplicated branch curves (splitted).", GH_ParamAccess.tree);
      pManager.AddGenericParameter("TreeInfo", "Tinfo", "Information about the tree.", GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      // Get input data tree of tree objects
      if (!DA.GetDataTree("Tree Objects", out GH_Structure<IGH_Goo> inputTree))
      { return; }

      string selectionId = "";
      if (!DA.GetData("Id", ref selectionId))
      { return; }


      // Initialize output data trees
      DataTree<Curve> trunkCrv = new DataTree<Curve>(); // trunk
      DataTree<Curve> singleBrCrv = new DataTree<Curve>(); // side branches
      DataTree<Curve> splitBrCrv = new DataTree<Curve>(); // top branches
      DataTree<TreeProperty> treeInfoTree = new DataTree<TreeProperty>(); // tree info

      // Process each path in the input data structure
      foreach (GH_Path inputPath in inputTree.Paths)
      {
        // Get all trees in current branch
        var trees = inputTree.get_Branch(inputPath);

        for (int treeIdx = 0; treeIdx < trees.Count; treeIdx++)
        {
          Tree3DWrapper treeWrapper = trees[treeIdx] as Tree3DWrapper;
          if (treeWrapper == null || !treeWrapper.IsValid)
          {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Some tree object from the input is invalid.");
            continue;
          }

          if (treeWrapper.Tree.mId != selectionId)
            continue;

          // use the input path for placing objects
          var treePath = inputPath;

          var trunk = treeWrapper.Tree.GetTrunk();
          trunkCrv.AddRange(trunk, treePath);

          // Process branches
          var (branches, splitFlags) = treeWrapper.Tree.GetBranch();
          int maxPhase = branches.Keys.Max();

          foreach (var phase in branches.Keys)
          {
            var phasePath = treePath.AppendElement(phase);
            var phaseBranches = branches[phase];
            var phaseFlags = splitFlags[phase];

            for (int i = 0; i < phaseBranches.Count; i++)
            {
              var targetTree = phaseFlags[i] ? splitBrCrv : singleBrCrv;
              targetTree.Add(phaseBranches[i], phasePath);
            }
          }

          // Calculate tree height
          treeInfoTree.Add(new TreeProperty(
            treeWrapper.Tree.mPln,
            treeWrapper.Phase,
            treeWrapper.Height,
            treeWrapper.Radius,
            treeWrapper.Tree.mScaledLen
            ),
            treePath);
        }
      }

      // Add tree info to corresponding path in output tree
      DA.SetDataTree(0, trunkCrv);
      DA.SetDataTree(1, singleBrCrv);
      DA.SetDataTree(2, splitBrCrv);
      DA.SetDataTree(3, treeInfoTree);
    }
  }


  public class BALtreeInteraction : GH_Component
  {
    public BALtreeInteraction()
      : base("Forest Interaction", "balForest",
          "Create interactions between trees based on space, shading, etc. to simulate tree status in a forest.",
          "BAL", "03::plant")
    { }

    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTreeInteraction);
    public override Guid ComponentGuid => new Guid("31624b38-fb3c-4028-9b92-dfd654a8337f");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGenericParameter("T -> Interaction", "tToInteract", "The collection of tree objects in various species before interaction.", GH_ParamAccess.list);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Interaction -> T", "tAfterInteract", "The collection of tree objects in various species after interaction.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      // Get the input tree objects
      List<Tree3DWrapper> treeWrappers = new List<Tree3DWrapper>();
      if (!DA.GetDataList("T -> Interaction", treeWrappers) || treeWrappers.Count == 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No tree objects provided.");
        return;
      }

      // Extract Tree3D objects and collect their positions
      List<Tree3D> trees = new List<Tree3D>();
      List<Point3d> treePositions = new List<Point3d>();
      List<Tree3DWrapper> processedTrees = new List<Tree3DWrapper>();

      foreach (var wrapper in treeWrappers)
      {
        // check if wrapper is valid
        if (wrapper != null && wrapper.IsValid)
        {
          trees.Add(wrapper.Tree.Copy());
          treePositions.Add(wrapper.Tree.mPln.Origin);
        }
        else
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "One or more tree objects are invalid, these trees are omitted.");
        }

        // Check for overlapping trees or trees that are too close
        for (int i = 0; i < treePositions.Count; i++)
        {
          double minDist = 0.5 * trees[i].GetRadius();
          for (int j = i + 1; j < treePositions.Count; j++)
          {
            if (treePositions[i].DistanceTo(treePositions[j]) < minDist)
            {
              AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                  $"Trees at positions {i} and {j} are too close or overlapping.");
            }
          }
        }
      }

      // For each tree, find its nearest neighbors
      List<List<Point3d>> nearestNeighbors = new List<List<Point3d>>();
      Utils.GetLstNearestPoint(treePositions, out nearestNeighbors, 6);

      // Apply the interaction effects
      if (nearestNeighbors.Count == 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No forest interaction happens. Either trees have no neighbour, or something wrong happens.");
        processedTrees = treeWrappers;
      }
      else
      {
        for (int i = 0; i < trees.Count; i++)
        {
          // Set the nearest trees for this tree
          trees[i].SetNearestTrees(nearestNeighbors[i]);
          trees[i].ForestInteract();
          processedTrees.Add(new Tree3DWrapper(trees[i]));
        }
      }

      // Output the processed trees
      DA.SetDataList("Interaction -> T", processedTrees);
    }
  }

  /// <summary>
  /// The canopy component for the 3D tree (mesh format).
  /// </summary>
  public class BALtreeEnergyCanopy : GH_Component
  {
    public BALtreeEnergyCanopy()
      : base("Tree Energy Canopy", "balEnergyCanopy",
                   "Generate the energy canopy of the 3D tree for energy analysis.",
                             "BAL", "03::plant")
    { }

    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTree3DVolume);
    public override Guid ComponentGuid => new Guid("73fc1cbd-f7b7-4b0c-ad39-cc88bbfdf385");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddCurveParameter("Trunk", "T", "Tree trunk curves.", GH_ParamAccess.tree);
      pManager[0].Optional = true;

      pManager.AddNumberParameter("TrunkRadius", "T-r", "Tree trunk radius, for creating different sized trunk.", GH_ParamAccess.tree, 1);
      pManager[1].Optional = true;

      pManager.AddCurveParameter("Branch", "B", "Tree branch curves.", GH_ParamAccess.tree);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddMeshParameter("EnergyVolume", "E", "Energy volume for energy analysis.", GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      #region input check

      //var plnLst = new List<Plane>();
      if (!DA.GetDataTree("Trunk", out GH_Structure<GH_Curve> trunckCol))
      { return; }
      if (!DA.GetDataTree("TrunkRadius", out GH_Structure<GH_Number> trunckRadiiCol))
      { return; }
      if (!DA.GetDataTree("Branch", out GH_Structure<GH_Curve> branchCol))
      { return; }

      // Check if there is only one element in trunkRadiiCol
      if (trunckRadiiCol.PathCount == 1)
      {
        var singleRadius = trunckRadiiCol.get_Branch(0)[0] as GH_Number;
        trunckRadiiCol = new GH_Structure<GH_Number>();
        for (int i = 0; i < trunckCol.PathCount; i++)
        {
          trunckRadiiCol.Append(singleRadius, new GH_Path(i));
        }
      }

      // Check if the path count of branchCol matches that of trunckCol
      if (branchCol.PathCount != trunckCol.PathCount)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The path count of Branch does not match the path count of Trunk.");
        return;
      }

      #endregion

      List<Mesh> canopyVolLst = new List<Mesh>();
      List<Mesh> trunkVolLst = new List<Mesh>();
      DataTree<Mesh> energyVolTree = new DataTree<Mesh>();

      // take tree structure as input and generate energy canopy for each GH-tree-branch
      for (int i = 0; i < branchCol.PathCount; i++)
      {
        var pth = branchCol.Paths[i];
        var brPtCol = new List<Point3d>();

        List<GH_Curve> brLst = branchCol.get_Branch(pth) as List<GH_Curve>;
        brLst.ForEach(x =>
        {
          brPtCol.Add(x.Value.PointAtStart);
          brPtCol.Add(x.Value.PointAtEnd);
        });

        var brMesh = BalCore.MeshUtils.CreateCvxHull(brPtCol);


        Mesh energyMesh = new Mesh();
        if (trunckCol.PathExists(pth))
        {
          var trCrv = trunckCol.get_Branch(pth)[0] as GH_Curve;
          var trR = trunckRadiiCol.get_Branch(pth)[0] as GH_Number;

          var trunkMesh = BalCore.MeshUtils.CreateCylinderMesh(trCrv.Value, trR.Value);
          energyMesh = BalCore.MeshUtils.MergeMeshes(ref brMesh, ref trunkMesh);
        }
        else
        {
          energyMesh = brMesh;
        }


        // Energy Canopy
        if (!energyMesh.IsValid)
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Canopy mesh is not valid. Please check.");

        energyVolTree.Add(energyMesh, new GH_Path(i));
        ;
      }

      DA.SetDataTree(0, energyVolTree);
    }
  }
}

