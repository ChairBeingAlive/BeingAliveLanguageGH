﻿using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    protected override System.Drawing.Bitmap Icon => Properties.Resources.balTree3D;
    public override Guid ComponentGuid => new Guid("36c5e013-321b-4064-b007-b17880644ce4");

    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
      pManager.AddPlaneParameter("Plane", "P", "Base plane(s) where the tree(s) is drawn.", GH_ParamAccess.list, Plane.WorldXY);
      pManager.AddNumberParameter("GlobalScale", "globalS", "Global scale of the tree.", GH_ParamAccess.list, 1);
      pManager.AddNumberParameter("TrunkScale", "trunkS", "Trunk scale of the tree.", GH_ParamAccess.list, 1);
      pManager.AddNumberParameter("SpreadAngleMain", "angMain", "Spread angle of the primary tree branches.", GH_ParamAccess.list, 50);
      pManager.AddNumberParameter("SpreadAngleTop", "angTop", "Spread angle of the secontary tree branches (the top part).", GH_ParamAccess.list, 35);
      pManager.AddIntegerParameter("Phase", "phase", "Phase of the tree's growth.", GH_ParamAccess.list);
      pManager.AddIntegerParameter("Seed", "seed", "Seed for random number to varify the tree shape.", GH_ParamAccess.list, 0);
      pManager.AddBooleanParameter("BranchRotation", "brRot", "Whether to rotate the branches sequentially.", GH_ParamAccess.list, false);
    }

    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
      pManager.AddCurveParameter("Trunk", "T", "Tree trunk curves.", GH_ParamAccess.tree);
      pManager.AddCurveParameter("Branches", "B", "Tree branch curves.", GH_ParamAccess.tree);
      pManager.AddMeshParameter("EnergyVolume", "E", "Energy volume for energy analysis.", GH_ParamAccess.list);

      //pManager.AddGenericParameter("TreeInfo", "Tinfo", "Information about the tree.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      #region Input Check
      var plnLst = new List<Plane>();
      if (!DA.GetDataList("Plane", plnLst))
      { return; }

      var gsLst = new List<double>();
      if (!DA.GetDataList("GlobalScale", gsLst))
      { return; }

      foreach (var s in gsLst)
      {
        if (s <= 0)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Global scale should be positive.");
          return;
        }
      };

      if (gsLst.Count == 1)
        gsLst = Enumerable.Repeat(gsLst[0], plnLst.Count).ToList();
      else if (gsLst.Count != plnLst.Count)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Global scale # does not match Plane #, please check.");
      }

      var tsLst = new List<double>();
      if (!DA.GetDataList("TrunkScale", tsLst))
      { return; }

      foreach (var s in tsLst)
      {
        if (s <= 0)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Trunk scale should be positive.");
          return;
        }
      };
      if (tsLst.Count == 1)
        tsLst = Enumerable.Repeat(tsLst[0], plnLst.Count).ToList();
      else if (tsLst.Count != plnLst.Count)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Trunk scale # does not match Plane #, please check.");
      }

      var angLstMain = new List<double>();
      if (!DA.GetDataList("SpreadAngleMain", angLstMain))
      { return; }

      foreach (var a in angLstMain)
      {
        if (a < 0 || a > 90)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Angle should be within [0, 90].");
          return;
        }
      };
      if (angLstMain.Count == 1)
        angLstMain = Enumerable.Repeat(angLstMain[0], plnLst.Count).ToList();
      else if (angLstMain.Count != plnLst.Count)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Spread angle # does not match Plane #, please check.");
      }

      var angLstTop = new List<double>();
      if (!DA.GetDataList("SpreadAngleTop", angLstTop))
      { return; }

      foreach (var a in angLstTop)
      {
        if (a < 0 || a > 90)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Angle should be within [0, 90].");
          return;
        }
      };
      if (angLstTop.Count == 1)
        angLstTop = Enumerable.Repeat(angLstTop[0], plnLst.Count).ToList();
      else if (angLstTop.Count != plnLst.Count)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Spread angle # does not match Plane #, please check.");
      }

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
      if (phaseLst.Count == 1)
        phaseLst = Enumerable.Repeat(phaseLst[0], plnLst.Count).ToList();
      else if (phaseLst.Count != plnLst.Count)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Phase # does not match Plane #, please check.");
      }

      var seedLst = new List<int>();
      if (!DA.GetDataList("Seed", seedLst))
      { return; }

      foreach (var s in seedLst)
      {
        if (s < 0)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Seed should be non-negative.");
          return;
        }
      };
      if (seedLst.Count == 1)
        seedLst = Enumerable.Repeat(seedLst[0], plnLst.Count).ToList();
      else if (seedLst.Count != plnLst.Count)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Seed # does not match Plane #, please check.");
      }

      var brRotLst = new List<bool>();
      if (!DA.GetDataList("BranchRotation", brRotLst))
      { return; }
      if (brRotLst.Count == 1)
        brRotLst = Enumerable.Repeat(brRotLst[0], plnLst.Count).ToList();
      else if (brRotLst.Count != plnLst.Count)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Branch rotation # does not match Plane #, please check.");
      }

      #endregion

      //! 1. determine horizontal scaling factor of the trees
      Dictionary<int, List<Curve>> branchCol = new Dictionary<int, List<Curve>>();
      Dictionary<int, List<Curve>> trunkCol = new Dictionary<int, List<Curve>>();

      if (plnLst.Count == 0)
        return;

      // calculate distance between trees
      // todo: currently, only consider distance between trunks, phases are not considered
      var distLst = new List<double>();
      if (plnLst.Count > 1)
        Utils.GetLstNearestDist(plnLst.Select(x => x.Origin).ToList(), out distLst);
      else
        distLst = Enumerable.Repeat(double.MaxValue, plnLst.Count).ToList();


      List<Mesh> canopyVolLst = new List<Mesh>();
      List<Mesh> trunkVolLst = new List<Mesh>();
      foreach (var (pln, i) in plnLst.Select((pln, i) => (pln, i)))
      {
        // generate tree
        var t = new Tree3D(pln, gsLst[i], tsLst[i], seedLst[i], brRotLst[i]);
        t.SetNearestDist(distLst[i]);
        t.Generate(phaseLst[i], angLstMain[i], angLstTop[i]);
        t.GetBranch(ref branchCol);
        trunkCol.Add(i, t.GetTrunk());

        // create 3D volume for Energy Analysis
        t.GetCanopyVolume(out Mesh canopyVol);
        canopyVolLst.Add(canopyVol);
        t.GetTrunckVolume(phaseLst[i], out Mesh trunkVol);
        trunkVolLst.Add(trunkVol);
      }

      // collection trunk
      DataTree<Curve> trCrv = new DataTree<Curve>();
      foreach (var tr in trunkCol)
      {
        trCrv.AddRange(tr.Value, new GH_Path(tr.Key));
      }

      // collection branches
      DataTree<Curve> brCrv = new DataTree<Curve>();
      var maxBr = 0;
      foreach (var br in branchCol)
      {
        maxBr = Math.Max(maxBr, br.Key);
        brCrv.AddRange(br.Value, new GH_Path(br.Key));
      }

      // in some cases, intermediate branches are not generated, we need to manually generate them
      // so that the tree structure is consistent across all trees with the phase
      for (int i = 0; i <= maxBr; i++)
      {
        var path = new GH_Path(i);
        if (!brCrv.PathExists(path))
        {
          brCrv.AddRange(new List<Curve>(), new GH_Path(i));
        }
      }

      var volLst = new List<Mesh>();
      volLst.AddRange(canopyVolLst);
      volLst.AddRange(trunkVolLst);
      DA.SetDataTree(0, trCrv);
      DA.SetDataTree(1, brCrv);
      DA.SetDataList(2, volLst);
    }
  }
}
