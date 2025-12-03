using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using KdTree;
using Rhino.Geometry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using BeingAliveLanguage.BalCore;

namespace BeingAliveLanguage {
  /// <summary>
  /// Initializes a new instance of the RootSoilMap2d class.
  /// </summary>
  public class BALSoilMap2d : GH_Component {
    public BALSoilMap2d()
        : base("SoilMap2d", "balSoilMap2d", "Build the 2D soil map for root drawing.", "BAL",
               "02::root") {}

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon =>
        SysUtils.cvtByteBitmap(Properties.Resources.balSoilMap2D);
    public override Guid ComponentGuid => new Guid("B17755A9-2101-49D3-8535-EC8F93A8BA01");

    protected override void RegisterInputParams(GH_InputParamManager pManager) {
      pManager.AddPlaneParameter(
          "Plane", "P",
          "Base plane where the soil map exists." +
              "For soil that is not aligned with the world coordinates, please use the soil boundary.",
          GH_ParamAccess.item, Rhino.Geometry.Plane.WorldXY);
      pManager.AddGenericParameter(
          "Soil Geometry", "soilGeo",
          "Soil geometry that representing the soil. " +
              "For sectional soil, this should be triangle grids;" +
              "for planar soil, this can be any tessellation or just a point collection.",
          GH_ParamAccess.list);

      pManager[0].Optional = true;
      pManager[1].DataMapping = GH_DataMapping.Flatten;  // flatten the triangle list by default
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager) {
      pManager.AddGenericParameter("SoilMap2D", "sMap2D", "The soil map class to build root upon.",
                                   GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA) {
      var pln = new Plane();
      DA.GetData(0, ref pln);

      var inputGeo = new List<IGH_Goo>();
      if (!DA.GetDataList(1, inputGeo) || inputGeo.Count == 0) {
        return;
      }

      var conPt = new ConcurrentBag<Point3d>();
      var conPoly = new ConcurrentBag<Polyline>();
      // SoilMap sMap = new SoilMap(pln, mapMode);
      var sMap = new SoilMap2d(pln);

      // detecting the goo type and add it to the corresponding container
      Parallel.ForEach(inputGeo, goo => {
        if (goo.CastTo<Point3d>(out Point3d p))
          conPt.Add(p);

        else if (goo.CastTo<Polyline>(out Polyline pl))
          conPoly.Add(pl);

        else if (goo.CastTo<Curve>(out Curve crv)) {
          if (crv.TryGetPolyline(out Polyline ply))
            conPoly.Add(ply);
          else {
            double[] tmpT = crv.DivideByCount(20, true);
            foreach (var t in tmpT) {
              conPt.Add(crv.PointAt(t));
            }
          }
        }

        else if (goo.CastTo<Rectangle3d>(out Rectangle3d c))
          conPoly.Add(c.ToPolyline());
      });

      sMap.BuildMap(conPt, conPoly);
      sMap.BuildBound();

      DA.SetData(0, sMap);
    }
  }

  /// <summary>
  /// Initializes a new instance of the SoilMap3d class.
  /// </summary>
  public class BALSoilMap3d : GH_Component {
    public BALSoilMap3d()
        : base("SoilMap3d", "balSoilMap3d", "Build the 3D soil map for root drawing.", "BAL",
               "02::root") {}

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon =>
        SysUtils.cvtByteBitmap(Properties.Resources.balSoilMap3D);
    public override Guid ComponentGuid => new Guid("84929d36-71bd-4c96-ae00-6f39b1025455");

    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager) {
      pManager.AddPlaneParameter("Surface Plane", "surP", "A plane representing the soil surface.",
                                 GH_ParamAccess.item, Plane.WorldXY);
      pManager[0].Optional = true;
      pManager.AddGenericParameter("Soil Volume", "soilVol",
                                   "Geometry volume that representing the soil.",
                                   GH_ParamAccess.item);
      pManager.AddIntegerParameter("Particle Number", "parN",
                                   "The number of particles to simulate the soil volume.",
                                   GH_ParamAccess.item, 1000);
      pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager) {
      pManager.AddGenericParameter("SoilMap3d", "sMap3d", "The 3D soil map class to build root in.",
                                   GH_ParamAccess.list);
      pManager.AddPointParameter("Map Points", "mapPt",
                                 "Spatial points that the 3D soil map builds upon.",
                                 GH_ParamAccess.list);
      // pManager.AddMeshParameter("testMesh", "TestM", "Test mesh for the soil volume.",
      // GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA) {
#region input handling
      var mPln = new Plane();
      DA.GetData("Surface Plane", ref mPln);

      int parNum = 0;
      DA.GetData("Particle Number", ref parNum);

      // detecting the goo type and add it to the corresponding container
      object soilVolObj = null;
      if (!DA.GetData("Soil Volume", ref soilVolObj)) {
        return;
      }

      var typ = soilVolObj.GetType();
      Mesh soilVol = null;
      if (soilVolObj is GH_Mesh sMesh) {
        soilVol = sMesh.Value;
      } else if (soilVolObj is GH_Brep sBrep) {
        // var soilVolBrep = soilVolObj;
        var fLst =
            Mesh.CreateFromBrep(sBrep.Value, MeshingParameters.QualityRenderMesh).ToList<Mesh>();
        foreach (var f in fLst) {
          if (soilVol == null)
            soilVol = f;
          else
            soilVol.Append(f);
        }
      } else {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                          "Invalid input type. Expected Mesh or Brep.");
        return;
      }

      if (!soilVol.IsClosed) {
        // DA.SetData("testMesh", soilVol);
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The input mesh is not watertight.");
        return;
      }
#endregion

      BoundingBox bbox = soilVol.GetBoundingBox(true);
      List<Point3d> points = new List<Point3d>();

      // Generate points inside the soil volume
      for (int i = 0; i < parNum * 10; i++) {
        double x = Utils.balRnd.NextDouble() * (bbox.Max.X - bbox.Min.X) + bbox.Min.X;
        double y = Utils.balRnd.NextDouble() * (bbox.Max.Y - bbox.Min.Y) + bbox.Min.Y;
        double z = Utils.balRnd.NextDouble() * (bbox.Max.Z - bbox.Min.Z) + bbox.Min.Z;

        Point3d pt = new Point3d(x, y, z);
        if (soilVol.IsPointInside(pt, 0.001, false))
          points.Add(pt);
      }

      // Eliminate until the `parNum` is reached
      cppUtils.SampleElim(points, soilVol.Volume(), 3, parNum, out List<Point3d> resPt);

      // build the map
      var sMap3d = new SoilMap3d(mPln);
      sMap3d.BuildMap(resPt);

      // Output the soil map
      DA.SetData("SoilMap3d", sMap3d);
      DA.SetDataList("Map Points", resPt);
    }
  }
  /// <summary>
  /// Draw the root in sectional soil grid.
  /// </summary>
  public class BALRootSectional : GH_Component {
    /// <summary>
    /// Initializes a new instance of the MyComponent1 class.
    /// </summary>
    public BALRootSectional()
        : base("Root_Sectional", "balRoot_S", "Draw root in sectional soil map.", "BAL",
               "02::root") {}

    string formMode = "single";  // none, single, multi
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    public override Guid ComponentGuid => new Guid("8772b28f-5853-4460-9aa0-1b711b1b3662");
    protected override System.Drawing.Bitmap Icon =>
        SysUtils.cvtByteBitmap(Properties.Resources.balRootSectional);

    protected override void RegisterInputParams(GH_InputParamManager pManager) {
      pManager.AddGenericParameter("SoilMap", "sMap", "The soil map class to build root upon.",
                                   GH_ParamAccess.item);
      pManager.AddPointParameter("Anchor", "A", "Anchor locations of the root(s).",
                                 GH_ParamAccess.item);
      pManager.AddIntegerParameter("Steps", "S", "Root growing steps.", GH_ParamAccess.item);
      pManager.AddIntegerParameter(
          "BranchN", "n",
          "Root branching number (>= 2, initial branching number from the root anchor.)",
          GH_ParamAccess.item, 2);
      pManager[3].Optional = true;
      pManager.AddIntegerParameter("seed", "s", "Int seed to randomize the generated root pattern.",
                                   GH_ParamAccess.item, -1);
      pManager[4].Optional = true;

      pManager.AddCurveParameter("Env Attractor", "envA",
                                 "Environmental attracting area (water, resource, etc.).",
                                 GH_ParamAccess.list);
      pManager[5].Optional = true;
      pManager.AddCurveParameter("Env Repeller", "envR",
                                 "Environmental repelling area (dryness, poison, etc.).",
                                 GH_ParamAccess.list);
      pManager[6].Optional = true;
      pManager.AddNumberParameter(
          "Env DetectionRange", "envD",
          "The range (to unit length of the grid) that a root can detect surrounding environment.",
          GH_ParamAccess.item, 5);
      pManager.AddBooleanParameter("EnvAffector Toggle", "envToggle",
                                   "Toggle the affects caused by environmental factors.",
                                   GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager) {
      pManager.AddGenericParameter("RootSec-Primary", "root-main",
                                   "The sectional root drawing for primary roots.",
                                   GH_ParamAccess.list);
      pManager.AddGenericParameter("RootSec-Secondary", "root-rest",
                                   "The sectional root drawing for secondary roots.",
                                   GH_ParamAccess.list);
    }

    public override void AppendAdditionalMenuItems(ToolStripDropDown menu) {
      base.AppendAdditionalMenuItems(menu);

      Menu_AppendSeparator(menu);
      Menu_AppendItem(menu, "Topological Branching:", (sender, e) => {}, false).Font =
          GH_FontServer.StandardItalic;
      Menu_AppendItem(menu, " None",
                      (sender, e) => Menu.SelectMode(this, sender, e, ref formMode, "none"), true,
                      CheckMode("none"));
      Menu_AppendItem(menu, " Level 1",
                      (sender, e) => Menu.SelectMode(this, sender, e, ref formMode, "single"), true,
                      CheckMode("single"));
      Menu_AppendItem(menu, " Level 2",
                      (sender, e) => Menu.SelectMode(this, sender, e, ref formMode, "multi"), true,
                      CheckMode("multi"));
    }

    private bool CheckMode(string _modeCheck) => formMode == _modeCheck;

    public override bool Write(GH_IWriter writer) {
      if (formMode != "")
        writer.SetString("formMode", formMode);
      return base.Write(writer);
    }
    public override bool Read(GH_IReader reader) {
      if (reader.ItemExists("formMode"))
        formMode = reader.GetString("formMode");

      Message = reader.GetString("formMode").ToUpper();
      return base.Read(reader);
    }

    protected override void SolveInstance(IGH_DataAccess DA) {
      var sMap = new SoilMap2d();
      var anchor = new Point3d();
      // double radius = 10.0;
      int steps = 10;
      int branchN = 2;
      int seed = -1;

      // if (!DA.GetData("SoilMap", ref sMap) || sMap.mapMode != "sectional")
      if (!DA.GetData("SoilMap", ref sMap)) {
        return;
      }
      if (!DA.GetData("Anchor", ref anchor)) {
        return;
      }
      if (!DA.GetData("Steps", ref steps)) {
        return;
      }
      DA.GetData("BranchN", ref branchN);
      DA.GetData("seed", ref seed);

      // optional param
      List<Curve> envAtt = new List<Curve>();
      List<Curve> envRep = new List<Curve>();
      double envRange = 5;
      bool envToggle = false;
      DA.GetDataList("Env Attractor", envAtt);
      DA.GetDataList("Env Repeller", envRep);
      DA.GetData("Env DetectionRange", ref envRange);
      DA.GetData("EnvAffector Toggle", ref envToggle);

      if (branchN < 2) {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Root should have branch number >= 2.");
        return;
      }

      if (envToggle) {
        if (envAtt.Count != 0) {
          foreach (var crv in envAtt) {
            if (crv == null) {
              continue;
            }

            if (!crv.IsClosed) {
              AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                                "Attractors contain non-closed curve.");
              return;
            }
          }
        }

        if (envRep.Count != 0) {
          foreach (var crv in envRep) {
            if (crv == null) {
              continue;
            }

            if (!crv.IsClosed) {
              AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                                "Repellers contain non-closed curve.");
              return;
            }
          }
        }
      }

      var rootProps = new RootProp(anchor, formMode, steps, branchN);
      var envProps = new EnvProp(envToggle, envRange, envAtt, envRep);

      var root = new RootSectional(sMap, rootProps, envProps, seed);

      root.Grow();

      DA.SetDataList(0, root.rootCrvMain);
      DA.SetDataList(1, root.rootCrvRest);
    }
  }

  /// <summary>
  /// Draw the planar roots.
  /// </summary>
  public class BALRootPlanar : GH_Component {
    /// <summary>
    /// Initializes a new instance of the MyComponent1 class.
    /// </summary>
    public BALRootPlanar()
        : base("Root_Planar", "balRoot_P", "Draw root in planar soil map.", "BAL", "02::root") {}

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override System.Drawing.Bitmap Icon =>
        SysUtils.cvtByteBitmap(Properties.Resources.balRootPlanar);
    public override Guid ComponentGuid => new Guid("8F8C6D2B-22F2-4511-A7C0-AA8CF2FDA42C");

    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager) {
      pManager.AddGenericParameter("SoilMap", "sMap", "The soil map class to build root upon.",
                                   GH_ParamAccess.item);
      pManager.AddPointParameter("Anchor", "A", "Anchor locations of the root(s).",
                                 GH_ParamAccess.item);

      pManager.AddNumberParameter("Scale", "S", "Root scaling.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Phase", "P", "Current root phase.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Division Num", "divN", "The number of initial root branching.",
                                   GH_ParamAccess.item);

      // 5-8
      pManager.AddCurveParameter("Env Attractor", "envA",
                                 "Environmental attracting area (water, resource, etc.).",
                                 GH_ParamAccess.list);
      pManager.AddCurveParameter("Env Repeller", "envR",
                                 "Environmental repelling area (dryness, poison, etc.).",
                                 GH_ParamAccess.list);
      pManager.AddNumberParameter(
          "Env DetectionRange", "envD",
          "The range (to unit length of the grid) that a root can detect surrounding environment.",
          GH_ParamAccess.item, 5);
      pManager.AddBooleanParameter("EnvAffector Toggle", "envToggle",
                                   "Toggle the affects caused by environmental factors.",
                                   GH_ParamAccess.item, false);

      pManager[5].Optional = true;
      pManager[6].Optional = true;
      pManager[7].Optional = true;
      pManager[8].Optional = true;
    }

    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager) {
      pManager.AddLineParameter("RootPlanar", "rootAll",
                                "The planar root drawing, collection of all level branches.",
                                GH_ParamAccess.list);

      pManager.AddLineParameter("RootPlanarLevel-1", "rootLv1", "Level 1 root components.",
                                GH_ParamAccess.list);
      pManager.AddLineParameter("RootPlanarLevel-2", "rootLv2", "Level 2 root components.",
                                GH_ParamAccess.list);
      pManager.AddLineParameter("RootPlanarLevel-3", "rootLv3", "Level 3 root components.",
                                GH_ParamAccess.list);
      pManager.AddLineParameter("RootPlanarLevel-4", "rootLv4", "Level 4 root components.",
                                GH_ParamAccess.list);
      pManager.AddLineParameter("RootPlanarLevel-5", "rootLv5", "Level 5 root components.",
                                GH_ParamAccess.list);

      pManager.AddLineParameter("RootPlanarAbsorb", "rootAbsorb", "Absorbant roots.",
                                GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA) {
      var sMap = new SoilMap2d();
      DA.GetData(0, ref sMap);

      if (!DA.GetData(0, ref sMap)) {
        return;
      }
      // if (sMap.mapMode != "planar")
      //{
      //   AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Soil map type is not 'planar'.");
      //   return;
      // }

      var anchor = new Point3d();
      if (!DA.GetData(1, ref anchor)) {
        return;
      }

      double scale = 0;
      if (!DA.GetData(2, ref scale)) {
        return;
      }

      int phase = 0;
      if (!DA.GetData(3, ref phase)) {
        return;
      }

      int divN = 1;
      if (!DA.GetData(4, ref divN)) {
        return;
      }

      // optional param
      List<Curve> envAtt = new List<Curve>();
      List<Curve> envRep = new List<Curve>();
      double envRange = 5;
      bool envToggle = false;
      DA.GetDataList(5, envAtt);
      DA.GetDataList(6, envRep);
      DA.GetData(7, ref envRange);
      DA.GetData(8, ref envToggle);

      if (envToggle) {
        foreach (var crv in envAtt)
          if (!crv.IsClosed) {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                              "Attractors contain non-closed curve.");
            return;
          }

        foreach (var crv in envRep)
          if (!crv.IsClosed) {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                              "Repellers contain non-closed curve.");
            return;
          }
      }

      var root =
          new RootPlanar(sMap, anchor, scale, phase, divN, envAtt, envRep, envRange, envToggle);
      var (rtRes, rtAbs) = root.GrowRoot();

      var allRt = rtRes.Aggregate(new List<Line>(), (x, y) => x.Concat(y).ToList());

      DA.SetDataList(0, allRt);
      DA.SetDataList(1, rtRes[0]);
      DA.SetDataList(2, rtRes[1]);
      DA.SetDataList(3, rtRes[2]);
      DA.SetDataList(4, rtRes[3]);
      DA.SetDataList(5, rtRes[4]);
      DA.SetDataList(6, rtAbs);
    }
  }

  /// <summary>
  /// The component to transform sectional roots into soil organic matters
  /// </summary>
  public class BALRootOM : GH_Component {
    public BALRootOM()
        : base("Root_OrganicMatter", "balRootOM", "Transforming roots into soil organic matters.",
               "BAL", "02::root") {}

    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override System.Drawing.Bitmap Icon =>
        SysUtils.cvtByteBitmap(Properties.Resources.balRootOrganicMatter);
    public override Guid ComponentGuid => new Guid("442FA58B-FD38-4403-A1EB-2D79987EC9B0");

    protected override void RegisterInputParams(GH_InputParamManager pManager) {
      pManager.AddGenericParameter("SoilMap", "sMap", "The soil map class to build root upon.",
                                   GH_ParamAccess.item);

      pManager.AddLineParameter(
          "Root Geometry", "rG",
          "The sectional root geometries that will be transformed into soil organic matters.",
          GH_ParamAccess.list);
      pManager[1].DataMapping = GH_DataMapping.Flatten;

      pManager.AddBooleanParameter("Disperse", "D",
                                   "Boolean option for disperse the organic matter into soils.",
                                   GH_ParamAccess.item, false);

      pManager.AddGenericParameter("Soil Info", "soilInfo",
                                   "Info about the current soil based on given content ratio.",
                                   GH_ParamAccess.item);
      pManager[3].Optional = true;
      pManager.AddCurveParameter(
          "Soil Triangle", "soilT",
          "Soil triangles, can be any or combined triangles of sand, silt, clay.",
          GH_ParamAccess.list);
      pManager[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager) {
      pManager.AddLineParameter("RootOM", "rOM", "Organic matters transformed from the roots",
                                GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess DA) {
      // ! 1. clean and remove duplicated segments
      var sMap = new SoilMap2d();

      var inLine = new List<Line>();
      var cleanLine = new List<Line>();
      var outLine = new List<Line>();

      bool disperse = false;

      if (!DA.GetData("SoilMap", ref sMap)) {
        return;
      }
      if (!DA.GetDataList("Root Geometry", inLine)) {
        return;
      }
      DA.GetData("Disperse", ref disperse);

      ConcurrentBag<string> ptKey = new ConcurrentBag<string>();
      var ptSet = new HashSet<string>();
      Parallel.ForEach(inLine, ln => {
        var key = Utils.PtString(ln.PointAt(0), 4) + Utils.PtString(ln.PointAt(1), 4);
        if (ptSet.Add(key)) {
          cleanLine.Add(ln);
        }
      });

      var omTree = new GH_Structure<GH_Line>();

      // ! OptionA. divide curves into segs
      if (!disperse) {
        // List<List<Line>> omCol = new List<List<Line>>();
        foreach (var (ln, i) in cleanLine.Select((ln, i) => (ln, i))) {
          Vector3d omDir = Vector3d.CrossProduct(sMap.mPln.ZAxis, ln.Direction);
          omDir.Unitize();

          double segLen = sMap.unitLen * 0.2;
          int segNum = (int)Math.Round(ln.Length / segLen);

          List<Line> segCol = new List<Line>();
          for (int n = 1; n < segNum; n++) {
            Point3d pt = ln.PointAt((double)n / (double)segNum);
            segCol.Add(new Line(pt - segLen * omDir, pt + segLen * omDir));
          }
          // omCol.Add(segCol);

          var path = new GH_Path(i);
          omTree.AppendRange(segCol.Select(x => new GH_Line(x)), path);
        }

        DA.SetDataTree(0, omTree);
      }

      // ! OptionB. find the corresponding triangles and create soil-based OM
      else  // disperse
      {
        SoilProperty sInfo = new SoilProperty();
        List<Curve> soilCrv = new List<Curve>();
        if (!DA.GetData("Soil Info", ref sInfo)) {
          AddRuntimeMessage(
              GH_RuntimeMessageLevel.Error,
              "To disperse the organic matters into the soil, soil saturation info is needed.");
          return;
        }

        // if (!DA.GetDataList("Soil Triangle", soilCrv))
        if (!DA.GetDataList(4, soilCrv)) {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            "To disperse the organic matters into the soil, soil base is needed.");
          return;
        }

        List<Polyline> soilT = new List<Polyline>();
        foreach (Curve c in soilCrv) {
          c.TryGetPolyline(out Polyline ply);
          soilT.Add(ply);
        }

        // 1. find the nearest triangles
        Utils.CreateCentreMap(soilT, out Dictionary<string, ValueTuple<Point3d, Polyline>> cenMap);
        var kdMap =
            new KdTree<double, Point3d>(2, new KdTree.Math.DoubleMath(), AddDuplicateBehavior.Skip);

        var toLocal = Transform.ChangeBasis(Plane.WorldXY, sMap.mPln);
        foreach (var pl in soilT) {
          var cen = (pl[0] + pl[1] + pl[2]) / 3;
          var originalCen = cen;
          cen.Transform(toLocal);
          kdMap.Add(new[] { cen.X, cen.Y }, originalCen);
        }

        // HashSet<string> allTriCenStr = new HashSet<string>(cenMap.Keys);
        HashSet<string> neighbourTriCen = new HashSet<string>();

        Parallel.ForEach(cleanLine, ln => {
          var crv = ln.ToNurbsCurve();
          if (crv == null)
            return;

          crv.DivideByCount(6, true, out Point3d[] pts);
          foreach (var p in pts) {
            p.Transform(toLocal);
            var res = kdMap.GetNearestNeighbours(new[] { p.X, p.Y }, 4);

            foreach (var r in res) neighbourTriCen.Add(Utils.PtString(r.Value));
          }
        });

        // for debugging parallel computation
        // foreach (var ln in cleanLine)
        //{
        //    var crv = ln.ToNurbsCurve();
        //    if (crv != null)
        //    {
        //        crv.DivideByCount(6, true, out Point3d[] pts);
        //        foreach (var p in pts)
        //        {
        //            p.Transform(toLocal);
        //            var res = kdMap.GetNearestNeighbours(new[] { p.X, p.Y }, 2);

        //            if (res[0].Value != null)
        //                neighbourTriCen.Add(Utils.PtString(res[0].Value));
        //        }

        //    }
        //}

        var filteredCen = new HashSet<string>();
        foreach (var c in neighbourTriCen) {
          if (c != null)
            filteredCen.Add(c);
        }

        var triOuter = filteredCen.Select(x => cenMap[x].Item2).ToList();
        var triInner = triOuter.Select(x => Utils.OffsetTri(x.Duplicate(), 1 - sInfo.saturation));

        var omRes = triOuter.Zip(triInner, (triO, triI) => Utils.createOM(triO, triI, 7)).ToList();

        for (int i = 0; i < omRes.Count; i++) {
          var path = new GH_Path(i);
          omTree.AppendRange(omRes[i].Select(x => new GH_Line(x)), path);
        }

        DA.SetDataTree(0, omTree);
      }
    }
  }

  /// <summary>
  /// Draw tree roots
  /// </summary>
  public class BALtreeRoot2d : GH_Component {
    public BALtreeRoot2d()
        : base("TreeRoot2D", "balTreeRoot2D",
               "Generate the BAL tree-root drawing in 2D using the BAL tree and soil information.",
               "BAL", "02::root") {}

    public override GH_Exposure Exposure => GH_Exposure.quarternary;
    protected override System.Drawing.Bitmap Icon =>
        SysUtils.cvtByteBitmap(Properties.Resources.balTreeRoot2D);
    public override Guid ComponentGuid => new Guid("27C279E0-08C9-4110-AE40-81A59C9D9EB8");
    private bool rootDense = false;
    private int scalingFactor = 1;
    private string drawingMode = "centerLine";  // Default to centerLine mode

    protected override void RegisterInputParams(GH_InputParamManager pManager) {
      pManager.AddGenericParameter("TreeInfo", "tInfo", "Information about the tree.",
                                   GH_ParamAccess.item);
      pManager.AddGenericParameter("SoilMap2D", "sMap2D", "The soil map class to build root upon.",
                                   GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager) {
      pManager.AddLineParameter("All Roots", "rootAll",
                                "The planar root drawing, collection of all level roots.",
                                GH_ParamAccess.list);

      pManager.AddCurveParameter("RootMain", "rootM", "Primary roots.", GH_ParamAccess.list);
      pManager.AddCurveParameter("RootNew", "rootN", "Secondary roots.", GH_ParamAccess.list);
      pManager.AddCurveParameter(
          "RootDead", "rootD", "Dead roots in later phases of a tree's life.", GH_ParamAccess.list);
    }

    public override void AppendAdditionalMenuItems(ToolStripDropDown menu) {
      base.AppendAdditionalMenuItems(menu);

      Menu_AppendSeparator(menu);
      Menu_AppendItem(menu, "Drawing Style:", (sender, e) => {}, false).Font =
          GH_FontServer.StandardItalic;
      Menu_AppendItem(
          menu, " Center Lines",
          (sender, e) => Menu.SelectMode(this, sender, e, ref drawingMode, "centerLine"), true,
          CheckDrawingMode("centerLine"));
      Menu_AppendItem(
          menu, " Fancy Bounds",
          (sender, e) => Menu.SelectMode(this, sender, e, ref drawingMode, "fancyBound"), true,
          CheckDrawingMode("fancyBound"));
    }

    private bool CheckDrawingMode(string mode) => drawingMode == mode;

    public override bool Write(GH_IWriter writer) {
      writer.SetString("drawingMode", drawingMode);
      return base.Write(writer);
    }

    public override bool Read(GH_IReader reader) {
      if (reader.ItemExists("drawingMode"))
        drawingMode = reader.GetString("drawingMode");

      return base.Read(reader);
    }

    protected override void SolveInstance(IGH_DataAccess DA) {
      //! Get data
      var tInfo = new TreeProperty();
      var sMap = new SoilMap2d();

      if (!DA.GetData<TreeProperty>("TreeInfo", ref tInfo)) {
        return;
      }

      if (!DA.GetData<SoilMap2d>("SoilMap2D", ref sMap)) {
        return;
      }

      // if (sMap.mapMode != "sectional")
      //{
      //   AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A tree root need a sectional soil map to
      //   grow upon.");
      // }

      if (sMap.mPln != tInfo.pln) {
        AddRuntimeMessage(
            GH_RuntimeMessageLevel.Warning,
            "Tree plane and SoilMap plane does not match. This may cause issues for the root drawing.");
      }

      // ! get anchor + determin root size based on tree size
      var anchorPt = sMap.GetNearestPoint(tInfo.pln.Origin);
      if (anchorPt.DistanceTo(tInfo.pln.Origin) > sMap.unitLen) {
        AddRuntimeMessage(
            GH_RuntimeMessageLevel.Error,
            "Tree anchor point is too far from the soil. Please plant your tree near the soil grid.");
      }

      // if the unit length of the soil grid is small enough, we allow the drawing of detailed root.
      if (sMap.unitLen < tInfo.height * 0.05) {
        rootDense = true;
        scalingFactor = 2;
      } else {
        rootDense = false;
        scalingFactor = 1;

        // send warning of the soil grid is too big
        if (sMap.unitLen > tInfo.height * 0.15) {
          AddRuntimeMessage(
              GH_RuntimeMessageLevel.Warning,
              "Soil grid too big. You will get unbalanced relationship between the tree and its roots.");
        }
      }

      // ! get parameter of the map and start drawing based on the phase
      var uL = sMap.unitLen;             // unit length, side length of the triangle
      var vL = uL * Math.Sqrt(3) * 0.5;  // vertical unit length, height of the triangle
      var mainRoot = new List<Curve>();
      var newRoot = new List<Curve>();
      var hairRoot = new List<Line>();
      var deadRoot = new List<Curve>();

      if (tInfo.phase < 1 || tInfo.phase > 12)
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Tree phase is out of range [0, 12].");

      var vVec = -sMap.mPln.YAxis * vL * scalingFactor;
      var hVec = sMap.mPln.XAxis * uL * scalingFactor;

      int vSideParam = 3;

      /////////////////////////////////////////////
      /// As the root is largely manually defined in different phases,
      /// a majority of the diagram here is hard-coded and not fully rule-based
      ///  - the hairy roots are even more hard-coded
      ////////////////////////////////////////////

      //! Main Root
      // vertical tap root (central layer)
      // --------------------
      //          *
      //          *
      int verticalTapRootParam = 4;
      if (tInfo.phase == 1) {
        verticalTapRootParam = 3;
        newRoot.Add(new Line(anchorPt, vVec * verticalTapRootParam).ToNurbsCurve());
      } else if (tInfo.phase > 1 && tInfo.phase < 11) {
        if (tInfo.phase == 2)  // at phase 2, tap root grows longer
        {
          var preVerticalParam = verticalTapRootParam - 1;
          mainRoot.Add(new Line(anchorPt, vVec * preVerticalParam).ToNurbsCurve());
          newRoot.Add(new Line(anchorPt + vVec * preVerticalParam, vVec).ToNurbsCurve());
        } else {
          mainRoot.Add(new Line(anchorPt, vVec * verticalTapRootParam).ToNurbsCurve());
        }
      } else if (tInfo.phase == 11) {
        // deadRoot.Add(new Line(anchorPt, vVec * 2));
        deadRoot.Add(new Line(anchorPt, vVec * verticalTapRootParam).ToNurbsCurve());
      }

      //! vertical root (1nd layer)
      // ---------------------
      //        *  |  *
      //        *  |  *
      int vTmpParam = 0;
      var lAnchor = anchorPt - hVec * 4;
      var rAnchor = anchorPt + hVec * 4;
      if (tInfo.phase == 5) {
        vTmpParam = 2;
        newRoot.Add(new Line(lAnchor, vVec * vTmpParam).ToNurbsCurve());
        newRoot.Add(new Line(rAnchor, vVec * vTmpParam).ToNurbsCurve());
      } else if (tInfo.phase > 5 && tInfo.phase < 12) {
        vTmpParam = vSideParam;
        if (tInfo.phase == 6) {
          var preVerticalParam = vTmpParam - 1;
          mainRoot.Add(new Line(lAnchor, vVec * preVerticalParam).ToNurbsCurve());
          mainRoot.Add(new Line(rAnchor, vVec * preVerticalParam).ToNurbsCurve());
          newRoot.Add(new Line(lAnchor + vVec * preVerticalParam, vVec).ToNurbsCurve());
          newRoot.Add(new Line(rAnchor + vVec * preVerticalParam, vVec).ToNurbsCurve());
        } else {
          mainRoot.Add(new Line(lAnchor, vVec * vTmpParam).ToNurbsCurve());
          mainRoot.Add(new Line(rAnchor, vVec * vTmpParam).ToNurbsCurve());
        }
      } else if (tInfo.phase == 12) {
        // phase 10, dead root shown
        deadRoot.Add(new Line(lAnchor, vVec * vSideParam).ToNurbsCurve());
        deadRoot.Add(new Line(rAnchor, vVec * vSideParam).ToNurbsCurve());
      }

      // ! vertical root (2rd layer)
      // ---------------------
      //     *  |  |  |  *
      //     *  |  |  |  *
      lAnchor = anchorPt - hVec * 7;
      rAnchor = anchorPt + hVec * 7;
      vTmpParam = 2;
      if (tInfo.phase == 6) {
        newRoot.Add(new Line(lAnchor, vVec * vTmpParam).ToNurbsCurve());
        newRoot.Add(new Line(rAnchor, vVec * vTmpParam).ToNurbsCurve());
      } else if (tInfo.phase > 6 && tInfo.phase <= 11) {
        // vTmpParam = vSideParam;
        mainRoot.Add(new Line(lAnchor, vVec * vTmpParam).ToNurbsCurve());
        mainRoot.Add(new Line(rAnchor, vVec * vTmpParam).ToNurbsCurve());

        // if (tInfo.phase == 7)
        //{
        //     var preVerticalParam = vTmpParam - 1;
        //     mainRoot.Add(new Line(lAnchor, vVec * preVerticalParam));
        //     mainRoot.Add(new Line(rAnchor, vVec * preVerticalParam));
        //     newRoot.Add(new Line(lAnchor + vVec * preVerticalParam, vVec));
        //     newRoot.Add(new Line(rAnchor + vVec * preVerticalParam, vVec));
        // }
        // else
        //{
        //     mainRoot.Add(new Line(lAnchor, vVec * vTmpParam));
        //     mainRoot.Add(new Line(rAnchor, vVec * vTmpParam));
        // }
      } else if (tInfo.phase == 12) {
        // phase 12, dead root shown
        deadRoot.Add(new Line(lAnchor, vVec * vTmpParam).ToNurbsCurve());
        deadRoot.Add(new Line(rAnchor, vVec * vTmpParam).ToNurbsCurve());
      }

      //! vertical root (3rd layer)
      // ---------------------
      //  *  |  |  |  |  |  *
      //  *  |  |  |  |  |  *

      // 2023.06.28: remove 3rd layer
      // lAnchor = anchorPt - hVec * 10;
      // rAnchor = anchorPt + hVec * 10;
      // vTmpParam = 0;
      // if (tInfo.phase >= 7 && tInfo.phase <= 11)
      //{
      //    vTmpParam = 2;
      //    if (tInfo.phase == 7)
      //    {
      //        newRoot.Add(new Line(lAnchor, vVec * vTmpParam));
      //        newRoot.Add(new Line(rAnchor, vVec * vTmpParam));
      //    }
      //    else
      //    {
      //        mainRoot.Add(new Line(lAnchor, vVec * vTmpParam));
      //        mainRoot.Add(new Line(rAnchor, vVec * vTmpParam));
      //    }
      //}
      // else if (tInfo.phase == 12)
      //{
      //    // phase 12, dead root shown
      //    deadRoot.Add(new Line(lAnchor, vVec * vSideParam));
      //    deadRoot.Add(new Line(rAnchor, vVec * vSideParam));
      //}

      //! vertical root (secondary 1st layer)
      // ---------------------
      //  |  |  |  |  |  |  |
      //     -------------
      //  |  |  | *|* |  |  |
      lAnchor = anchorPt - hVec * 2 + vVec * 2;
      rAnchor = anchorPt + hVec * 2 + vVec * 2;
      vTmpParam = 1;

      if (tInfo.phase == 8) {
        newRoot.Add(new Line(lAnchor, vVec * vTmpParam).ToNurbsCurve());
        newRoot.Add(new Line(rAnchor, vVec * vTmpParam).ToNurbsCurve());
      }
      if (tInfo.phase >= 9 && tInfo.phase < 11) {
        mainRoot.Add(new Line(lAnchor, vVec * vTmpParam).ToNurbsCurve());
        mainRoot.Add(new Line(rAnchor, vVec * vTmpParam).ToNurbsCurve());
      } else if (tInfo.phase == 11) {
        deadRoot.Add(new Line(rAnchor, vVec * vTmpParam).ToNurbsCurve());
        deadRoot.Add(new Line(lAnchor, vVec * vTmpParam).ToNurbsCurve());
      }

      //! horizontal tap root (central)
      // ********************
      //          |
      //          |
      int totalHorizontalTapRootParam = 0;
      int prevHorTapRootParam = 0;
      int curHorTapRootParam = 0;
      if (tInfo.phase >= 1 && tInfo.phase < 9) {
        if (tInfo.phase == 1) {
          prevHorTapRootParam = 0;
          curHorTapRootParam = 1;
        } else {
          prevHorTapRootParam = (tInfo.phase - 2) * 2 + 1;
          curHorTapRootParam = 2;
        }

        newRoot.Add(new Line(anchorPt + hVec * prevHorTapRootParam, hVec * curHorTapRootParam)
                        .ToNurbsCurve());
        newRoot.Add(new Line(anchorPt - hVec * prevHorTapRootParam, -hVec * curHorTapRootParam)
                        .ToNurbsCurve());

        mainRoot.Add(new Line(anchorPt, hVec * prevHorTapRootParam).ToNurbsCurve());
        mainRoot.Add(new Line(anchorPt, -hVec * prevHorTapRootParam).ToNurbsCurve());
      } else {
        totalHorizontalTapRootParam = 15;

        // if (tInfo.phase > 10)
        //{
        //     totalHorizontalTapRootParam = 10;

        //    if (tInfo.phase == 11)
        //    {
        //        var lPt = anchorPt + hVec * totalHorizontalTapRootParam;
        //        deadRoot.Add(new Line(lPt, hVec * 5));
        //        var rPt = anchorPt - hVec * totalHorizontalTapRootParam;
        //        deadRoot.Add(new Line(rPt, -hVec * 5));

        //    }
        //}
        mainRoot.Add(new Line(anchorPt, hVec * totalHorizontalTapRootParam).ToNurbsCurve());
        mainRoot.Add(new Line(anchorPt, -hVec * totalHorizontalTapRootParam).ToNurbsCurve());
      }

      //! horizontal tap root (2nd layer)
      // -------------------
      //         |
      //       *****
      //         |
      int lParam = 5;
      int rParam = 5;
      int preParam = 0;
      int curParam = 0;

      var startPtH2 = anchorPt - sMap.mPln.YAxis * vL * 2;
      if (tInfo.phase >= 4 && tInfo.phase < 7) {
        if (tInfo.phase == 4) {
          curParam = 1;
          preParam = 0;
        } else {
          preParam = (tInfo.phase - 5) * 2 + 1;
          curParam = 2;
        }

        newRoot.Add(new Line(startPtH2 - hVec * preParam, -hVec * curParam).ToNurbsCurve());
        newRoot.Add(new Line(startPtH2 + hVec * preParam, hVec * curParam).ToNurbsCurve());

        mainRoot.Add(new Line(startPtH2, -hVec * preParam).ToNurbsCurve());
        mainRoot.Add(new Line(startPtH2, hVec * preParam).ToNurbsCurve());

      } else if (tInfo.phase >= 7 && tInfo.phase < 11) {
        mainRoot.Add(new Line(startPtH2, hVec * rParam).ToNurbsCurve());
        mainRoot.Add(new Line(startPtH2, -hVec * lParam).ToNurbsCurve());
      } else if (tInfo.phase == 11) {
        deadRoot.Add(new Line(startPtH2, hVec * rParam).ToNurbsCurve());
        deadRoot.Add(new Line(startPtH2, -hVec * lParam).ToNurbsCurve());
      }

      //! horizontal tap root (3rd layer)
      // -------------------
      //         |
      //       -----
      //         |
      //       *****

      int prevParam = 0;
      var startPtH3 = anchorPt - sMap.mPln.YAxis * vL * 4;
      if (tInfo.phase == 6) {
        curParam = 3;
        newRoot.Add(new Line(startPtH3, hVec * curParam).ToNurbsCurve());
        newRoot.Add(new Line(startPtH3, -hVec * curParam).ToNurbsCurve());
      } else if (tInfo.phase == 7) {
        prevParam = 3;
        curParam = 1;
        newRoot.Add(new Line(startPtH3 + hVec * prevParam, hVec * curParam).ToNurbsCurve());
        newRoot.Add(new Line(startPtH3 - hVec * prevParam, -hVec * curParam).ToNurbsCurve());

        mainRoot.Add(new Line(startPtH3, hVec * prevParam).ToNurbsCurve());
        mainRoot.Add(new Line(startPtH3, -hVec * prevParam).ToNurbsCurve());
      } else if (tInfo.phase >= 8 && tInfo.phase < 10) {
        mainRoot.Add(new Line(startPtH3, hVec * 4).ToNurbsCurve());
        mainRoot.Add(new Line(startPtH3, -hVec * 4).ToNurbsCurve());
      } else if (tInfo.phase == 10) {
        deadRoot.Add(new Line(startPtH3, hVec * 4).ToNurbsCurve());
        deadRoot.Add(new Line(startPtH3, -hVec * 4).ToNurbsCurve());
      }

      // Remove any null items from the result lists
      mainRoot = mainRoot.Where(r => r != null).ToList();
      newRoot = newRoot.Where(r => r != null).ToList();
      deadRoot = deadRoot.Where(r => r != null).ToList();

      // If using fancy boundary mode, convert the centerlines to boundary curves
      if (drawingMode == "fancyBound") {
        var allRootLines = new List<Curve>();
        allRootLines.AddRange(mainRoot);
        allRootLines.AddRange(newRoot);
        allRootLines.AddRange(deadRoot);

        var fancyMainRoot = CreateFancyBound(mainRoot, anchorPt, sMap.unitLen * 0.3, tInfo.phase);
        // var fancyNewRoot = CreateFancyBound(newRoot, anchorPt, sMap.unitLen * 0.2, tInfo.phase);
        // var fancyDeadRoot = CreateFancyBound(deadRoot, anchorPt, sMap.unitLen * 0.15,
        // tInfo.phase);

        // Replace the original lists with the fancy versions
        mainRoot = fancyMainRoot;
        // newRoot = fancyNewRoot;
        // deadRoot = fancyDeadRoot;
      }

      // ! export all
      DA.SetDataList("RootMain", mainRoot);
      DA.SetDataList("RootNew", newRoot);
      DA.SetDataList("RootDead", deadRoot);
    }
    /// <summary>
    /// Creates fancy boundary lines around centerlines
    /// </summary>
    private List<Curve> CreateFancyBound(List<Curve> centerLines, Point3d anchorPoint,
                                         double baseThickness, int phase) {
      if (centerLines.Count == 0)
        return new List<Curve>();

      List<Curve> boundaryCrv = new List<Curve>();

      foreach (var centerLine in centerLines) {
        var lnCollection = new List<Line>();
        // Calculate thickness based on distance from anchor and position along the root
        Point3d startPt = centerLine.PointAtStart;
        Point3d endPt = centerLine.PointAtEnd;

        // Get distance from anchor to calculate tapering
        double distFromAnchor = startPt.DistanceTo(anchorPoint);
        double lineLength = centerLine.GetLength();

        // Calculate thickness factor (thicker near anchor, thinner at extremities)
        double thicknessFactor = 1.0 - Math.Min(0.2, distFromAnchor / (20 * baseThickness));
        double thickness = baseThickness * thicknessFactor;

        // Get the direction vector of the line
        Vector3d dir = centerLine.PointAtStart - centerLine.PointAtEnd;
        dir.Unitize();

        // Get perpendicular vector
        Vector3d perpDir = Vector3d.CrossProduct(dir, Vector3d.ZAxis);
        perpDir.Unitize();

        // Create boundary points to form a closed polygon
        Point3d leftStart = startPt + perpDir * thickness;
        Point3d leftEnd = endPt + perpDir * (thickness * 0.1);   // Taper at the end
        Point3d rightEnd = endPt - perpDir * (thickness * 0.1);  // Taper at the end
        Point3d rightStart = startPt - perpDir * thickness;

        // Add boundary lines as a closed polygon
        lnCollection.Add(new Line(leftStart, leftEnd));
        lnCollection.Add(new Line(leftEnd, rightEnd));
        lnCollection.Add(new Line(rightEnd, rightStart));
        lnCollection.Add(new Line(rightStart, leftStart));

        var joinedCrv = Curve.JoinCurves(lnCollection.Select(x => x.ToNurbsCurve()));
        if (joinedCrv.Length != 0)
          boundaryCrv.Add(joinedCrv[0]);
      }

      return boundaryCrv;
    }
  }

  /// <summary>
  /// Draw Tree Root in 3D
  /// </summary>
  public class BALtreeRoot3d : GH_Component {
    public BALtreeRoot3d()
        : base("TreeRoot3D", "balTreeRoot3D",
               "Generate the BAL tree-root drawing in 3D using the BAL tree and soil information.",
               "BAL", "02::Root") {}

    public override GH_Exposure Exposure => GH_Exposure.quarternary;
    protected override System.Drawing.Bitmap Icon =>
        SysUtils.cvtByteBitmap(Properties.Resources.balTreeRoot3D);
    public override Guid ComponentGuid => new Guid("3f18edbd-320a-49e8-b16f-6c19b5654301");

    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager) {
      pManager.AddGenericParameter("TreeInfo", "tInfo", "Information about the tree.",
                                   GH_ParamAccess.item);
      pManager.AddGenericParameter("SoilMap3d", "sMap3d", "The soil map class to build root upon.",
                                   GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager) {
      // pManager.AddLineParameter("All Roots", "rootAll", "The planar root drawing, collection of
      // all level roots.", GH_ParamAccess.list);

      pManager.AddCurveParameter("Root3dTap", "root3dT", "Tap roots in 3D.", GH_ParamAccess.list);
      pManager.AddCurveParameter("Root3dMaster", "root3dM", "Master horizontal roots in 3D.",
                                 GH_ParamAccess.list);
      pManager.AddCurveParameter("Root3dExplorer", "root3dE", "Explorer horizontal roots in 3D.",
                                 GH_ParamAccess.list);
      pManager.AddCurveParameter("Root3dDead", "root3dD",
                                 "Dead roots in various phases of a tree's life in 3D.",
                                 GH_ParamAccess.list);

      // pManager.AddPointParameter("DebugPt", "debugPt", "Debugging points.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA) {
#region input handling
      //! Get data
      var tInfo = new TreeProperty();

      if (!DA.GetData("TreeInfo", ref tInfo)) {
        return;
      }

      var sMap3d = new SoilMap3d(tInfo.pln);
      if (!DA.GetData("SoilMap3d", ref sMap3d)) {
        return;
      }

      // ! get anchor additional info
      // var anchorPt = sMap3d.GetNearestPoint(tInfo.pln.Origin);
      var anchorPt = tInfo.pln.Origin;
      var curPhase = tInfo.phase;
      var curHeight = tInfo.height;
      var curRadius = tInfo.radius;
      var curUnitLen = tInfo.unitLen;
#endregion

      // Draw Roots based on the current phase
      var rootTree3D = new RootTree3D(sMap3d, anchorPt, curUnitLen, curPhase, 6);
      string msg = rootTree3D.GrowRoot();
      if (msg != "Success") {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, msg);
      }

      // Output data
      // DA.SetData("DebugPt", rootTree3D.debugPt);
      DA.SetDataList("Root3dTap", rootTree3D.GetRootTap());
      DA.SetDataList("Root3dMaster", rootTree3D.GetRootMaster());
      DA.SetDataList("Root3dExplorer", rootTree3D.GetRootExplorer());
      DA.SetDataList("Root3dDead", rootTree3D.GetRootDead());
    }
  }
}
