using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace BeingAliveLanguage
{
  public class BALsoilDiagramGeneral_OBSOLETE : GH_Component
  {
    public BALsoilDiagramGeneral_OBSOLETE()
      : base("General Soil Content", "balsoilGeneral",
            "Draw a soil map based on the ratio of 3 soil contents, and avoid rock area rocks if rock curves are provided.",
            "BAL", "01::soil")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.hidden;
    protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilDiv_win;
    public override Guid ComponentGuid => new Guid("53411C7C-0833-49C8-AE71-B1948D2DCC6C");

    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
      pManager.AddGenericParameter("Soil Base", "soilBase", "soil base triangle map.", GH_ParamAccess.item);
      pManager.AddNumberParameter("Sand Ratio", "rSand", "The ratio of sand in the soil.", GH_ParamAccess.item, 1.0);
      pManager.AddNumberParameter("Silt Ratio", "rSilt", "The ratio of silt in the soil.", GH_ParamAccess.item, 0.0);
      pManager.AddNumberParameter("Clay Ratio", "rClay", "The ratio of clay in the soil.", GH_ParamAccess.item, 0.0);
      pManager.AddCurveParameter("Rocks", "R", "Curves represendting the rocks in the soil.", GH_ParamAccess.list);
      pManager[4].DataMapping = GH_DataMapping.Flatten; // flatten the triangle list by default
      pManager[4].Optional = true; // rock can be optionally provided
    }

    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Soil Info", "soilInfo", "Info about the current soil based on given content ratio.", GH_ParamAccess.item);
      pManager.AddCurveParameter("Sand Tri", "sandT", "Sand triangles.", GH_ParamAccess.list);
      pManager.AddCurveParameter("Silt Tri", "siltT", "Silt triangles.", GH_ParamAccess.list);
      pManager.AddCurveParameter("Clay Tri", "clayT", "Clay triangles.", GH_ParamAccess.list);
      pManager.AddCurveParameter("All Tri", "allT", "Collection of all triangles of the three types.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      // get data
      //List<Curve> triL = new List<Curve>();
      var sBase = new SoilBase();
      double rSand = 0;
      double rSilt = 0;
      double rClay = 0;
      List<Curve> rock = new List<Curve>();
      if (!DA.GetData(0, ref sBase))
      { return; }
      if (!DA.GetData(1, ref rSand))
      { return; }
      if (!DA.GetData(2, ref rSilt))
      { return; }
      if (!DA.GetData(3, ref rClay))
      { return; }
      DA.GetDataList(4, rock);

      if (rSand + rClay + rSilt != 1)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Ratio of all content need to sum up to 1.");
        return;
      }

      //List<Polyline> triPoly = sBase.soilT.Select(x => Utils.CvtCrvToPoly(x)).ToList();
      double[] ratio = new double[3] { rSand, rSilt, rClay };

      // call the actural function
      //var (sandT, siltT, clayT, soilInfo) = BalCore.DivGeneralSoilMap(in sBase.soilT, in ratio, in rock);

      //DA.SetData(0, soilInfo);
      //DA.SetDataList(1, sandT);
      //DA.SetDataList(2, siltT);
      //DA.SetDataList(3, clayT);

      //var allT = sandT.Concat(siltT).Concat(clayT).ToList();
      //DA.SetDataList(4, allT);
    }

  }

  public class BALsoilWaterOffset_OBSOLETE : GH_Component
  {
    public BALsoilWaterOffset_OBSOLETE()
      : base("Soil Water Visualization", "balSoilWaterVis",
        "Generate soil diagram with water info.",
        "BAL", "01::soil")
    {
    }

    //public override GH_Exposure Exposure => GH_Exposure.tertiary;
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
      pManager.AddGenericParameter("Soil Info", "soilInfo", "Info about the current soil based on given content ratio.", GH_ParamAccess.item);
      pManager.AddCurveParameter("Soil Triangle", "soilT", "Soil triangles, can be any or combined triangles of sand, silt, clay.", GH_ParamAccess.list);

      pManager.AddNumberParameter("Current Water ratio", "rCurWater", "The current water ratio [0, 1] in the soil for visualization purposes.", GH_ParamAccess.item, 0.5);
      pManager[2].Optional = true;
      pManager.AddIntegerParameter("Core Water Hatch Density", "dHatchCore", "Hatch density of the embedded water.", GH_ParamAccess.item, 5);
      pManager[3].Optional = true;
      pManager.AddIntegerParameter("Available Water Hatch Density", "dHatchAvail", "Hatch density of the current water.", GH_ParamAccess.item, 3);
      pManager[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
      pManager.AddCurveParameter("Soil Core", "soilCore", "Soil core triangles, representing soil content without any water.", GH_ParamAccess.list);
      pManager.AddCurveParameter("Wilting Point", "soilWP", "Soil wilting point triangles.", GH_ParamAccess.list);
      pManager.AddCurveParameter("Field Capacity", "soilFC", "Soil field capacity triangles.", GH_ParamAccess.list);
      pManager.AddCurveParameter("Current WaterLine", "soilCW", "Current water stage.", GH_ParamAccess.list);

      pManager.AddCurveParameter("Embedded Water Hatch", "waterEmbed", "Hatch of the embedded water of the soil.", GH_ParamAccess.tree);
      pManager.AddCurveParameter("Current Water Hatch", "waterCurrent", "Hatch of the current water stage in the soil.", GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      // get data
      SoilProperty soilInfo = new SoilProperty();
      List<Curve> triCrv = new List<Curve>();
      double rWater = 0.5;
      int denEmbedWater = 3;
      int denAvailWater = 3;

      if (!DA.GetData(0, ref soilInfo))
      { return; }
      if (!DA.GetDataList(1, triCrv))
      { return; }
      DA.GetData(2, ref rWater);
      DA.GetData(3, ref denEmbedWater);
      DA.GetData(4, ref denAvailWater);


      // compute offseted curves 
      var (triCore, triWP, triFC, triCW, embedWater, curWater) =
          Utils.OffsetWater(triCrv, soilInfo, rWater, denEmbedWater, denAvailWater);


      // assign output
      DA.SetDataList(0, triCore);
      DA.SetDataList(1, triWP);
      DA.SetDataList(2, triFC);
      DA.SetDataList(3, triCW);


      GH_Structure<GH_Curve> eWTree = new GH_Structure<GH_Curve>();
      GH_Structure<GH_Curve> cWTree = new GH_Structure<GH_Curve>();

      for (int i = 0; i < embedWater.Count; i++)
      {
        var path = new GH_Path(i);
        eWTree.AppendRange(embedWater[i].Select(x => new GH_Curve(x.ToPolylineCurve())), path);
      }

      for (int i = 0; i < curWater.Count; i++)
      {
        var path = new GH_Path(i);
        cWTree.AppendRange(curWater[i].Select(x => new GH_Curve(x.ToPolylineCurve())), path);
      }

      DA.SetDataTree(4, eWTree);
      DA.SetDataTree(5, cWTree);

    }

    protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilWaterVis;
    public override Guid ComponentGuid => new Guid("F6D8797A-674F-442B-B1BF-606D18B5277A");
  }

  public class BALsoilBase_OBSOLETE : GH_Component
  {
    public BALsoilBase_OBSOLETE()
      : base("Soil Base", "balSoilBase",
        "Generate a base map from the boundary rectangle.",
        "BAL", "01::soil")
    {
    }

    public override Guid ComponentGuid => new Guid("140A327A-B36E-4D39-86C5-317D7C24E7FE");
    protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilBase;

    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
      pManager.AddRectangleParameter("Boundary", "Bound", "Boundary rectangle.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Resolution", "res", "Vertical resolution of the generated grid.", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Soil Base", "soilBase", "The base object used for soil diagram generation.", GH_ParamAccess.item);
      pManager.AddGenericParameter("Soil Base Grid", "soilT", "The base grids used for soil diagram generation.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      Rectangle3d rec = new Rectangle3d();
      int rsl = 1;

      if (!DA.GetData(0, ref rec))
      { return; }
      if (!DA.GetData(1, ref rsl))
      { return; }

      // call the actural function
      var (uL, res, _) = Utils.MakeTriMap(ref rec, rsl);
      rec.ToNurbsCurve().TryGetPlane(out Plane curPln);

      var triArray = new List<Polyline>();
      for (int i = 0; i < res.Count; i++)
      {
        var path = new GH_Path(i);
        triArray.AddRange(res[i].Select(x => x.ToPolyline()).ToList());
      }

      DA.SetData(0, new SoilBase(rec, curPln, triArray, uL));
      DA.SetDataList(1, triArray);
    }
  }

  public class BALRootSec_OBSOLETE : GH_Component
  {
    /// <summary>
    /// Initializes a new instance of the MyComponent1 class.
    /// </summary>
    public BALRootSec_OBSOLETE()
      : base("Root_Sectional", "balRoot_S",
          "Draw root in sectional soil map.",
          "BAL", "02::root")
    {
    }

    //public override GH_Exposure Exposure => GH_Exposure.secondary;
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
      pManager.AddGenericParameter("SoilMap", "sMap", "The soil map class to build root upon.", GH_ParamAccess.item);
      pManager.AddPointParameter("Anchor", "A", "Anchor locations of the root(s).", GH_ParamAccess.item);
      pManager.AddNumberParameter("Radius", "R", "Root Radius.", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("RootSectional", "root", "The sectional root drawing.", GH_ParamAccess.list);
    }

    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
      base.AppendAdditionalMenuItems(menu);

      Menu_AppendSeparator(menu);
      Menu_AppendItem(menu, "Root Type:", (sender, e) => { }, false).Font = GH_FontServer.StandardItalic;
      Menu_AppendItem(menu, " Single Form", (sender, e) => Menu.SelectMode(this, sender, e, ref formMode, "single"), true, CheckMode("single"));
      Menu_AppendItem(menu, " Multi  Form", (sender, e) => Menu.SelectMode(this, sender, e, ref formMode, "multi"), true, CheckMode("multi"));
    }

    private bool CheckMode(string _modeCheck) => formMode == _modeCheck;

    public override bool Write(GH_IWriter writer)
    {
      if (formMode != "")
        writer.SetString("formMode", formMode);
      return base.Write(writer);
    }
    public override bool Read(GH_IReader reader)
    {
      if (reader.ItemExists("formMode"))
        formMode = reader.GetString("formMode");

      Message = reader.GetString("formMode").ToUpper();

      return base.Read(reader);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "This component is obsolete, please use the new one.");
    }

    string formMode = "multi";  // s-single, m-multi
    protected override System.Drawing.Bitmap Icon => Properties.Resources.balRootSectional;
    public override Guid ComponentGuid => new Guid("A0E63559-41E8-4353-B78E-510E3FCEB577");
  }

  public class BALRootSectional_OBSOLETE : GH_Component
  {
    /// <summary>
    /// Initializes a new instance of the MyComponent1 class.
    /// </summary>
    public BALRootSectional_OBSOLETE()
      : base("Root_Sectional", "balRoot_S",
          "Draw root in sectional soil map.",
          "BAL", "02::root")
    {
    }

    string formMode = "single";  // none, single, multi
    public override GH_Exposure Exposure => GH_Exposure.hidden;
    public override Guid ComponentGuid => new Guid("E2D1F590-4BE8-4AAD-812E-4BF682F786A4");
    protected override System.Drawing.Bitmap Icon => Properties.Resources.balRootSectional;

    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
      pManager.AddGenericParameter("SoilMap", "sMap", "The soil map class to build root upon.", GH_ParamAccess.item);
      pManager.AddPointParameter("Anchor", "A", "Anchor locations of the root(s).", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Steps", "S", "Root growing steps.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("BranchN", "n", "Root branching number (>= 2, initial branching number from the root anchor.)", GH_ParamAccess.item, 2);
      pManager[3].Optional = true;
      pManager.AddIntegerParameter("seed", "s", "Int seed to randomize the generated root pattern.", GH_ParamAccess.item, -1);
      pManager[4].Optional = true;

      pManager.AddCurveParameter("Env Attractor", "envA", "Environmental attracting area (water, resource, etc.).", GH_ParamAccess.list);
      pManager[5].Optional = true;
      pManager.AddCurveParameter("Env Repeller", "envR", "Environmental repelling area (dryness, poison, etc.).", GH_ParamAccess.list);
      pManager[6].Optional = true;
      pManager.AddNumberParameter("Env DetectionRange", "envD", "The range (to unit length of the grid) that a root can detect surrounding environment.", GH_ParamAccess.item, 5);
      pManager.AddBooleanParameter("EnvAffector Toggle", "envToggle", "Toggle the affects caused by environmental factors.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("RootSectional", "root", "The sectional root drawing.", GH_ParamAccess.list);
    }

    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
      base.AppendAdditionalMenuItems(menu);

      Menu_AppendSeparator(menu);
      Menu_AppendItem(menu, "Topological Branching:", (sender, e) => { }, false).Font = GH_FontServer.StandardItalic;
      Menu_AppendItem(menu, " None", (sender, e) => Menu.SelectMode(this, sender, e, ref formMode, "none"), true, CheckMode("none"));
      Menu_AppendItem(menu, " Level 1", (sender, e) => Menu.SelectMode(this, sender, e, ref formMode, "single"), true, CheckMode("single"));
      Menu_AppendItem(menu, " Level 2", (sender, e) => Menu.SelectMode(this, sender, e, ref formMode, "multi"), true, CheckMode("multi"));
    }

    private bool CheckMode(string _modeCheck) => formMode == _modeCheck;

    public override bool Write(GH_IWriter writer)
    {
      if (formMode != "")
        writer.SetString("formMode", formMode);
      return base.Write(writer);
    }
    public override bool Read(GH_IReader reader)
    {
      if (reader.ItemExists("formMode"))
        formMode = reader.GetString("formMode");

      Message = reader.GetString("formMode").ToUpper();
      return base.Read(reader);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var sMap = new SoilMap();
      var anchor = new Point3d();
      //double radius = 10.0;
      int steps = 10;
      int branchN = 2;
      int seed = -1;

      //if (!DA.GetData("SoilMap", ref sMap) || sMap.mapMode != "sectional")
      if (!DA.GetData("SoilMap", ref sMap))
      { return; }
      if (!DA.GetData("Anchor", ref anchor))
      { return; }
      if (!DA.GetData("Steps", ref steps))
      { return; }
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

      if (branchN < 2)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Root should have branch number >= 2.");
        return;
      }

      if (envToggle)
      {
        if (envAtt.Count != 0)
        {
          foreach (var crv in envAtt)
          {
            if (crv == null)
            { continue; }

            if (!crv.IsClosed)
            {
              AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Attractors contain non-closed curve.");
              return;
            }
          }
        }

        if (envRep.Count != 0)
        {
          foreach (var crv in envRep)
          {
            if (crv == null)
            { continue; }

            if (!crv.IsClosed)
            {
              AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Repellers contain non-closed curve.");
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
    }
  }

  public class BALsoilDiagramGeneral_2_OBSOLETE : GH_Component
  {
    public BALsoilDiagramGeneral_2_OBSOLETE()
      : base("General Soil Separates", "balsoilGeneral",
            "Draw a soil map based on the ratio of 3 soil separates, and avoid rock area rocks if rock curves are provided.",
            "BAL", "01::soil")
    {
    }

    // additional constructor for macOS-version component
    public BALsoilDiagramGeneral_2_OBSOLETE(string name, string nickname, string description, string category, string subCategory)
      : base(name, nickname, description, category, subCategory)
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.hidden;
    protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilDiv_win;
    public override Guid ComponentGuid => new Guid("8634cd28-f37e-4204-b60b-d36b16181d7b");

    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
      pManager.AddGenericParameter("Soil Base", "soilBase", "soil base triangle map.", GH_ParamAccess.item);
      pManager.AddGenericParameter("Soil Info", "soilInfo", "Info about the current soil based on given content ratio.", GH_ParamAccess.item);

      pManager.AddCurveParameter("Rocks", "R", "Curves represendting the rocks in the soil.", GH_ParamAccess.list);
      pManager[2].DataMapping = GH_DataMapping.Flatten; // flatten the triangle list by default
      pManager[2].Optional = true; // rock can be optionally provided

      pManager.AddIntegerParameter("seed", "s", "Int seed for randomize the generated soil pattern.", GH_ParamAccess.item, -1);
      pManager[3].Optional = true; // if no seed is provided, use random seeds


      pManager.AddIntegerParameter("stage", "t", "Int stage index [1 - 8] representing the randomness of the soil separates that are gradually changed by the organic matter.", GH_ParamAccess.item, 5);
      pManager[4].Optional = true; // if no seed is provided, use random seeds
    }

    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
      pManager.AddCurveParameter("Sand Triangle", "sandT", "Sand triangles.", GH_ParamAccess.list);
      pManager.AddCurveParameter("Silt Triangle", "siltT", "Silt triangles.", GH_ParamAccess.list);
      pManager.AddCurveParameter("Clay Triangle", "clayT", "Clay triangles.", GH_ParamAccess.list);
      pManager.AddCurveParameter("All Triangle", "soilT", "Collection of all triangles of the three types.", GH_ParamAccess.list);

      //pManager.AddCurveParameter("debugPts", "dP", "Debugging point list.", GH_ParamAccess.list);
      //pManager.AddNumberParameter("debug", "debugNum", "debugging", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      // get data
      var sBase = new SoilBase();
      var sInfo = new SoilProperty();
      List<Curve> rock = new List<Curve>();
      int seed = -1;
      int stage = 5;
      if (!DA.GetData("Soil Base", ref sBase))
      { return; }
      if (!DA.GetData("Soil Info", ref sInfo))
      { return; }
      DA.GetDataList("Rocks", rock);
      DA.GetData("seed", ref seed);
      DA.GetData("stage", ref stage);


      if (stage < 0 || stage > 8)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Randomness of soil separates distribution should be within the range [1 - 8].");
        return;
      }


      // call the actural function
      var soil = new SoilGeneral(sBase, sInfo, rock, seed, stage);
      soil.Build();

      DA.SetDataList(0, soil.mSandT);
      DA.SetDataList(1, soil.mSiltT);
      DA.SetDataList(2, soil.mClayT);

      DA.SetDataList(3, soil.Collect());

      // debug
      //var res = BeingAliveLanguageRC.Utils.Addition(10, 23.5);
      //DA.SetData(4, res);
    }
  }
}
