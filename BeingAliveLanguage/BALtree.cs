using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace BeingAliveLanguage
{
  /// <summary>
  /// 2D version of the tree component.
  /// </summary>
  public class BALtree : GH_Component
  {
    public BALtree()
    : base("Tree", "balTree",
          "Generate the BAL tree model.",
          "BAL", "03::plant")
    { }

    string modeUnitary = "non-unitary";
    protected override System.Drawing.Bitmap Icon => Properties.Resources.balTree;
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
        tInfoLst.Add(new TreeProperty(t.mPln, t.mHeight, t.mCurPhase));
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
  public class BALtree3D : GH_Component
  {
    public BALtree3D()
    : base("Tree3D", "balTree3D",
          "Generate the BAL tree model in 3D, using Drenou's model.",
          "BAL", "03::plant")
    { }

    //string modeUnitary = "non-unitary";
    protected override System.Drawing.Bitmap Icon => Properties.Resources.balTree;
    public override Guid ComponentGuid => new Guid("36c5e013-321b-4064-b007-b17880644ce4");

    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
      pManager.AddPlaneParameter("Plane", "P", "Base plane(s) where the tree(s) is drawn.", GH_ParamAccess.list, Plane.WorldXY);
      pManager.AddNumberParameter("Scale", "S", "Scale of the tree.", GH_ParamAccess.list);
      pManager.AddIntegerParameter("Phase", "phase", "Phase of the tree's growth.", GH_ParamAccess.list);
    }

    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
      pManager.AddCurveParameter("Trunk", "T", "Tree trunk curves.", GH_ParamAccess.tree);
      pManager.AddCurveParameter("Branches", "B", "Tree branch curves.", GH_ParamAccess.tree);
      //pManager.AddCurveParameter("Canopy", "C", "Tree canopy curves.", GH_ParamAccess.tree);
      //pManager.AddCurveParameter("SideBranch", "SB", "Tree side branch curves.", GH_ParamAccess.tree);
      //pManager.AddCurveParameter("TopBranch", "TB", "Tree top branch curves.", GH_ParamAccess.tree);
      //pManager.AddCurveParameter("BabyBranch", "BB", "Tree baby branch at dying phase curves.", GH_ParamAccess.tree);

      pManager.AddGenericParameter("TreeInfo", "Tinfo", "Information about the tree.", GH_ParamAccess.list);
      //pManager.AddCurveParameter("Debug", "debug", "Debug curves.", GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var plnLst = new List<Plane>();
      if (!DA.GetDataList("Plane", plnLst))
      { return; }

      var scaleLst = new List<double>();
      if (!DA.GetDataList("Scale", scaleLst))
      { return; }

      foreach (var s in scaleLst)
      {
        if (s <= 0)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Scale should be positive.");
          return;
        }
      };

      var phaseLst = new List<int>();
      if (!DA.GetDataList("Phase", phaseLst))
      { return; }

      foreach (var p in phaseLst)
      {
        if (p < 0)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Phase should be non-negative.");
          return;
        }
      };

      //! 1. determine horizontal scaling factor of the trees
      //var tscal = new List<Tuple<double, double>>();
      var distLst = new List<double>();
      //var treeCol = new List<Tree>();
      Dictionary<int, List<Curve>> branchCol = new Dictionary<int, List<Curve>>();
      Dictionary<int, List<Curve>> trunkCol = new Dictionary<int, List<Curve>>();

      if (plnLst.Count == 0)
        return;
      else if (plnLst.Count >= 1)
      {
        if (scaleLst.Count == 1)
          scaleLst = Enumerable.Repeat(scaleLst[0], plnLst.Count).ToList();
        else if (scaleLst.Count != plnLst.Count)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Height # does not match Plane #, please check.");
        }

        if (phaseLst.Count == 1)
          phaseLst = Enumerable.Repeat(phaseLst[0], plnLst.Count).ToList();
        else if (phaseLst.Count != plnLst.Count)
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
        //for (int i = 0; i < plnLst.Count - 1; i++)
        //{
        //  var dis = Math.Abs(plnLst[i].Origin.DistanceTo(plnLst[i + 1].Origin));
        //  distLst.Add(dis);
        //}

        //! 2. draw the trees, collect tree width
        foreach (var (pln, i) in plnLst.Select((pln, i) => (pln, i)))
        {
          var t = new Tree3D(pln, scaleLst[i]);
          t.Generate(phaseLst[i]);
          t.GetBranch(ref branchCol);
          trunkCol.Add(i, t.GetTrunk());
          //var res = t.Draw(phaseLst[i]);

          //if (!res.Item1)
          //{
          //  AddRuntimeMessage(GH_RuntimeMessageLevel.Error, res.Item2);
          //  return;
          //}

          //treeCol.Add(t);
          //widCol.Add(t.CalWidth());
        }
      }

      // collection trunk
      DataTree<Curve> trCrv = new DataTree<Curve>();
      foreach (var tr in trunkCol)
      {
        trCrv.AddRange(tr.Value, new GH_Path(tr.Key));
      }

      // collection branches
      DataTree<Curve> brCrv = new DataTree<Curve>();
      foreach (var br in branchCol)
      {
        brCrv.AddRange(br.Value, new GH_Path(br.Key));
      }

      DA.SetDataTree(0, trCrv);
      DA.SetDataTree(1, brCrv);
    }
  }
}
