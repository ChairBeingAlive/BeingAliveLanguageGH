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

using GH_IO.Serialization;

namespace BeingAliveLanguage
{

    public class BALsoilAnalysis : GH_Component
    {
        public BALsoilAnalysis()
            : base("Soil Analysis", "balSoilAna",
                 "Analysis the soil composition and determine the soil information.",
                 "BAL", "01::soil")
        {
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilAnalysis;
        public override Guid ComponentGuid => new Guid("F12AEDCA-4FE1-4734-8E71-249C9D90CBA1");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Sand Ratio", "rSand", "The ratio of sand in the soil.", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Silt Ratio", "rSilt", "The ratio of silt in the soil.", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Clay Ratio", "rClay", "The ratio of clay in the soil.", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Info", "soilInfo", "Info about the current soil based on given content ratio.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double rSand = 0;
            double rSilt = 0;
            double rClay = 0;
            if (!DA.GetData("Sand Ratio", ref rSand))
            { return; }
            if (!DA.GetData("Silt Ratio", ref rSilt))
            { return; }
            if (!DA.GetData("Clay Ratio", ref rClay))
            { return; }

            if (rSand + rClay + rSilt != 1 || rSand < 0 || rClay < 0 || rSilt < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Ratio of all content need to sum up to 1. Only positive ratio allowed.");
                return;
            }

            var soilProperty = BalCore.SoilType(rSand, rSilt, rClay);

            DA.SetData("Soil Info", soilProperty);
        }
    }

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
            var (uL, res) = BalCore.MakeTriMap(ref rec, rsl);
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

        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilBase;

        public override Guid ComponentGuid => new Guid("140A327A-B36E-4D39-86C5-317D7C24E7FE");
    }

    public class BALsoilDiagramGeneral : GH_Component
    {
        public BALsoilDiagramGeneral()
          : base("General Soil Separates", "balsoilGeneral",
                "Draw a soil map based on the ratio of 3 soil separates, and avoid rock area rocks if rock curves are provided.",
                "BAL", "01::soil")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilDiv;
        public override Guid ComponentGuid => new Guid("8634cd28-f37e-4204-b60b-d36b16181d7b");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Base", "soilBase", "soil base triangle map.", GH_ParamAccess.item);
            pManager.AddGenericParameter("Soil Info", "soilInfo", "Info about the current soil based on given content ratio.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Rocks", "R", "Curves represendting the rocks in the soil.", GH_ParamAccess.list);
            pManager[2].DataMapping = GH_DataMapping.Flatten; // flatten the triangle list by default
            pManager[2].Optional = true; // rock can be optionally provided
            pManager.AddIntegerParameter("seed", "s", "Int seed for randomize the generated soil pattern.", GH_ParamAccess.item, -1);
            pManager[3].Optional = true; // rock can be optionally provided
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Sand Triangle", "sandT", "Sand triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Silt Triangle", "siltT", "Silt triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Clay Triangle", "clayT", "Clay triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("All Triangle", "soilT", "Collection of all triangles of the three types.", GH_ParamAccess.list);

            //pManager.AddNumberParameter("debug", "debugNum", "debugging", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // get data
            //List<Curve> triL = new List<Curve>();
            var sBase = new SoilBase();
            var sInfo = new SoilProperty();
            List<Curve> rock = new List<Curve>();
            int seed = -1;
            if (!DA.GetData("Soil Base", ref sBase))
            { return; }
            if (!DA.GetData("Soil Info", ref sInfo))
            { return; }
            DA.GetDataList("Rocks", rock);
            DA.GetData("seed", ref seed);


            //List<Polyline> triPoly = sBase.soilT.Select(x => Utils.CvtCrvToPoly(x)).ToList();
            //double[] ratio = new double[3] { sInfo.rSand, sInfo.rSilt, sInfo.rClay };

            // call the actural function
            //var (sandT, siltT, clayT, soilInfo) = BalCore.DivGeneralSoilMap(in sBase.soilT, in ratio, in rock);
            var soil = new SoilGeneral(sBase, sInfo, rock, seed);
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


    public class BALsoilDiagramUrban : GH_Component
    {
        public BALsoilDiagramUrban()
          : base("Urban Soil", "balsoilUrban",
                "Draw a soil map based on the ratio of soil compositions of different urban soil types.",
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
            pManager.AddNumberParameter("Relative Stone Size", "szStone", "The relative stone size [1, 5], representing stones dia. from 5mm to 50mm in reality.", GH_ParamAccess.list, 3);
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
            pManager.AddCurveParameter("Stone Poly", "stonePoly", "Stone polygons.", GH_ParamAccess.tree);
            //pManager.AddCurveParameter("All Polygon", "allPoly", "Collection of all polygons.", GH_ParamAccess.list);
            pManager.AddLineParameter("Organic Matther", "OM", "Collection of organic matters.", GH_ParamAccess.list);

            //pManager.AddPointParameter("StoneCentre", "stoneCen", "Centres of the stone.", GH_ParamAccess.list);
            //pManager.AddCurveParameter("StoneCol", "stoneCollection", "Collections of the stone poly.", GH_ParamAccess.tree);
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

            if (rSand > 0 && rStone.Sum() > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No urban stone contains sand and stone simultaneously.");
                return;
            }
            if (rSand + rClay + rBiochar + rOM + rStone.Sum() != 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Ratio of all contents need to sum up to 1. Current value is {rSand + rClay + rBiochar + rOM + rStone.Sum()}");
                return;
            }
            if (szStone.Any(x => x < 1 || x > 5))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Relative stone size out of range [1 - 5].");
                return;
            }
            if (rStone.Count != szStone.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "# of Stone ratios and sizes need to be matched.");
                return;
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
            var rOffset = Utils.remap(szStone.Sum() / szStone.Count(), 1, 10, 0.97, 0.91);

            var offsetSandT = urbanS.sandT.Select(x => ClipperUtils.OffsetPolygon(cPln, x, rOffset)).ToList();
            var offsetClayT = urbanS.clayT.Select(x => ClipperUtils.OffsetPolygon(cPln, x, rOffset)).ToList();
            var offsetBiocharT = urbanS.biocharT.Select(x => ClipperUtils.OffsetPolygon(cPln, x, rOffset)).ToList();

            // ! For stone polylines, we need to create a tree structure for storing them
            //var offsetStoneT = urbanS.stonePoly.Select(x => ClipperUtils.OffsetPolygon(cPln, x, rOffset)).ToList();

            GH_Structure<GH_Curve> offsetStonePoly = new GH_Structure<GH_Curve>();
            for (int i = 0; i < urbanS.stonePoly.Count; i++)
            {
                var path = new GH_Path(i);
                //offsetStonePoly.AppendRange(urbanS.stonePoly[i].Select(x => new GH_Curve(x.ToPolylineCurve())), path);
                offsetStonePoly.AppendRange(urbanS.stonePoly[i].Select(x => new GH_Curve(ClipperUtils.OffsetPolygon(cPln, x, rOffset).ToPolylineCurve())), path);
            }

            var offsetAllT = allT.Select(x => ClipperUtils.OffsetPolygon(cPln, x, rOffset)).ToList();

            // ! step5: create organic matter
            var omLn = BalCore.GenOrganicMatterUrban(sBase, allT, offsetAllT, rOM);
            var biocharFilling = BalCore.GenOrganicMatterBiochar(sBase, offsetBiocharT);



            List<Polyline> offsetStoneT = new List<Polyline>();
            List<Polyline> originStoneT = new List<Polyline>();
            for (int i = 0; i < urbanS.stonePoly.Count; i++)
            {
                originStoneT.AddRange(urbanS.stonePoly[i]);
                var stoneCol = urbanS.stonePoly[i].Select(x => ClipperUtils.OffsetPolygon(cPln, x, rOffset)).ToList();
                offsetStoneT.AddRange(stoneCol);
            }


            var omStone = BalCore.GenOrganicMatterUrban(sBase, originStoneT, offsetStoneT, rOM);
            var omClay = BalCore.GenOrganicMatterUrban(sBase, urbanS.clayT, offsetClayT, rOM);
            var omSand = BalCore.GenOrganicMatterUrban(sBase, urbanS.sandT, offsetSandT, rOM);

            List<Line> allOM = new List<Line>();
            double tmpOmDist = sBase.unitL / 7.0;

            allOM.AddRange(omStone);
            allOM.AddRange(omClay);
            allOM.AddRange(omSand);

            omLn.AddRange(biocharFilling);

            // ! assignment
            int idx = 0;
            DA.SetDataList(idx++, offsetSandT);
            DA.SetDataList(idx++, offsetClayT);
            DA.SetDataList(idx++, offsetBiocharT);

            DA.SetDataTree(idx++, offsetStonePoly);

            //DA.SetDataTree(idx++, );
            //DA.SetDataList(idx++, omLn);
            DA.SetDataList(idx++, allOM);
            //DA.SetDataList(idx++, urbanS.tmpT);


            // ! helper assignment
            //DA.SetDataList(idx++, urbanS.stoneCen);

            //GH_Structure<GH_Curve> stoneColTree = new GH_Structure<GH_Curve>();
            //for (int i = 0; i < urbanS.stoneCollection.Count; i++)
            //{
            //    var path = new GH_Path(i);
            //    stoneColTree.AppendRange(urbanS.stoneCollection[i].Select(x => new GH_Curve(x.ToPolylineCurve())), path);
            //}
            //DA.SetDataTree(idx++, stoneColTree);

        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilUrban;

        public override Guid ComponentGuid => new Guid("4f0a934c-dd27-447c-a67b-a478940c2d6e");
    }



    public class BALsoilInfo : GH_Component
    {
        public BALsoilInfo() :
            base("Soil_Information", "balSoilInfoText",
                "Export the soil information in text format.",
                "BAL", "09::utils")
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


            var sText = BalCore.SoilText(soilInfo);

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
        public override Guid ComponentGuid => new Guid("320a54cc-ca9a-44a2-b313-2ba08035cb1c");
        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilWaterVis;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Info", "soilInfo", "Info about the current soil based on given content ratio.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Soil Triangle", "soilT", "Soil triangles, can be any or combined triangles of sand, silt, clay.", GH_ParamAccess.list);

            pManager.AddNumberParameter("Current Water ratio", "rCurWater", "The current water ratio [0, 1] in the soil for visualization purposes.", GH_ParamAccess.item, 0.5);
            pManager[2].Optional = true;
            //pManager.AddIntegerParameter("Core Water Hatch Density", "dHatchCore", "Hatch density of the embedded water.", GH_ParamAccess.item, 5);
            //pManager[3].Optional = true;
            //pManager.AddIntegerParameter("Available Water Hatch Density", "dHatchAvail", "Hatch density of the current water.", GH_ParamAccess.item, 3);
            //pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Soil Core", "soilCore", "Soil core triangles, representing soil separates without any water.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Wilting Point", "soilWP", "Soil wilting point triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Field Capacity", "soilFC", "Soil field capacity triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Saturation", "soilST", "Soil saturation triangles.", GH_ParamAccess.list);

            pManager.AddCurveParameter("Current Water Content", "soilCW", "Current water stage.", GH_ParamAccess.list);

            //pManager.AddCurveParameter("Embedded Water Hatch", "waterEmbed", "Hatch of the embedded water of the soil.", GH_ParamAccess.tree);
            //pManager.AddCurveParameter("Current Water Hatch", "waterCurrent", "Hatch of the current water stage in the soil.", GH_ParamAccess.tree);
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
            //DA.GetData(3, ref denEmbedWater);
            //DA.GetData(4, ref denAvailWater);


            // compute offseted curves 
            var (triCore, triWP, triFC, triCW, embedWater, curWater) =
                BalCore.OffsetWater(triCrv, soilInfo, rWater, denEmbedWater, denAvailWater);


            // assign output
            DA.SetDataList(0, triCore);
            DA.SetDataList(1, triWP);
            DA.SetDataList(2, triFC);
            DA.SetDataList(3, triCrv);
            DA.SetDataList(4, triCW);


            //GH_Structure<GH_Curve> eWTree = new GH_Structure<GH_Curve>();
            //GH_Structure<GH_Curve> cWTree = new GH_Structure<GH_Curve>();

            //for (int i = 0; i < embedWater.Count; i++)
            //{
            //    var path = new GH_Path(i);
            //    eWTree.AppendRange(embedWater[i].Select(x => new GH_Curve(x.ToPolylineCurve())), path);
            //}

            //for (int i = 0; i < curWater.Count; i++)
            //{
            //    var path = new GH_Path(i);
            //    cWTree.AppendRange(curWater[i].Select(x => new GH_Curve(x.ToPolylineCurve())), path);
            //}

            //DA.SetDataTree(4, eWTree);
            //DA.SetDataTree(5, cWTree);

        }
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
            pManager.AddGenericParameter("Soil Base", "soilBase", "The base object used for soil diagram generation.", GH_ParamAccess.item);
            pManager.AddGenericParameter("Soil Info", "soilInfo", "Info about the current soil based on given content ratio.", GH_ParamAccess.item);
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
            SoilBase sBase = new SoilBase();
            SoilProperty soilInfo = new SoilProperty();
            List<Curve> soilT = new List<Curve>();
            double dOM = 0.5;

            if (!DA.GetData("Soil Base", ref sBase))
            { return; }
            if (!DA.GetData("Soil Info", ref soilInfo))
            { return; }
            if (!DA.GetDataList("Soil Tri", soilT))
            { return; }
            DA.GetData("Organic Matter Density", ref dOM);
            if (dOM <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Density should be larger than 0.");
                return;
            }

            // compute
            var crvT = soilT.Select(x => Utils.CvtCrvToPoly(x)).ToList();
            var (omLn, omProp) = BalCore.GenOrganicMatterInner(sBase.bnd, soilInfo, crvT, dOM);

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
          : base("Soil Surface Organic Matter (independent version)", "balSoilOG_topInd",
            "Generate soil surface organic matter based on given intensity.",
            "BAL", "01::soil")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Base", "soilBase", "The base object used for soil diagram generation.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("numLayer", "numL", "Layer number of surface organic matter", GH_ParamAccess.item, 1);
            pManager[1].Optional = true;
            pManager.AddNumberParameter("Organic Matter Density", "dOrganics", "Density of organic matter [ 0 - 1 ].", GH_ParamAccess.item, 0.5);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Organic Matter Top", "soilOrgTop", "Curves representing organic matter on soil surface.", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // get data
            var sBase = new SoilBase();
            if (!DA.GetData(0, ref sBase))
            { return; }

            double dOM = 0.5;
            int numLayer = 1;

            DA.GetData(1, ref numLayer);
            if (numLayer <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Layer number needs to be positive.");
                return;
            }

            DA.GetData(2, ref dOM);
            if (dOM <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Density needs to be positive.");
                return;
            }

            int sz = 0;
            switch (omSize)
            {
                case "S":
                    sz = 0;
                    break;
                case "M":
                    sz = 1;
                    break;
                case "L":
                    sz = 2;
                    break;
                default:
                    break;
            }

            // compute
            var omLn = BalCore.GenOrganicMatterTop(sBase, sz, dOM, numLayer);

            GH_Structure<GH_Line> outLn = new GH_Structure<GH_Line>();
            // output data
            for (int i = 0; i < omLn.Count; i++)
            {
                var path = new GH_Path(i);
                outLn.AppendRange(omLn[i].Select(x => new GH_Line(x)), path);
            }

            DA.SetDataTree(0, outLn);
        }

        protected override void BeforeSolveInstance()
        {
            Message = "size: " + omSize.ToUpper();
        }

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);

            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "OM Size:", (sender, e) => { }, false).Font = GH_FontServer.StandardItalic;
            Menu_AppendItem(menu, " S", (sender, e) => Menu.SelectMode(this, sender, e, ref omSize, "S"), true, CheckMode("S"));
            Menu_AppendItem(menu, " M", (sender, e) => Menu.SelectMode(this, sender, e, ref omSize, "M"), true, CheckMode("M"));
            Menu_AppendItem(menu, " L", (sender, e) => Menu.SelectMode(this, sender, e, ref omSize, "L"), true, CheckMode("L"));
        }

        private bool CheckMode(string _modeCheck) => omSize == _modeCheck;

        public override bool Write(GH_IWriter writer)
        {
            if (omSize != "")
                writer.SetString("omSize", omSize);
            return base.Write(writer);
        }
        public override bool Read(GH_IReader reader)
        {
            if (reader.ItemExists("omSize"))
                omSize = reader.GetString("omSize");

            Message = "size: " + reader.GetString("omSize").ToUpper();

            return base.Read(reader);
        }

        private string omSize = "S"; // om sizing: 0-small, 1-middle, 2-big

        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilOrganicsTop;
        public override Guid ComponentGuid => new Guid("6BE29C7A-7BE9-4DBD-9202-61FC5201E79F");
    }

    public class BALsoilOrganicMatterTopAlter : GH_Component
    {
        public BALsoilOrganicMatterTopAlter()
          : base("Soil Surface Organic Matter (dependent version)", "balSoilOG_topDep",
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
            pManager.AddIntegerParameter("numLayer", "numL", "Layer number of surface organic matter", GH_ParamAccess.item, 1);
            pManager[1].Optional = true;
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


            DA.GetData(1, ref numLayer);
            if (numLayer <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Layer number needs to be positive.");
                return;
            }

            switch (omSize)
            {
                case "S":
                    sz = 0;
                    break;
                case "M":
                    sz = 1;
                    break;
                case "L":
                    sz = 2;
                    break;
                default:
                    break;
            }


            // compute
            var omLn = BalCore.GenOrganicMatterTop(omProp, sz, numLayer);

            GH_Structure<GH_Line> outLn = new GH_Structure<GH_Line>();
            // output data
            for (int i = 0; i < omLn.Count; i++)
            {
                var path = new GH_Path(i);
                outLn.AppendRange(omLn[i].Select(x => new GH_Line(x)), path);
            }

            DA.SetDataTree(0, outLn);
        }

        protected override void BeforeSolveInstance()
        {
            Message = "size: " + omSize.ToUpper();
        }

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);

            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "OM Size:", (sender, e) => { }, false).Font = GH_FontServer.StandardItalic;
            Menu_AppendItem(menu, " S", (sender, e) => Menu.SelectMode(this, sender, e, ref omSize, "S"), true, CheckMode("S"));
            Menu_AppendItem(menu, " M", (sender, e) => Menu.SelectMode(this, sender, e, ref omSize, "M"), true, CheckMode("M"));
            Menu_AppendItem(menu, " L", (sender, e) => Menu.SelectMode(this, sender, e, ref omSize, "L"), true, CheckMode("L"));
        }

        private bool CheckMode(string _modeCheck) => omSize == _modeCheck;

        public override bool Write(GH_IWriter writer)
        {
            if (omSize != "")
                writer.SetString("omSize", omSize);
            return base.Write(writer);
        }
        public override bool Read(GH_IReader reader)
        {
            if (reader.ItemExists("omSize"))
                omSize = reader.GetString("omSize");

            Message = "size: " + reader.GetString("omSize").ToUpper();

            return base.Read(reader);
        }

        private string omSize = "S"; // om sizing: 0-small, 1-middle, 2-big

        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilOrganicsTopDep;
        public override Guid ComponentGuid => new Guid("fde1d789-19ea-41aa-8330-0176f4289d1e");
    }
}
