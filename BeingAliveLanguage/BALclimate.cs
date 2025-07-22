using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Rhino.Geometry;
using Grasshopper;
using Grasshopper.Kernel.Data;
using BeingAliveLanguage.BalCore;

namespace BeingAliveLanguage {
  public class BALETP : GH_Component {
    public BALETP()
        : base("Climate_HydraulicBalance", "balClimate_HydraBal",
               "Calculate the climatical hydralic balance data and relevant information (evapotranspiration, etc.) for a given location.", "BAL",
               "05::climate") {}
    protected override Bitmap Icon =>
        SysUtils.cvtByteBitmap(Properties.Resources.balEvapotranspiration);
    public override Guid ComponentGuid => new Guid("F99420CC-DFFB-4538-9207-83F24AA57FF9");

    protected override void RegisterInputParams(GH_InputParamManager pManager) {
      pManager.AddNumberParameter("Precipitation", "P",
                                  "Precipitation of given location in 12 months.",
                                  GH_ParamAccess.list);
      pManager.AddNumberParameter("Temperature", "T", "Temperature of given location in 12 months.",
                                  GH_ParamAccess.list);
      pManager.AddNumberParameter("Latitude", "Lat", "Latitude of the given location.",
                                  GH_ParamAccess.item);
      // pManager.AddNumberParameter("MaxReserve", "maxRes", "Maximum reserved water of previous
      // years.", GH_ParamAccess.item);
      pManager.AddGenericParameter("SoilInfo", "soilInfo",
                                   "Info about the current soil based on given content ratio.",
                                   GH_ParamAccess.item);
      pManager.AddNumberParameter("SoilDepth", "soilDep", "The depth [m] of the target soil.",
                                  GH_ParamAccess.item, 0.5);
      pManager[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager) {
      pManager.AddNumberParameter("Potential Evapotranspiration (Corrected)", "PET.corr",
                                  "Corrected evapotranspiration (mm/yr).", GH_ParamAccess.list);
      pManager.AddNumberParameter("Actual Evapotranspiration", "ETa",
                                  "Real evapotranspiration (mm/yr).", GH_ParamAccess.list);
      pManager.AddNumberParameter("Surplus", "SUR",
                                  "The water that is not evapotranspired or held in the soil (mm).",
                                  GH_ParamAccess.list);
      pManager.AddNumberParameter(
          "Deficit", "DEF",
          "The difference between the maximum evapotranspiration and the water in the system (mm).",
          GH_ParamAccess.list);
      pManager.AddNumberParameter(
          "Reserve", "RES", "The ammount of water reserved in the soil (mm).", GH_ParamAccess.list);
      pManager.AddNumberParameter(
          "MaxReserve", "maxRES",
          "The maximum ammount of water can be reserved in the soil (mm). When this value is reached, the soil is fully saturated.",
          GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA) {
      // ! obtain data
      var temp = new List<double>();
      var precipitation = new List<double>();
      if (!DA.GetDataList(0, precipitation)) {
        return;
      }
      if (!DA.GetDataList(1, temp)) {
        return;
      }

      double lat = 0;
      if (!DA.GetData(2, ref lat)) {
        return;
      }
      if (lat > 90 || lat < -90)  // no correction factor available
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Latitude should be within [0, 90].");
        return;
      }

      SoilProperty soilInfo = new SoilProperty();
      if (!DA.GetData("SoilInfo", ref soilInfo)) {
        return;
      }

      double soilDepth = 0.5;
      if (!DA.GetData("SoilDepth", ref soilDepth)) {
        return;
      }

      // ! Heat Index related
      var heatIdx = temp.Select(x => Math.Pow(x / 5, 1.514)).ToList();
      var sumHeatIdx = heatIdx.Sum();
      var aHeatIdx = 0.492 + (0.0179 * sumHeatIdx) - (0.0000771 * sumHeatIdx * sumHeatIdx) +
                     (0.000000675 * Math.Pow(sumHeatIdx, 3));

      // ! ETP related
      var etpUncorrected =
          temp.Select(x => 16 * Math.Pow((x * 10 / sumHeatIdx), aHeatIdx)).ToList();
      var correctFactor = Utils.GetCorrectionFactorPET(lat);
      var etpCorrected = etpUncorrected.Zip(correctFactor, (x, y) => (x * y)).ToList();

      // !ETR, surplus, reserve, etc.
      var etr = new List<double>();
      var surplus = new List<double>();
      var reserve = new List<double>();
      var maxReserve = (soilInfo.fieldCapacity - soilInfo.wiltingPoint) * 1000 * soilDepth;

      // dry-run for one year to get the December data
      var decemberRes = maxReserve;
      for (int i = 0; i < 12; i++) {
        var previousRes = (i == 0 ? decemberRes : reserve[i - 1]);
        var curETR = Math.Min(etpCorrected[i], precipitation[i] + previousRes);
        var curRes = Math.Min(previousRes + precipitation[i] - curETR, maxReserve);
        reserve.Add(curRes);

        decemberRes = curRes;
      }

      // real calculation
      reserve.Clear();
      for (int i = 0; i < 12; i++) {
        var previousRes = (i == 0 ? decemberRes : reserve[i - 1]);
        var curETR = Math.Min(etpCorrected[i], precipitation[i] + previousRes);
        var curRes = Math.Min(previousRes + precipitation[i] - curETR, maxReserve);
        var curSur = Math.Max(previousRes + precipitation[i] - curETR - maxReserve, 0);

        etr.Add(curETR);
        surplus.Add(curSur);
        reserve.Add(curRes);
      }

      var deficit = etpCorrected.Zip(etr, (x, y) => (x - y)).ToList();

      // ! Set data output
      DA.SetDataList(0, etpCorrected);
      DA.SetDataList(1, etr);
      DA.SetDataList(2, surplus);
      DA.SetDataList(3, deficit);
      DA.SetDataList(4, reserve);
      DA.SetData(5, maxReserve);
    }
  }

  public class BALGaussen : GH_Component {
    public BALGaussen()
        : base("Climate_GaussenDiagram", "balClimate_Gaussen",
               "Automatically draw the Bagnouls-Gaussen diagram with given climate data.", "BAL",
               "05::climate") {}
    private bool useSI = true;  // True for Metric, False for Imperial
    protected override Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balGaussen);
    public override Guid ComponentGuid => new Guid("8184F7FD-5A24-49F2-A12E-9E5AAC7998DA");

    protected override void RegisterInputParams(GH_InputParamManager pManager) {
      pManager.AddPlaneParameter("Plane", "pln", "The plane to draw the diagram.",
                                 GH_ParamAccess.item, Plane.WorldXY);
      pManager[0].Optional = true;

      pManager.AddNumberParameter(
          "Precipitation", "Prec",
          "Precipitation of given location in 12 months. Right click to switch between SI [mm] and Imperial system [in].",
          GH_ParamAccess.list);
      pManager[1].Optional = true;
      pManager.AddIntervalParameter("Precipitation Range", "Prec-range",
                                    "Range of the precipitation. Omit to use auto-scaling feature.",
                                    GH_ParamAccess.item);
      pManager[2].Optional = true;

      pManager.AddNumberParameter(
          "Temperature", "Temp",
          "Temperature of given location in 12 months. Right click to switch between SI (°C) and Imperial system (°F).",
          GH_ParamAccess.list);
      pManager[3].Optional = true;
      pManager.AddIntervalParameter("Temperature Range", "Temp-range",
                                    "Range of the temperature. Omit to use auto-scaling feature.",
                                    GH_ParamAccess.item);
      pManager[4].Optional = true;

      pManager.AddNumberParameter("Scale", "s", "Scale of the diagram.", GH_ParamAccess.item, 1.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager) {
      pManager.AddCurveParameter("Frame", "F", "The Frame of the Gaussen diagram.",
                                 GH_ParamAccess.list);

      pManager.AddCurveParameter("TempCrv", "TC", "The temperature curve of the Gaussen diagram.",
                                 GH_ParamAccess.list);
      pManager.AddCurveParameter("PrecCrv", "PC", "The precipitation curve of the Gaussen diagram.",
                                 GH_ParamAccess.list);

      pManager.AddPlaneParameter("LabelLocation", "TxtLoc",
                                 "The label location of the Gaussen diagram.", GH_ParamAccess.tree);
      pManager.AddGenericParameter("Label", "Txt", "The label text of the Gaussen diagram.",
                                   GH_ParamAccess.tree);
      // pManager.AddPlaneParameter("testPln", "tP", "xxxx", GH_ParamAccess.item);
    }

    protected override void AppendAdditionalComponentMenuItems(
        System.Windows.Forms.ToolStripDropDown menu) {
      base.AppendAdditionalComponentMenuItems(menu);
      Menu_AppendSeparator(menu);

      var metricItem = Menu_AppendItem(menu, "SI Units", ToggleUnitSystem, true, useSI);
      var imperialItem = Menu_AppendItem(menu, "Imperial Units", ToggleUnitSystem, true, !useSI);
    }

    private void ToggleUnitSystem(object sender, EventArgs e) {
      useSI = !useSI;
      ExpireSolution(true);
    }

    protected override void SolveInstance(IGH_DataAccess DA) {
      var pln = Plane.WorldXY;
      var precLst = new List<double>();
      var tempLst = new List<double>();
      double scale = 1.0;
      var precRng = new Interval();
      var tempRng = new Interval();

      DA.GetData("Plane", ref pln);
      DA.GetData("Scale", ref scale);

      // data check
      if (!DA.GetDataList("Precipitation", precLst) || !DA.GetDataList("Temperature", tempLst)) {
        return;
      }
      DA.GetData("Precipitation Range", ref precRng);
      DA.GetData("Temperature Range", ref tempRng);

      if (precLst.Count != 12 || tempLst.Count != 12) {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                          "Precipitation and temperature data should be 12 months.");
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
      var monText = new List<string> { "Jan", "Feb", "Mar", "Apr", "May", "Jun",
                                       "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

      // parcipitation, temp label
      var parcLocPt = (verAxis1.From + verAxis1.To) * 0.5 - pln.XAxis * 1.5 * scale;
      var parcLoc = new Plane(parcLocPt, pln.YAxis, -pln.XAxis);
      var parcText = "Precipitation (mm)";

      var tempLocPt = (verAxis2.From + verAxis2.To) * 0.5 + pln.XAxis * 1.5 * scale;
      var tempLoc = new Plane(tempLocPt, -pln.YAxis, pln.XAxis);
      var tempText = "Temperature (°C)";

      if (!useSI) {
        parcText = "Precipitation (in)";
        tempText = "Temperature (°F)";
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
      double maxPrec = 0.0;
      // if have a range input, use it
      if (precRng.T1 > precRng.T0) {
        maxPrec = precRng.T1;
      } else {
        maxPrec = (precLst.Max() > high_0
                       ? precLst.Max() > high_1 ? precLst.Max() > high_2 ? high_3 : high_2 : high_1
                       : high_0);
      }

      List<double> precHeight =
          precLst.Select(x => Utils.remap(x, 0, maxPrec, diagramVertL, diagramVertH)).ToList();

      // label loc, text
      int precNum = 11;
      List<double> precLabelInterval =
          Enumerable.Range(0, precNum).Select(x => (double)x * maxPrec / (precNum - 1)).ToList();
      List<Point3d> precLabelLoc =
          precLabelInterval
              .Select(x => verAxis1.From - pln.XAxis * 0.5 * scale +
                           pln.YAxis * Utils.remap(x, 0, maxPrec, diagramVertL, diagramVertH))
              .ToList();

      labelLoc.AddRange(precLabelLoc.Select(x => new Plane(x, pln.XAxis, pln.YAxis)).ToList(),
                        new GH_Path(locIdx++));
      labelTxt.AddRange(precLabelInterval.Select(x => x.ToString("F1")), new GH_Path(labelIdx++));

      // actual curve.
      var precCrvPt =
          monthPt.Select(x => x + pln.YAxis * precHeight[monthPt.ToList().IndexOf(x)]).ToList();
      var precCrv = new Polyline(precCrvPt).ToNurbsCurve();

      // automatically determin between three temperature range:
      // low: -30, -20, -10, 0, 10
      // high:  30, 40, 50
      // int tempNum = 7;
      var lowTemps = new List<double> { -30, -20, -10, 0, 10, 20 }
                         .Select(x => useSI ? x : Utils.ToFahrenheit(x))
                         .ToList();
      var highTemps =
          new List<double> { 30, 40, 50 }.Select(x => useSI ? x : Utils.ToFahrenheit(x)).ToList();

      double maxTemp, minTemp;
      if (tempRng.T1 > tempRng.T0) {
        maxTemp = tempRng.T1;
        minTemp = tempRng.T0;
      } else {
        maxTemp = (tempLst.Max() > highTemps[0]
                       ? tempLst.Max() > highTemps[1] ? highTemps[2] : highTemps[1]
                       : highTemps[0]);
        minTemp = (tempLst.Min() < lowTemps[5]
                       ? tempLst.Min() < lowTemps[4]
                             ? tempLst.Min() < lowTemps[3]
                                   ? tempLst.Min() < lowTemps[2]
                                         ? tempLst.Min() < lowTemps[1] ? lowTemps[0] : lowTemps[1]
                                         : lowTemps[2]
                                   : lowTemps[3]
                             : lowTemps[4]
                       : lowTemps[5]);
      }

      // Rule: Precipitation should always be 2x Temperature
      List<double> tempLabelInterval = new List<double>();
      if (useSI) {
        foreach (var px in precLabelInterval) {
          if (px * 0.5 <= maxTemp)
            tempLabelInterval.Add(px * 0.5);
        }
      } else {
        // Imperial Unit
        foreach (var px in precLabelInterval) {
          var px_imperial = Utils.ToCelcius(px);
          if (px_imperial * 0.5 <= Utils.ToCelcius(maxTemp)) {
            tempLabelInterval.Add(px_imperial * 0.5);
          }
        }
      }

      // old
      // List<double> tempLabelInterval = Enumerable.Range(0, tempNum).Select(x => (double)x *
      // maxTemp / (tempNum - 1)).ToList();

      var tempLabelLoc = precLabelLoc.Take(tempLabelInterval.Count)
                             .Select(x => x - verAxis1.From + verAxis2.From + pln.XAxis * scale)
                             .ToList();

      var tempHeightUpperLimit =
          pln.ClosestPoint(precLabelLoc[tempLabelInterval.Count - 1]).Y - pln.Origin.Y;
      List<double> tempHeight = tempLst
                                    .Select(x => Utils.remap(x, minTemp, tempLabelInterval.Max(),
                                                             diagramVertL, tempHeightUpperLimit))
                                    .ToList();

      labelLoc.AddRange(tempLabelLoc.Select(x => new Plane(x, pln.XAxis, pln.YAxis)).ToList(),
                        new GH_Path(locIdx++));
      labelTxt.AddRange(tempLabelInterval.Select(x => x.ToString("F1")), new GH_Path(labelIdx++));

      var tempCrvPt =
          monthPt.Select(x => x + pln.YAxis * tempHeight[monthPt.ToList().IndexOf(x)]).ToList();
      var tempCrv = new Polyline(tempCrvPt).ToNurbsCurve();

      DA.SetData("TempCrv", tempCrv);
      DA.SetData("PrecCrv", precCrv);

      DA.SetDataTree(3, labelLoc);
      DA.SetDataTree(4, labelTxt);
    }
  }
}
