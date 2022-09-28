using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Windows.Forms;

namespace BeingAliveLanguage
{

    public class BALsoilBase : GH_Component
    {
        public BALsoilBase()
          : base("Soil Base", "balSoilBase",
            "Generate a base map from the boundary rectangle.",
            "BAL", "01::soil")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddRectangleParameter("Boundary", "Bound", "Boundary rectangle.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Resolution", "res", "Vertical resolution of the generated grid.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Base", "soilBase", "The base object used for soil diagram generation.", GH_ParamAccess.item);
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
            var (uL, res) = balCore.MakeTriMap(ref rec, rsl);
            rec.ToNurbsCurve().TryGetPlane(out Plane curPln);

            var triArray = new List<Polyline>();
            for (int i = 0; i < res.Count; i++)
            {
                var path = new GH_Path(i);
                triArray.AddRange(res[i].Select(x => x.ToPolyline()).ToList());
            }

            DA.SetData(0, new SoilBase(rec, curPln, triArray, uL));
            //var sBase = SoilBase();
            //DA.SetDataTree(0, triArray);
            //DA.SetData(1, uL);
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilBase;

        public override Guid ComponentGuid => new Guid("140A327A-B36E-4D39-86C5-317D7C24E7FE");
    }

    public class BALgeneralSoil : GH_Component
    {
        public BALgeneralSoil()
          : base("General Soil Content", "balGeneralSoil",
                "Generate a soil map based on the ratio of 3 soil contents, and avoid rock area rocks if rock curves are provided.",
                "BAL", "01::soil")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Base", "soilBase", "soil base triangle map.", GH_ParamAccess.item);
            //pManager.AddCurveParameter("Soil Base", "T", "soil base triangle map.", GH_ParamAccess.list);
            //pManager[0].DataMapping = GH_DataMapping.Flatten; // flatten the triangle list by default
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
            var (sandT, siltT, clayT, soilInfo) = balCore.DivGeneralSoilMap(in sBase.soilT, in ratio, in rock);

            DA.SetData(0, soilInfo);
            DA.SetDataList(1, sandT);
            DA.SetDataList(2, siltT);
            DA.SetDataList(3, clayT);

            var allT = sandT.Concat(siltT).Concat(clayT).ToList();
            DA.SetDataList(4, allT);
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilDiv;

        public override Guid ComponentGuid => new Guid("53411C7C-0833-49C8-AE71-B1948D2DCC6C");
    }

    public class BALurbanSoil : GH_Component
    {
        public BALurbanSoil()
          : base("Urban Soil Content", "balUrbanSoil",
                "Generate a soil map based on the ratio of soil contents of different urban soil types.",
                "BAL", "01::soil")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Base", "soilBase", "soil base triangle map.", GH_ParamAccess.item);
            pManager[0].DataMapping = GH_DataMapping.Flatten; // flatten the triangle list by default
            pManager.AddNumberParameter("Sand Ratio", "rSand", "The ratio of sand in the soil.", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Clay Ratio", "rClay", "The ratio of clay in the soil.", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Biochar Ratio", "rBiochar", "The ratio of biochar in the soil.", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Stone Ratio", "rStone", "The ratio of stone in the soil.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Relative Stone Size", "szStone", "The relative stone size [1, 10], representing stones dia. from 5mm to 50mm in reality.", GH_ParamAccess.list, 0);
            pManager.AddNumberParameter("Organic Matter Ratio", "rOM", "The ratio of organic matter in the soil.", GH_ParamAccess.item, 0);
            // TODO: if we should separate organic matter out
            pManager[6].Optional = true; // rock can be optionally provided

        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //pManager.AddGenericParameter("Soil Info", "soilInfo", "Info about the current soil based on given content ratio.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Sand Tri", "sandT", "Sand triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Clay Tri", "clayT", "Clay triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Biochar Tri", "biocharT", "Biochar triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Stone Poly", "stonePoly", "Stone polygons.", GH_ParamAccess.list);
            pManager.AddCurveParameter("All Polygon", "allPoly", "Collection of all polygons.", GH_ParamAccess.list);
            pManager.AddLineParameter("Organic Matther", "OM", "Collection of organic matters.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // get data
            SoilBase sBase = new SoilBase();
            double rSand = 0;
            double rClay = 0;
            double rBiochar = 0;
            double rOM = 0;
            var rStone = new List<double>();
            var szStone = new List<double>();
            List<Curve> rock = new List<Curve>();

            if (!DA.GetData(0, ref sBase))
            { return; }
            if (!DA.GetData(1, ref rSand))
            { return; }
            if (!DA.GetData(2, ref rClay))
            { return; }
            if (!DA.GetData(3, ref rBiochar))
            { return; }
            if (!DA.GetDataList(4, rStone))
            { return; }
            if (!DA.GetDataList(5, szStone))
            { return; }
            if (!DA.GetData(6, ref rOM))
            { return; }

            if (rClay == 0 && rStone.Sum() == 0)
            { return; }

            if (rSand + rClay + rBiochar + rOM + rStone.Sum() != 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Ratio of all contents need to sum up to 1. Current value is {rSand + rClay + rBiochar + rOM + rStone.Sum()}");
                return;
            }
            if (szStone.Any(x => x < 1 || x > 10))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Relative stone size out of range [1 - 10].");
            }

            // ! step1: scaling the ratio of sand, clay, biochar, stone if organic matter is presented
            if (rOM != 0)
            {
                var sum = rSand + rClay + rBiochar + rStone.Sum();
                rSand /= sum;
                rClay /= sum;
                rBiochar /= sum;
                rStone = rStone.Select(x => x / sum).ToList();
            }

            // ! step3: conduct subdividing
            // call the actural function
            var urbanS = new SoilUrban(sBase, rSand, rClay, rBiochar, rStone, szStone);
            urbanS.Build();
            urbanS.CollectAll(out List<Polyline> allT);

            // ! step4: offset polylines

            var cPln = sBase.pln;
            var rOffset = Utils.remap(szStone.Sum() / szStone.Count(), 1, 10, 0.95, 0.75);

            var offsetSandT = urbanS.sandT.Select(x => ClipperUtils.OffsetPolygon(cPln, x, rOffset)).ToList();
            var offsetClayT = urbanS.clayT.Select(x => ClipperUtils.OffsetPolygon(cPln, x, rOffset)).ToList();
            var offsetBiocharT = urbanS.biocharT.Select(x => ClipperUtils.OffsetPolygon(cPln, x, rOffset)).ToList();
            var offsetStoneT = stonePoly.Select(x => ClipperUtils.OffsetPolygon(cPln, x, rOffset)).ToList();
            var offsetAllT = offsetSandT.Concat(offsetClayT).Concat(offsetBiocharT).Concat(offsetStoneT).ToList();


            // ! step5: create organic matter
            var omLn = balCore.GenOrganicMatterUrban(sBase, allT, offsetAllT, rOM);
            var biocharFilling = balCore.GenOrganicMatterBiochar(sBase, offsetBiocharT);

            omLn.AddRange(biocharFilling);

            // ! assignment
            int idx = 0;
            DA.SetDataList(idx++, offsetSandT);
            DA.SetDataList(idx++, offsetClayT);
            DA.SetDataList(idx++, offsetBiocharT);
            DA.SetDataList(idx++, offsetStoneT);
            DA.SetDataList(idx++, offsetAllT);
            DA.SetDataList(idx++, omLn);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("4f0a934c-dd27-447c-a67b-a478940c2d6e");
    }

    public class BALsoilInfo : GH_Component
    {
        public BALsoilInfo() :
            base("Soil Information", "balSoilInfoText",
                "Export the soil information in text format.",
                "BAL", "04::utils")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Info", "soilInfo", "Info about the current soil based on given content ratio.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Soil Info Text", "soilText", "Soil Info that can be visualized with the TAG component.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // get data
            SoilProperty soilInfo = new SoilProperty();
            List<Curve> triCrv = new List<Curve>();

            if (!DA.GetData(0, ref soilInfo))
            { return; }


            var sText = balCore.SoilText(soilInfo);

            // assign output
            DA.SetData(0, sText);
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilInfo;
        public override Guid ComponentGuid => new Guid("af64a14a-6795-469c-b044-7db972d5bd84");

    }

    public class BALsoilWaterOffset : GH_Component
    {
        public BALsoilWaterOffset()
          : base("Soil Water Visualization", "balSoilWaterVis",
            "Generate soil diagram with water info.",
            "BAL", "01::soil")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Info", "soilInfo", "Info about the current soil based on given content ratio.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Soil Tri", "soilT", "Soil triangles, can be any or combined triangles of sand, silt, clay.", GH_ParamAccess.list);

            pManager.AddNumberParameter("Current Water ratio", "rCurWater", "The current water ratio [0, 1] in the soil for visualization purposes.", GH_ParamAccess.item, 0.5);
            pManager.AddIntegerParameter("Core Water Hatch Density", "dHatchCore", "Hatch density of the embedded water.", GH_ParamAccess.item, 5);
            pManager.AddIntegerParameter("Available Water Hatch Density", "dHatchAvail", "Hatch density of the current water.", GH_ParamAccess.item, 3);
            pManager[2].Optional = true;
            pManager[3].Optional = true;
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
                balCore.OffsetWater(triCrv, soilInfo, rWater, denEmbedWater, denAvailWater);


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

    public class BALsoilOrganicMatterInner : GH_Component
    {
        public BALsoilOrganicMatterInner()
          : base("Soil Interior Organic Matter", "balSoilOG_in",
            "Generate soil inner organic matter based on given intensity.",
            "BAL", "01::soil")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Info", "soilInfo", "Info about the current soil based on given content ratio.", GH_ParamAccess.item);
            pManager.AddRectangleParameter("Boundary", "Bound", "Boundary rectangle.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Soil Tri", "soilT", "Soil triangles, can be any or combined triangles of sand, silt, clay.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Organic Matter Density", "dOrganics", "Density of organic matter [ 0 - 1 ].", GH_ParamAccess.item, 0.5);

            pManager[3].Optional = true; // OM
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Organic Matter Inner", "OM-inner", "Lines as inner soil organic matter.", GH_ParamAccess.tree);
            pManager.AddGenericParameter("Organic Matter Property", "OM-prop", "Property of inner organic matter to generate top organic matter.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // get data
            SoilProperty soilInfo = new SoilProperty();
            List<Curve> triCrv = new List<Curve>();
            Rectangle3d bnd = new Rectangle3d();
            double dOM = 0.5;

            if (!DA.GetData(0, ref soilInfo))
            { return; }
            if (!DA.GetData(1, ref bnd))
            { return; }
            if (!DA.GetDataList(2, triCrv))
            { return; }
            DA.GetData(3, ref dOM);
            if (dOM <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Density should be larger than 0.");
                return;
            }

            // compute
            var (omLn, omProp) = balCore.GenOrganicMatterInner(bnd, soilInfo, triCrv, dOM);

            GH_Structure<GH_Line> outLn = new GH_Structure<GH_Line>();
            // output data
            for (int i = 0; i < omLn.Count; i++)
            {
                var path = new GH_Path(i);
                outLn.AppendRange(omLn[i].Select(x => new GH_Line(x)), path);
            }

            DA.SetDataTree(0, outLn);
            DA.SetData(1, omProp);
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilOrganicsInner;
        public override Guid ComponentGuid => new Guid("B781B9DE-6953-4E8E-A71A-801592B99CBD");
    }

    public class BALsoilOrganicMatterTop : GH_Component
    {
        public BALsoilOrganicMatterTop()
          : base("Soil Surface Organic Matter", "balSoilOG_top",
            "Generate soil surface organic matter based on given intensity.",
            "BAL", "01::soil")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddRectangleParameter("Boundary", "Bound", "Boundary rectangle.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("OM Unit Size", "S", "Sizing: 0 - S, 1 - M, 2 - L.", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("numLayer", "numL", "Layer number of surface organic matter", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Unit Length", "uL", "The triangle's side length", GH_ParamAccess.item);
            pManager.AddNumberParameter("Organic Matter Density", "dOrganics", "Density of organic matter [ 0 - 1 ].", GH_ParamAccess.item, 0.5);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Organic Matter Top", "soilOrgTop", "Curves representing organic matter on soil surface.", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // get data
            Rectangle3d bnd = new Rectangle3d();
            double uL = 1;
            int sz = 0;
            double dOM = 0.5;
            int numLayer = 1;

            if (!DA.GetData(0, ref bnd))
            { return; }

            DA.GetData(1, ref sz);
            if (sz != 0 && sz != 1 && sz != 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Sizing param should be one of: 0, 1, 2.");
                return;
            }
            DA.GetData(2, ref numLayer);
            if (numLayer <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Layer number needs to be positive.");
                return;
            }

            if (!DA.GetData(3, ref uL))
            { return; }
            if (uL <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, String.Format("The unit length needs to be positive."));
                return;
            }
            DA.GetData(4, ref dOM);
            if (dOM <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Density needs to be positive.");
                return;
            }



            // compute
            var omLn = balCore.GenOrganicMatterTop(bnd, uL, sz, dOM, numLayer);

            GH_Structure<GH_Line> outLn = new GH_Structure<GH_Line>();
            // output data
            for (int i = 0; i < omLn.Count; i++)
            {
                var path = new GH_Path(i);
                outLn.AppendRange(omLn[i].Select(x => new GH_Line(x)), path);
            }

            DA.SetDataTree(0, outLn);
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilOrganicsTop;
        public override Guid ComponentGuid => new Guid("6BE29C7A-7BE9-4DBD-9202-61FC5201E79F");
    }

    public class BALsoilOrganicMatterTopAlter : GH_Component
    {
        public BALsoilOrganicMatterTopAlter()
          : base("Soil Surface Organic Matter (dependent version)", "balSoilOG_topDepend",
            "Generate soil surface organic matter based on properties from the inner organic matter.",
            "BAL", "01::soil")
        {
        }

        /// <summary>
        /// icon position in a category
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("OM Property", "omProp", "Organic matter property from soil inner organic matter component.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("OM Unit Size", "S", "Sizing: 0 - S, 1 - M, 2 - L.", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("numLayer", "numL", "Layer number of surface organic matter", GH_ParamAccess.item, 1);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Organic Matter Top", "soilOrgTop", "Curves representing organic matter on soil surface.", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // get data
            OrganicMatterProperty omProp = new OrganicMatterProperty();
            int sz = 0;
            int numLayer = 1;

            if (!DA.GetData(0, ref omProp))
            { return; }

            DA.GetData(1, ref sz);
            if (sz != 0 && sz != 1 && sz != 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Sizing param should be one of: 0, 1, 2.");
                return;
            }

            DA.GetData(2, ref numLayer);
            if (numLayer <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Layer number needs to be positive.");
                return;
            }


            // compute
            var omLn = balCore.GenOrganicMatterTop(omProp, sz, numLayer);

            GH_Structure<GH_Line> outLn = new GH_Structure<GH_Line>();
            // output data
            for (int i = 0; i < omLn.Count; i++)
            {
                var path = new GH_Path(i);
                outLn.AppendRange(omLn[i].Select(x => new GH_Line(x)), path);
            }

            DA.SetDataTree(0, outLn);
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilOrganicsTopDep;
        public override Guid ComponentGuid => new Guid("fde1d789-19ea-41aa-8330-0176f4289d1e");
    }
}
