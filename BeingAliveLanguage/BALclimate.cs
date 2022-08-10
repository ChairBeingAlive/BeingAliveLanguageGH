using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grasshopper;
using Grasshopper.Kernel;
using System.Drawing;
using BALcontract;

namespace BeingAliveLanguage
{
    public class BALETP : GH_Component
    {
        public BALETP()
          : base("Climate_Evapotranspiration", "balClimate_ETP",
              "Calculate the climate evapotranspiration related data for a given location.",
              "BAL", "03::climate")
        {
        }
        protected override Bitmap Icon => Properties.Resources.balEvapotranspiration;
        public override Guid ComponentGuid => new Guid("F99420CC-DFFB-4538-9207-83F24AA57FF9");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Temperature", "T", "Temperature of given location in 12 months.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Participation", "P", "Participation of given location in 12 months.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Latitude", "Lat", "Latitude of the given location.", GH_ParamAccess.item);
            pManager.AddNumberParameter("MaxReserve", "maxRes", "Maximum reserved water of previous years.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Evapotraspiration-Corrected", "ETP.corr", "Corrected evapotranspiration (mm/yr).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Evapotraspiration-Real", "ETR", "Real evapotranspiration (mm/yr).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Surplus", "SUR", "The water that is not evapotranspired or held in the soil (mm).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Deficit", "DEF", "The difference between the maximum evapotranspiration and the water in the system (mm).", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ! obtain data
            var temp = new List<double>();
            var precipitation = new List<double>();
            if (!DA.GetDataList(0, temp))
            { return; }
            if (!DA.GetDataList(1, precipitation))
            { return; }

            double lat = 0;
            double maxReserve = 0;
            if (!DA.GetData(2, ref lat))
            { return; }
            if (!DA.GetData(3, ref maxReserve))
            { return; }
            if (lat > 44 || lat < 0) // no correction factor available
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Latitude > 44-deg, data unavailable.");
                return;
            }

            // ! Heat Index related 
            var heatIdx = temp.Select(x => Math.Pow(x / 5, 1.514)).ToList();
            var sumHeatIdx = heatIdx.Sum();
            var aHeatIdx = 0.492 + (0.0179 * sumHeatIdx) - (0.0000771 * sumHeatIdx * sumHeatIdx) + (0.000000675 * Math.Pow(sumHeatIdx, 3));

            // ! ETP related
            var etpUncorrected = temp.Select(x => 16 * Math.Pow((x * 10 / sumHeatIdx), aHeatIdx)).ToList();
            var correctFactor = Utils.GetCorrectionFactorETP(lat);
            var etpCorrected = etpUncorrected.Zip(correctFactor, (x, y) => (x * y)).ToList();

            // !ETR, surplus, reserve, etc.
            var etr = new List<double>();
            var surplus = new List<double>();
            var reserve = new List<double>();
            for (int i = 0; i < 12; i++)
            {
                var previousRes = (i == 0 ? maxReserve : reserve[i - 1]);
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
            DA.SetDataList(3, reserve);
        }
    }
}
