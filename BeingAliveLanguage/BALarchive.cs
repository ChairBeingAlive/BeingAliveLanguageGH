using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BeingAliveLanguage.BalCore;

namespace BeingAliveLanguage
{

  public class BALGaussen_OBSOLETE : GH_Component
  {
    public BALGaussen_OBSOLETE()
      : base("Climate_GaussenDiagram", "balClimate_Gaussen",
          "Automatically draw the Bagnouls-Gaussen diagram with given climate data.",
          "BAL", "05::climate")
    {
    }
    private bool useSI = true; // True for Metric, False for Imperial
    protected override Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balGaussen);
    public override GH_Exposure Exposure => GH_Exposure.hidden;
    public override Guid ComponentGuid => new Guid("3C5480D5-32B6-4EAD-A945-4F81D109EBEA");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddPlaneParameter("Plane", "pln", "The plane to draw the diagram.", GH_ParamAccess.item, Plane.WorldXY);
      pManager[0].Optional = true;

      pManager.AddNumberParameter("Precipitation", "Prec", "Precipitation [mm] of given location in 12 months. Right click to switch between SI (mm) and Imperial system [in].", GH_ParamAccess.list);
      pManager[1].Optional = true;

      pManager.AddNumberParameter("Temperature", "Temp", "Temperature of given location in 12 months. Right click to switch between SI (°C) and Imperial system (°F).", GH_ParamAccess.list);
      pManager[2].Optional = true;

      pManager.AddNumberParameter("Scale", "s", "Scale of the diagram.", GH_ParamAccess.item, 1.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddCurveParameter("Frame", "F", "The Frame of the Gaussen diagram.", GH_ParamAccess.list);

      pManager.AddCurveParameter("TempCrv", "TC", "The temperature curve of the Gaussen diagram.", GH_ParamAccess.list);
      pManager.AddCurveParameter("PrecCrv", "PC", "The precipitation curve of the Gaussen diagram.", GH_ParamAccess.list);

      pManager.AddPlaneParameter("LabelLocation", "TxtLoc", "The label location of the Gaussen diagram.", GH_ParamAccess.tree);
      pManager.AddGenericParameter("Label", "Txt", "The label text of the Gaussen diagram.", GH_ParamAccess.tree);
      //pManager.AddPlaneParameter("testPln", "tP", "xxxx", GH_ParamAccess.item);
    }

    protected override void AppendAdditionalComponentMenuItems(System.Windows.Forms.ToolStripDropDown menu)
    {
      base.AppendAdditionalComponentMenuItems(menu);
      Menu_AppendSeparator(menu);

      var metricItem = Menu_AppendItem(menu, "Metric Units", ToggleUnitSystem, true, useSI);
      var imperialItem = Menu_AppendItem(menu, "Imperial Units", ToggleUnitSystem, true, !useSI);
    }

    private void ToggleUnitSystem(object sender, EventArgs e)
    {
      useSI = !useSI;
      ExpireSolution(true);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var pln = Plane.WorldXY;
      var precLst = new List<double>();
      var tempLst = new List<double>();
      double scale = 1.0;

      DA.GetData("Plane", ref pln);
      DA.GetData("Scale", ref scale);

      // data check
      if (!DA.GetDataList("Precipitation", precLst) || !DA.GetDataList("Temperature", tempLst))
      { return; }

      if (precLst.Count != 12 || tempLst.Count != 12)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Precipitation and temperature data should be 12 months.");
        return;
      }

      double unitLen = 10;
      // ! frame
      var horAxis = new Line(pln.Origin, pln.XAxis * unitLen * scale);
      var verAxis1 = new Line(pln.Origin, pln.YAxis * unitLen * scale);
      var verAxis2 = new Line(horAxis.To, pln.YAxis * unitLen * scale);

      var monthPtParam = horAxis.ToNurbsCurve().DivideByCount(11, true, out Point3d[] monthPt);
      var monthAxis = monthPt.ToArray().Select(x => new Line(x, -pln.YAxis * 0.2 * scale)).ToList();

      var frameLn = new List<Line> { horAxis, verAxis1, verAxis2 }.Concat(monthAxis).ToList();
      DA.SetDataList("Frame", frameLn.Concat(monthAxis));

      // ! label
      // month label
      var monLocPt = monthAxis.Select(x => x.To + x.Direction).ToList();
      var monLoc = monLocPt.Select(x => new Plane(x, pln.XAxis, pln.YAxis)).ToList();
      var monText = new List<string> { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

      // parcipitation, temp label
      var parcLocPt = (verAxis1.From + verAxis1.To) * 0.5 - pln.XAxis * 1.5 * scale;
      var parcLoc = new Plane(parcLocPt, pln.YAxis, -pln.XAxis);
      var parcText = "Precipitation (mm)";

      var tempLocPt = (verAxis2.From + verAxis2.To) * 0.5 + pln.XAxis * 1.5 * scale;
      var tempLoc = new Plane(tempLocPt, -pln.YAxis, pln.XAxis);
      var tempText = "Temperature (°C)";

      if (!useSI)
      {
        parcText = "Precipitation (in)";
        tempText = "Temperature (°F)";

        // ! No need to convert. Input are also in the same system.
        // Convert precipitation from inches to millimeters
        //precLst = precLst.Select(x => Utils.MmToInch(x)).ToList();

        // Convert temperature from Fahrenheit to Celsius
        //tempLst = tempLst.Select(x => Utils.ToFahrenheit(x)).ToList();
      }

      int locIdx = 0;
      DataTree<Plane> labelLoc = new DataTree<Plane>();
      labelLoc.AddRange(monLoc, new GH_Path(locIdx++));
      labelLoc.Add(parcLoc, new GH_Path(locIdx++));
      labelLoc.Add(tempLoc, new GH_Path(locIdx++));


      int labelIdx = 0;
      DataTree<string> labelTxt = new DataTree<string>();
      labelTxt.AddRange(monText, new GH_Path(labelIdx++));
      labelTxt.Add(parcText, new GH_Path(labelIdx++));
      labelTxt.Add(tempText, new GH_Path(labelIdx++));

      // ! curve
      // 0.1 - 0.9 ratio of the total height of the diagram.
      double diagramVertL = unitLen * scale * 0.1;
      double diagramVertH = unitLen * scale * 0.9;

      // automatically determin between three precipitation range:
      // high: 100, 200, 300, 500 
      // low:  0
      var high_0 = useSI ? 100 : Utils.MmToInch(100);
      var high_1 = useSI ? 200 : Utils.MmToInch(200);
      var high_2 = useSI ? 300 : Utils.MmToInch(300);
      var high_3 = useSI ? 500 : Utils.MmToInch(500);
      double maxPrec = (precLst.Max() > high_0 ? precLst.Max() > high_1 ? precLst.Max() > high_2 ? high_3 : high_2 : high_1 : high_0);
      List<double> precHeight = precLst.Select(x => Utils.remap(x, 0, maxPrec, diagramVertL, diagramVertH)).ToList();

      // label loc, text
      int precNum = 11;
      List<double> precLabelInterval = Enumerable.Range(0, precNum).Select(x => (double)x * maxPrec / (precNum - 1)).ToList();
      List<Point3d> precLabelLoc = precLabelInterval.Select(x =>
      verAxis1.From
      - pln.XAxis * 0.5 * scale
      + pln.YAxis * Utils.remap(x, 0, maxPrec, diagramVertL, diagramVertH)).ToList();

      labelLoc.AddRange(precLabelLoc.Select(x => new Plane(x, pln.XAxis, pln.YAxis)).ToList(), new GH_Path(locIdx++));
      labelTxt.AddRange(precLabelInterval.Select(x => x.ToString("F1")), new GH_Path(labelIdx++));

      // actual curve.
      var percCrvPt = monthPt.Select(x => x + pln.YAxis * precHeight[monthPt.ToList().IndexOf(x)]).ToList();
      var precCrv = new Polyline(percCrvPt).ToNurbsCurve();

      // automatically determin between three temperature range:
      // low: -30, -20, -10, 0, 10 
      // high:  30, 40, 50
      int tempNum = 7;
      var lowTemps = new List<double> { -30, -20, -10, 0, 10, 20 }.Select(x => useSI ? x : Utils.ToFahrenheit(x)).ToList();
      var highTemps = new List<double> { 30, 40, 50 }.Select(x => useSI ? x : Utils.ToFahrenheit(x)).ToList();

      var maxTemp = (tempLst.Max() > highTemps[0] ? tempLst.Max() > highTemps[1] ? highTemps[2] : highTemps[1] : highTemps[0]);
      var minTemp = (tempLst.Min() < lowTemps[5] ? tempLst.Min() < lowTemps[4] ? tempLst.Min() < lowTemps[3] ? tempLst.Min() < lowTemps[2] ? tempLst.Min() < lowTemps[1] ? lowTemps[0] : lowTemps[1] : lowTemps[2] : lowTemps[3] : lowTemps[4] : lowTemps[5]);

      //List<double> tempLabelInterval = new List<double>();
      //foreach (var px in precLabelInterval)
      //{
      //  if (px * 0.5 <= maxTemp)
      //  {
      //    tempLabelInterval.Add(px * 0.5);
      //  }
      //}

      List<double> tempLabelInterval = Enumerable.Range(0, tempNum).Select(x => (double)x * maxTemp / (tempNum - 1)).ToList();

      var tempLabelLoc = precLabelLoc.Take(tempLabelInterval.Count).Select(x =>
      x - verAxis1.From + verAxis2.From
      + pln.XAxis * scale).ToList();

      var tempHeightUpperLimit = pln.ClosestPoint(precLabelLoc[tempLabelInterval.Count - 1]).Y;
      List<double> tempHeight = tempLst.Select(x => Utils.remap(x, minTemp, tempLabelInterval.Max(), diagramVertL, tempHeightUpperLimit)).ToList();

      labelLoc.AddRange(tempLabelLoc.Select(x => new Plane(x, pln.XAxis, pln.YAxis)).ToList(), new GH_Path(locIdx++));
      labelTxt.AddRange(tempLabelInterval.Select(x => x.ToString("F1")), new GH_Path(labelIdx++));

      // actual curve.
      var tempCrvPt = monthPt.Select(x => x + pln.YAxis * tempHeight[monthPt.ToList().IndexOf(x)]).ToList();
      var tempCrv = new Polyline(tempCrvPt).ToNurbsCurve();


      DA.SetData("TempCrv", tempCrv);
      DA.SetData("PrecCrv", precCrv);


      // set label
      DA.SetDataTree(3, labelLoc);
      DA.SetDataTree(4, labelTxt);
    }
  }

  public class BALsoilDiagramGeneral_OBSOLETE : GH_Component
  {
    public BALsoilDiagramGeneral_OBSOLETE()
      : base("General Soil Content", "balsoilGeneral",
            "Draw a soil map based on the ratio of 3 soil contents, and avoid rock area rocks if rock curves are provided.",
            "BAL", "01::soil")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.hidden;
    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balSoilDiv_win);
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

    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balSoilWaterVis);
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
    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balSoilBase);

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
    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balRootSectional);
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
    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balRootSectional);

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
    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balSoilDiv_win);
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

  public class BALtreeDrenou_OBSOLETE : GH_Component
  {
    public BALtreeDrenou_OBSOLETE()
    : base("Tree_Drenou", "balTree_Drenou",
          "Generate the BAL tree using Drenou's architectural model.",
          "BAL", "03::plant")
    { }

    //string modeUnitary = "non-unitary";
    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTree3D);
    public override Guid ComponentGuid => new Guid("36c5e013-321b-4064-b007-b17880644ce4");
    public override GH_Exposure Exposure => GH_Exposure.hidden;

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
      // duplication
      pManager.AddIntegerParameter("DuplicateNumber", "dupNum", "[0-3] Number of top side branches for duplicate branching.", GH_ParamAccess.list, 0);
    }

    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
      pManager.AddCurveParameter("Trunk", "T", "Tree trunk curves.", GH_ParamAccess.tree);
      pManager.AddCurveParameter("SingleBranch", "SB", "Tree side branch curves (non-split).", GH_ParamAccess.tree);
      pManager.AddCurveParameter("SplitBranch", "TB", "Tree top branch and duplicated branch curves (splitted).", GH_ParamAccess.tree);
      pManager.AddGenericParameter("TreeInfo", "Tinfo", "Information about the tree.", GH_ParamAccess.list);
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

      var dupNumLst = new List<int>();
      if (!DA.GetDataList("DuplicateNumber", dupNumLst))
      { return; }
      if (dupNumLst.Count == 1)
      {
        if (dupNumLst[0] < 0 || dupNumLst[0] > 3)
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "DuplicateNumber is out of range [1, 3], please check.");

        dupNumLst = Enumerable.Repeat(dupNumLst[0], plnLst.Count).ToList();
      }
      else if (dupNumLst.Count != plnLst.Count)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "DuplicateNumber # does not match Plane #, please check.");
      }
      else
      {
        foreach (var n in dupNumLst)
          if (n < 1 || n > 3)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "DuplicateNumber is out of range [1, 3], please check.");
      }

      #endregion

      //! 1. determine horizontal scaling factor of the trees
      Dictionary<int, List<Curve>> branchCol = new Dictionary<int, List<Curve>>();
      Dictionary<int, List<bool>> branchSplitFlagCol = new Dictionary<int, List<bool>>();
      Dictionary<int, List<Curve>> trunkCol = new Dictionary<int, List<Curve>>();

      if (plnLst.Count == 0)
        return;

      // calculate distance between trees
      // todo: currently, only consider distance  between trunks, phases are not considered
      var distLst = new List<double>();
      var nearestTreeLst = new List<List<Point3d>>();
      if (plnLst.Count > 1)
      {
        Utils.GetLstNearestDist(plnLst.Select(x => x.Origin).ToList(), out distLst);
        Utils.GetLstNearestPoint(plnLst.Select(x => x.Origin).ToList(), out nearestTreeLst, 6);

        if (distLst.Min() < 1e-5)
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Trees are too close to each other or overlap, please check.");

      }
      // single tree case, add a virtual tree in the far dist
      else
      {
        //distLst = Enumerable.Repeat(double.MaxValue, plnLst.Count).ToList();

        var virtualLst = new List<Point3d> { new Point3d(double.MaxValue, double.MaxValue, double.MaxValue) };
        nearestTreeLst = Enumerable.Repeat(virtualLst, plnLst.Count).ToList();
      }

      DataTree<Curve> trCrv = new DataTree<Curve>(); // trunk
      DataTree<Curve> singleBrCrv = new DataTree<Curve>(); // side branches
      DataTree<Curve> splitBrCrv = new DataTree<Curve>(); // top branches
      DataTree<TreeProperty> tInfoCol = new DataTree<TreeProperty>(); // tree info

      foreach (var (pln, i) in plnLst.Select((pln, i) => (pln, i)))
      {
        // generate tree
        var t = new Tree3D(pln, gsLst[i], tsLst[i], seedLst[i], brRotLst[i]);
        t.SetNearestTrees(nearestTreeLst[i]);

        t.Generate(phaseLst[i], angLstMain[i], angLstTop[i], dupNumLst[i]);

        // collection branches
        (branchCol, branchSplitFlagCol) = t.GetBranch();
        var maxBr = 0;
        foreach (var (br, id) in branchCol.Select((br, id) => (br, id)))
        {
          maxBr = Math.Max(maxBr, br.Key);
          //if (br.Key > 0 && br.Key <= 4)
          //  singleBrCrv.AddRange(br.Value, new GH_Path(new int[] { i, br.Key }));
          //else
          //  splitBrCrv.AddRange(br.Value, new GH_Path(new int[] { i, br.Key }));


          var curPath = new GH_Path(new int[] { i, br.Key });
          foreach (var (ln, id2) in br.Value.Select((ln, id2) => (ln, id2)))
          {
            if (branchSplitFlagCol[id][id2])
              splitBrCrv.Add(ln, curPath);
            else
              singleBrCrv.Add(ln, curPath);
          }
          //if (!branchSplitFlagCol[br.Key])
          //  singleBrCrv.AddRange(br.Value, new GH_Path(new int[] { i, br.Key }));
          //else
          //  splitBrCrv.AddRange(br.Value, new GH_Path(new int[] { i, br.Key }));
        }


        for (int id = 0; id <= maxBr; id++)
        {
          var path = new GH_Path(i, id);
          if (!singleBrCrv.PathExists(path))
          {
            singleBrCrv.AddRange(new List<Curve>(), new GH_Path(i, id));
          }
          else if (!splitBrCrv.PathExists(path))
          {
            splitBrCrv.AddRange(new List<Curve>(), new GH_Path(i, id));
          }
        }

        // collection of trunk
        var trC = t.GetTrunk();
        trCrv.AddRange(trC, new GH_Path(new int[] { i }));

        // Calculate tree height
        var brPtCol = new List<Point3d>();
        foreach (var (br, id) in branchCol.Select((br, id) => (br, id)))
        {
          foreach (var crv in br.Value)
          {
            t.mPln.RemapToPlaneSpace(crv.PointAtStart, out Point3d mappedPtStart);
            t.mPln.RemapToPlaneSpace(crv.PointAtEnd, out Point3d mappedPtEnd);
            brPtCol.Add(mappedPtStart);
            brPtCol.Add(mappedPtEnd);
          }
        }

        double tHeight = 0;
        foreach (var pt in brPtCol)
        {
          var dir = (pt - t.mPln.Origin);
          tHeight = Math.Max(Math.Abs(Vector3d.Multiply(dir, t.mPln.ZAxis)), tHeight);
        }

        tInfoCol.Add(new TreeProperty(t.mPln, tHeight, phaseLst[i]), new GH_Path(new int[] { i }));
      }

      DA.SetDataTree(0, trCrv);
      DA.SetDataTree(1, singleBrCrv);
      DA.SetDataTree(2, splitBrCrv);
      DA.SetDataTree(3, tInfoCol);
    }
  }
}
