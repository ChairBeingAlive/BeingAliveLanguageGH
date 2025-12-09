using GH_IO.Serialization;
using Grasshopper;
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
public
class BALSoilMap2d : GH_Component {
public
  BALSoilMap2d()
      : base("SoilMap2d",
             "balSoilMap2d",
             "Build the 2D soil map for root drawing.",
             "BAL",
             "02::root") {}

public
  override GH_Exposure Exposure => GH_Exposure.primary;
protected
  override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balSoilMap2D);
public
  override Guid ComponentGuid => new Guid("B17755A9-2101-49D3-8535-EC8F93A8BA01");

protected
  override void RegisterInputParams(GH_InputParamManager pManager) {
    pManager.AddPlaneParameter("Plane",
                               "P",
                               "Base plane where the soil map exists." +
                                   "For soil that is not aligned with the world coordinates, please use the soil boundary.",
                               GH_ParamAccess.item,
                               Rhino.Geometry.Plane.WorldXY);
    pManager.AddGenericParameter(
        "Soil Geometry",
        "soilGeo",
        "Soil geometry that representing the soil. " +
            "For sectional soil, this should be triangle grids;" +
            "for planar soil, this can be any tessellation or just a point collection.",
        GH_ParamAccess.list);

    pManager[0].Optional = true;
    pManager[1].DataMapping = GH_DataMapping.Flatten;  // flatten the triangle list by default
  }

protected
  override void RegisterOutputParams(GH_OutputParamManager pManager) {
    pManager.AddGenericParameter(
        "SoilMap2D", "sMap2D", "The soil map class to build root upon.", GH_ParamAccess.item);
  }

protected
  override void SolveInstance(IGH_DataAccess DA) {
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
    Parallel.ForEach(
        inputGeo, goo => {
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
public
  BALSoilMap3d()
      : base("SoilMap3d",
             "balSoilMap3d",
             "Build the 3D soil map for root drawing.",
             "BAL",
             "02::root") {}

public
  override GH_Exposure Exposure => GH_Exposure.primary;
protected
  override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balSoilMap3D);
public
  override Guid ComponentGuid => new Guid("84929d36-71bd-4c96-ae00-6f39b1025455");

protected
  override void RegisterInputParams(GH_Component.GH_InputParamManager pManager) {
    pManager.AddPlaneParameter("Surface Plane",
                               "surP",
                               "A plane representing the soil surface.",
                               GH_ParamAccess.item,
                               Plane.WorldXY);
    pManager[0].Optional = true;
    pManager.AddGenericParameter("Soil Volume",
                                 "soilVol",
                                 "Geometry volume that representing the soil.",
                                 GH_ParamAccess.item);
    pManager.AddIntegerParameter("Particle Number",
                                 "parN",
                                 "The number of particles to simulate the soil volume.",
                                 GH_ParamAccess.item,
                                 1000);
    pManager[2].Optional = true;
  }

protected
  override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager) {
    pManager.AddGenericParameter(
        "SoilMap3d", "sMap3d", "The 3D soil map class to build root in.", GH_ParamAccess.list);
    pManager.AddPointParameter("Map Points",
                               "mapPt",
                               "Spatial points that the 3D soil map builds upon.",
                               GH_ParamAccess.list);
    // pManager.AddMeshParameter("testMesh", "TestM", "Test mesh for the soil volume.",
    // GH_ParamAccess.item);
  }

protected
  override void SolveInstance(IGH_DataAccess DA) {
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
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid input type. Expected Mesh or Brep.");
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
public
  BALRootSectional()
      : base("Root_Sectional", "balRoot_S", "Draw root in sectional soil map.", "BAL", "02::root") {
  }

  string formMode = "single";  // none, single, multi
public
  override GH_Exposure Exposure => GH_Exposure.secondary;
public
  override Guid ComponentGuid => new Guid("8772b28f-5853-4460-9aa0-1b711b1b3662");
protected
  override System.Drawing.Bitmap
      Icon => SysUtils.cvtByteBitmap(Properties.Resources.balRootSectional);

protected
  override void RegisterInputParams(GH_InputParamManager pManager) {
    pManager.AddGenericParameter(
        "SoilMap", "sMap", "The soil map class to build root upon.", GH_ParamAccess.item);
    pManager.AddPointParameter(
        "Anchor", "A", "Anchor locations of the root(s).", GH_ParamAccess.item);
    pManager.AddIntegerParameter("Steps", "S", "Root growing steps.", GH_ParamAccess.item);
    pManager.AddIntegerParameter(
        "BranchN",
        "n",
        "Root branching number (>= 2, initial branching number from the root anchor.)",
        GH_ParamAccess.item,
        2);
    pManager[3].Optional = true;
    pManager.AddIntegerParameter(
        "seed", "s", "Int seed to randomize the generated root pattern.", GH_ParamAccess.item, -1);
    pManager[4].Optional = true;

    pManager.AddCurveParameter("Env Attractor",
                               "envA",
                               "Environmental attracting area (water, resource, etc.).",
                               GH_ParamAccess.list);
    pManager[5].Optional = true;
    pManager.AddCurveParameter("Env Repeller",
                               "envR",
                               "Environmental repelling area (dryness, poison, etc.).",
                               GH_ParamAccess.list);
    pManager[6].Optional = true;
    pManager.AddNumberParameter(
        "Env DetectionRange",
        "envD",
        "The range (to unit length of the grid) that a root can detect surrounding environment.",
        GH_ParamAccess.item,
        5);
    pManager.AddBooleanParameter("EnvAffector Toggle",
                                 "envToggle",
                                 "Toggle the affects caused by environmental factors.",
                                 GH_ParamAccess.item,
                                 false);
  }

protected
  override void RegisterOutputParams(GH_OutputParamManager pManager) {
    pManager.AddGenericParameter("RootSec-Primary",
                                 "root-main",
                                 "The sectional root drawing for primary roots.",
                                 GH_ParamAccess.list);
    pManager.AddGenericParameter("RootSec-Secondary",
                                 "root-rest",
                                 "The sectional root drawing for secondary roots.",
                                 GH_ParamAccess.list);
  }

public
  override void AppendAdditionalMenuItems(ToolStripDropDown menu) {
    base.AppendAdditionalMenuItems(menu);

    Menu_AppendSeparator(menu);
    Menu_AppendItem(menu, "Topological Branching:", (sender, e) => {}, false).Font =
        GH_FontServer.StandardItalic;
    Menu_AppendItem(menu,
                    " None",
                    (sender, e) => Menu.SelectMode(this, sender, e, ref formMode, "none"),
                    true,
                    CheckMode("none"));
    Menu_AppendItem(menu,
                    " Level 1",
                    (sender, e) => Menu.SelectMode(this, sender, e, ref formMode, "single"),
                    true,
                    CheckMode("single"));
    Menu_AppendItem(menu,
                    " Level 2",
                    (sender, e) => Menu.SelectMode(this, sender, e, ref formMode, "multi"),
                    true,
                    CheckMode("multi"));
  }

private
  bool CheckMode(string _modeCheck) => formMode == _modeCheck;

public
  override bool Write(GH_IWriter writer) {
    if (formMode != "")
      writer.SetString("formMode", formMode);
    return base.Write(writer);
  }
public
  override bool Read(GH_IReader reader) {
    if (reader.ItemExists("formMode"))
      formMode = reader.GetString("formMode");

    Message = reader.GetString("formMode").ToUpper();
    return base.Read(reader);
  }

protected
  override void SolveInstance(IGH_DataAccess DA) {
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
public
  BALRootPlanar()
      : base("Root_Planar", "balRoot_P", "Draw root in planar soil map.", "BAL", "02::root") {}

public
  override GH_Exposure Exposure => GH_Exposure.secondary;

protected
  override System.Drawing.Bitmap Icon =>
                                        SysUtils.cvtByteBitmap(Properties.Resources.balRootPlanar);
public
  override Guid ComponentGuid => new Guid("8F8C6D2B-22F2-4511-A7C0-AA8CF2FDA42C");

protected
  override void RegisterInputParams(GH_Component.GH_InputParamManager pManager) {
    pManager.AddGenericParameter(
        "SoilMap", "sMap", "The soil map class to build root upon.", GH_ParamAccess.item);
    pManager.AddPointParameter(
        "Anchor", "A", "Anchor locations of the root(s).", GH_ParamAccess.item);

    pManager.AddNumberParameter("Scale", "S", "Root scaling.", GH_ParamAccess.item);
    pManager.AddIntegerParameter("Phase", "P", "Current root phase.", GH_ParamAccess.item);
    pManager.AddIntegerParameter(
        "Division Num", "divN", "The number of initial root branching.", GH_ParamAccess.item);

    // 5-8
    pManager.AddCurveParameter("Env Attractor",
                               "envA",
                               "Environmental attracting area (water, resource, etc.).",
                               GH_ParamAccess.list);
    pManager.AddCurveParameter("Env Repeller",
                               "envR",
                               "Environmental repelling area (dryness, poison, etc.).",
                               GH_ParamAccess.list);
    pManager.AddNumberParameter(
        "Env DetectionRange",
        "envD",
        "The range (to unit length of the grid) that a root can detect surrounding environment.",
        GH_ParamAccess.item,
        5);
    pManager.AddBooleanParameter("EnvAffector Toggle",
                                 "envToggle",
                                 "Toggle the affects caused by environmental factors.",
                                 GH_ParamAccess.item,
                                 false);

    pManager[5].Optional = true;
    pManager[6].Optional = true;
    pManager[7].Optional = true;
    pManager[8].Optional = true;
  }

protected
  override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager) {
    pManager.AddLineParameter("RootPlanar",
                              "rootAll",
                              "The planar root drawing, collection of all level branches.",
                              GH_ParamAccess.list);

    pManager.AddLineParameter(
        "RootPlanarLevel-1", "rootLv1", "Level 1 root components.", GH_ParamAccess.list);
    pManager.AddLineParameter(
        "RootPlanarLevel-2", "rootLv2", "Level 2 root components.", GH_ParamAccess.list);
    pManager.AddLineParameter(
        "RootPlanarLevel-3", "rootLv3", "Level 3 root components.", GH_ParamAccess.list);
    pManager.AddLineParameter(
        "RootPlanarLevel-4", "rootLv4", "Level 4 root components.", GH_ParamAccess.list);
    pManager.AddLineParameter(
        "RootPlanarLevel-5", "rootLv5", "Level 5 root components.", GH_ParamAccess.list);

    pManager.AddLineParameter(
        "RootPlanarAbsorb", "rootAbsorb", "Absorbant roots.", GH_ParamAccess.list);
  }

protected
  override void SolveInstance(IGH_DataAccess DA) {
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
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Attractors contain non-closed curve.");
          return;
        }

      foreach (var crv in envRep)
        if (!crv.IsClosed) {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Repellers contain non-closed curve.");
          return;
        }
    }

    var root =
        new RootPlanar(sMap, anchor, scale, phase, divN, envAtt, envRep, envRange, envToggle);
    var(rtRes, rtAbs) = root.GrowRoot();

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
public
  BALRootOM()
      : base("Root_OrganicMatter",
             "balRootOM",
             "Transforming roots into soil organic matters.",
             "BAL",
             "02::root") {}

public
  override GH_Exposure Exposure => GH_Exposure.secondary;
protected
  override System.Drawing.Bitmap
      Icon => SysUtils.cvtByteBitmap(Properties.Resources.balRootOrganicMatter);
public
  override Guid ComponentGuid => new Guid("442FA58B-FD38-4403-A1EB-2D79987EC9B0");

protected
  override void RegisterInputParams(GH_InputParamManager pManager) {
    pManager.AddGenericParameter(
        "SoilMap", "sMap", "The soil map class to build root upon.", GH_ParamAccess.item);

    pManager.AddLineParameter(
        "Root Geometry",
        "rG",
        "The sectional root geometries that will be transformed into soil organic matters.",
        GH_ParamAccess.list);
    pManager[1].DataMapping = GH_DataMapping.Flatten;

    pManager.AddBooleanParameter("Disperse",
                                 "D",
                                 "Boolean option for disperse the organic matter into soils.",
                                 GH_ParamAccess.item,
                                 false);

    pManager.AddGenericParameter("Soil Info",
                                 "soilInfo",
                                 "Info about the current soil based on given content ratio.",
                                 GH_ParamAccess.item);
    pManager[3].Optional = true;
    pManager.AddCurveParameter(
        "Soil Triangle",
        "soilT",
        "Soil triangles, can be any or combined triangles of sand, silt, clay.",
        GH_ParamAccess.list);
    pManager[4].Optional = true;
  }

protected
  override void RegisterOutputParams(GH_OutputParamManager pManager) {
    pManager.AddLineParameter(
        "RootOM", "rOM", "Organic matters transformed from the roots", GH_ParamAccess.tree);
  }

protected
  override void SolveInstance(IGH_DataAccess DA) {
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
    Parallel.ForEach(
        inLine, ln => {
          var key = Utils.PtString(ln.PointAt(0), 4) + Utils.PtString(ln.PointAt(1), 4);
          if (ptSet.Add(key)) {
            cleanLine.Add(ln);
          }
        });

    var omTree = new GH_Structure<GH_Line>();

    // ! OptionA. divide curves into segs
    if (!disperse) {
      // List<List<Line>> omCol = new List<List<Line>>();
      foreach (var(ln, i) in cleanLine.Select((ln, i) => (ln, i))) {
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
        kdMap.Add(new[]{cen.X, cen.Y}, originalCen);
      }

      // HashSet<string> allTriCenStr = new HashSet<string>(cenMap.Keys);
      HashSet<string> neighbourTriCen = new HashSet<string>();

      Parallel.ForEach(
          cleanLine, ln => {
            var crv = ln.ToNurbsCurve();
            if (crv == null)
              return;

            crv.DivideByCount(6, true, out Point3d[] pts);
            foreach (var p in pts) {
              p.Transform(toLocal);
              var res = kdMap.GetNearestNeighbours(new[]{p.X, p.Y}, 4);

              foreach (var r in res)
                neighbourTriCen.Add(Utils.PtString(r.Value));
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
/// Draw Tree Root in 3D
/// </summary>
public class BALtreeRoot3d : GH_Component {
public
  BALtreeRoot3d()
      : base("TreeRoot3D",
             "balTreeRoot3D",
             "Generate the BAL tree-root drawing in 3D using the BAL tree and soil information.",
             "BAL",
             "02::Root") {}

public
  override GH_Exposure Exposure => GH_Exposure.quarternary;
protected
  override System.Drawing.Bitmap Icon =>
                                        SysUtils.cvtByteBitmap(Properties.Resources.balTreeRoot3D);
public
  override Guid ComponentGuid => new Guid("3f18edbd-320a-49e8-b16f-6c19b5654301");

protected
  override void RegisterInputParams(GH_Component.GH_InputParamManager pManager) {
    pManager.AddGenericParameter(
        "TreeInfo", "tInfo", "Information about the tree.", GH_ParamAccess.item);
    pManager.AddGenericParameter("SoilMap3d",
                                 "sMap3d",
                                 "Optional: The soil map class to build root upon. If not " +
                                 "provided, generates simplified symmetric roots.",
                                 GH_ParamAccess.item);
    pManager[1].Optional = true;
    pManager.AddBooleanParameter(
        "ToggleExplorer",
        "tExp",
        "Toggle explorer root generation. Set to False for faster computation with multiple trees.",
        GH_ParamAccess.item,
        false);
    pManager.AddBooleanParameter(
        "TrueScale",
        "tScale",
        "Scale roots to match tree canopy size (2.5x canopy radius). " +
        "When False, root size depends on soil point density.",
        GH_ParamAccess.item,
        false);
  }

protected
  override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager) {
      pManager.AddCurveParameter("Root3dTap",
                               "root3dT",
                               "Tap roots in 3D, organized by start phase in DataTree branches.",
                               GH_ParamAccess.tree);
    pManager.AddCurveParameter(
        "Root3dMaster",
        "root3dM",
        "Master horizontal roots in 3D, organized by start phase in DataTree branches.",
        GH_ParamAccess.tree);
    pManager.AddCurveParameter(
        "Root3dExplorer",
        "root3dE",
        "Explorer horizontal roots in 3D, organized by start phase in DataTree branches.",
        GH_ParamAccess.tree);
    pManager.AddCurveParameter("Root3dDead",
                               "root3dD",
                                 "Dead roots in various phases of a tree's life in 3D, organized by original start phase.",
                                 GH_ParamAccess.tree);
  }

protected
  override void SolveInstance(IGH_DataAccess DA) {
#region input handling
    //! Get data
    var tInfo = new TreeProperty();

    if (!DA.GetData("TreeInfo", ref tInfo)) {
      return;
    }

    // SoilMap3d is optional - if not provided, use simplified growth
    SoilMap3d sMap3d = null;
    DA.GetData("SoilMap3d", ref sMap3d);

    bool toggleExplorer = false;
    DA.GetData("ToggleExplorer", ref toggleExplorer);

    bool trueScale = false;
    DA.GetData("TrueScale", ref trueScale);

    // ! get anchor additional info
    var anchorPt = tInfo.pln.Origin;
    var curPhase = tInfo.phase;
    var curHeight = tInfo.height;
    var curRadius = tInfo.radius;
    var curUnitLen = tInfo.unitLen;
    
    // Calculate target root radius: 2.5x tree canopy radius (biological rule)
    var targetRootRadius = curRadius * 2.5;
#endregion

    // Draw Roots based on the current phase
    // Pass the plane from tInfo for simplified mode when sMap3d is null
    var rootTree3D =
        new RootTree3D(sMap3d, tInfo.pln, anchorPt, curUnitLen, curPhase, 6, toggleExplorer, targetRootRadius, trueScale);
    string msg = rootTree3D.GrowRoot();
    if (msg != "Success") {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, msg);
    }

    // Output data organized by phase using DataTrees
    // Tap roots by phase
    var tapByPhase = rootTree3D.GetRootTapByPhase();
    var tapTree = new DataTree<Curve>();
    foreach (var kvp in tapByPhase.OrderBy(x => x.Key)) {
      var path = new GH_Path(kvp.Key);
      tapTree.AddRange(kvp.Value.Cast<Curve>(), path);
    }
    DA.SetDataTree(0, tapTree);

      // Master roots by phase
    var masterByPhase = rootTree3D.GetRootMasterByPhase();
    var masterTree = new DataTree<Curve>();
    foreach (var kvp in masterByPhase.OrderBy(x => x.Key)) {
      var path = new GH_Path(kvp.Key);
      masterTree.AddRange(kvp.Value.Cast<Curve>(), path);
    }
    DA.SetDataTree(1, masterTree);

    // Explorer roots by phase
    var explorerByPhase = rootTree3D.GetRootExplorerByPhase();
    var explorerTree = new DataTree<Curve>();
    foreach (var kvp in explorerByPhase.OrderBy(x => x.Key)) {
      var path = new GH_Path(kvp.Key);
      explorerTree.AddRange(kvp.Value.Cast<Curve>(), path);
    }
    DA.SetDataTree(2, explorerTree);

    // Dead roots by phase
    var deadByPhase = rootTree3D.GetRootDeadByPhase();
    var deadTree = new DataTree<Curve>();
    foreach (var kvp in deadByPhase.OrderBy(x => x.Key)) {
      var path = new GH_Path(kvp.Key);
      deadTree.AddRange(kvp.Value.Cast<Curve>(), path);
    }
    DA.SetDataTree(3, deadTree);
  }
}
}  // namespace BeingAliveLanguage
