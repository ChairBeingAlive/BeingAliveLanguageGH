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
using BALcontract;



namespace BALloader
{
    // derived class that include MEF functionality
    public class GH_BAL : GH_Component
    {
        protected GH_BAL(string name, string nickname, string description, string category, string subCategory)
            : base(name, nickname, description, category, subCategory)
        {
        }

        protected CompositionContainer _container;
        public void LoadDll()
        {

            var info = Instances.ComponentServer.FindAssemblyByObject(ComponentGuid);
            string dllFile = info.Location.Replace(info.Name + ".gha", "BALcore.dll"); // hard coded

            if (!System.IO.File.Exists(dllFile))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, String.Format("The core computation lib {0} does not exist.", dllFile));
            }

            // MEF
            try
            {
                // An aggregate catalog that combines multiple catalogs.
                var catalog = new AggregateCatalog();
                catalog.Catalogs.Add(new AssemblyCatalog(Assembly.Load(System.IO.File.ReadAllBytes(dllFile))));

                // Create the CompositionContainer with the parts in the catalog.
                _container = new CompositionContainer(catalog);
                _container.ComposeParts(this);

            }
            catch (CompositionException compositionException)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, compositionException.ToString());
                return;
            }
        }

        public override Guid ComponentGuid => throw new NotImplementedException();

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            throw new NotImplementedException();
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            throw new NotImplementedException();
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            throw new NotImplementedException();
        }
    }

    public class BALsoilBase : GH_BAL
    {
        // import func collection from MEF.
        [Import(typeof(IPlugin))]
        public IPlugin mFunc;

        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public BALsoilBase()
          : base("BAL Soil Base", "soilBase",
            "Generate a base map from the boundary rectangle.",
            "BAL", "01::soil")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddRectangleParameter("Boundary", "B", "Boundary rectangle.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Resolution", "res", "Vertical resolution of the generated grid.", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("triGrid", "T", "The generated triangle map grid.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("unit Length", "uL", "The triangle's side length", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.LoadDll();

            Rectangle3d rec = new Rectangle3d();
            int rsl = 1;

            if (!DA.GetData(0, ref rec)) { return; }
            if (!DA.GetData(1, ref rsl)) { return; }

            // call the actural function
            var (uL, res) = mFunc.MakeTriMap(ref rec, rsl);

            GH_Structure<GH_Curve> triArray = new GH_Structure<GH_Curve>();
            //DataTree<PolylineCurve> triArray = new DataTree<PolylineCurve>();
            for (int i = 0; i < res.Count; i++)
            {
                var path = new GH_Path(i);
                //triArray.AddRange(res[i].Select(x=>new GH_Curve(x)), path);
                triArray.AppendRange(res[i].Select(x => new GH_Curve(x)), path);
            }
            DA.SetDataTree(0, triArray);
            DA.SetData(1, uL);
        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => null;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("140A327A-B36E-4D39-86C5-317D7C24E7FE");
    }

    public class BALbaseDiv : GH_BAL
    {
        // import func collection from MEF.
        [Import(typeof(IPlugin))]
        public IPlugin mFunc;

        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public BALbaseDiv()
          : base("BAL Soil Content", "soilContent",
            "Generate soil map based on the ratio of 3 contents, and add rocks if provided.",
            "BAL", "01::soil")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("soil Base", "T", "soil base triangle map.", GH_ParamAccess.list);
            pManager.AddNumberParameter("sand ratio", "rSand", "The ratio of sand in the soil.", GH_ParamAccess.item);
            pManager.AddNumberParameter("silt ratio", "rSilt", "The ratio of silt in the soil.", GH_ParamAccess.item);
            pManager.AddNumberParameter("clay ratio", "rClay", "The ratio of clay in the soil.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Rocks", "R", "Curves represendting the rocks in the soil.", GH_ParamAccess.list);

            pManager[0].DataMapping = GH_DataMapping.Flatten; // flatten the triangle list by default
            pManager[4].DataMapping = GH_DataMapping.Flatten; // flatten the triangle list by default
            pManager[4].Optional = true; // rock can be optionally provided
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Info", "soilInfo", "Info about the current soil based on given content ratio.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Sand Tri", "sandT", "Sand triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Silt Tri", "siltT", "Silt triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Clay Tri", "clayT", "Clay triangles.", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.LoadDll();

            // get data
            List<Curve> triL = new List<Curve>();
            double rSand = 0;
            double rSilt = 0;
            double rClay = 0;
            List<Curve> rock = new List<Curve>();
            if (!DA.GetDataList(0, triL)) { return; }
            if (!DA.GetData(1, ref rSand)) { return; }
            if (!DA.GetData(2, ref rSilt)) { return; }
            if (!DA.GetData(3, ref rClay)) { return; }
            DA.GetDataList(4, rock);

            List<Polyline> triPoly = triL.Select(x => Utils.CvtCrvToTriangle(x)).ToList();
            double[] ratio = new double[3] { rSand, rSilt, rClay };

            // call the actural function
            var (sandT, siltT, clayT, soilInfo) = mFunc.DivBaseMap(in triPoly, in ratio, in rock);

            DA.SetData(0, soilInfo);
            DA.SetDataList(1, sandT);
            DA.SetDataList(2, siltT);
            DA.SetDataList(3, clayT);

            // for debugging info
            //if (!String.IsNullOrEmpty(msg))
            //{
            //    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, msg);
            //}

        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => null;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("53411C7C-0833-49C8-AE71-B1948D2DCC6C");
    }

    public class BALsoilInfo : GH_BAL
    {
        // import func collection from MEF.
        [Import(typeof(IPlugin))]
        public IPlugin mFunc;

        public BALsoilInfo() :
            base("BAL Soil Information", "soilInfoText",
                "Export the soil information in text format.",
                "BAL", "01::soil")
        { }

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
            this.LoadDll();

            // get data
            soilProperty soilInfo = new soilProperty();
            List<Curve> triCrv = new List<Curve>();

            if (!DA.GetData(0, ref soilInfo)) { return; }


            var sText = mFunc.SoilText(soilInfo);

            // assign output
            DA.SetData(0, sText);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("af64a14a-6795-469c-b044-7db972d5bd84");

    }

    public class BALsoilWaterOffset : GH_BAL
    {
        // import func collection from MEF.
        [Import(typeof(IPlugin))]
        public IPlugin mFunc;

        public BALsoilWaterOffset()
          : base("BAL Soil Water Visualization", "soilWaterVis",
            "Generate soil diagram with water info.",
            "BAL", "01::soil")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Info", "soilInfo", "Info about the current soil based on given content ratio.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Soil Tri", "soilT", "Soil triangles, can be any or combined triangles of sand, silt, clay.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Current Water ratio", "rCurWater", "The current water ratio[0, 1] in the soil for visualization purposes.", GH_ParamAccess.item, 0.5);
            pManager[2].Optional = true;

        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Soil Core", "soilCore", "Soil core triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Wilting Point", "soilWP", "Soil wilting point triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Field Capacity", "soilFC", "Soil field capacity triangles.", GH_ParamAccess.list);

            pManager.AddCurveParameter("Embedded Water Hatch", "waterEmbed", "The embedded water of the soil.", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Current Water Hatch", "waterCurrent", "The current water stage in the soil.", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.LoadDll();

            // get data
            soilProperty soilInfo = new soilProperty();
            List<Curve> triCrv = new List<Curve>();
            double rWater = 0.5;

            if (!DA.GetData(0, ref soilInfo)) { return; }
            if (!DA.GetDataList(1, triCrv)) { return; }
            if (!DA.GetData(2, ref rWater)) { return; }


            // compute offseted curves 
            var (triCore, triWP, triFC, embedWater, curWater) = mFunc.OffsetWater(triCrv, soilInfo, rWater);


            // assign output
            DA.SetDataList(0, triCore);
            DA.SetDataList(1, triWP);
            DA.SetDataList(2, triFC);


            GH_Structure<GH_Curve> eWTree = new GH_Structure<GH_Curve>();
            GH_Structure<GH_Curve> cWTree = new GH_Structure<GH_Curve>();

            for (int i = 0; i < embedWater.Count; i++)
            {
                var path = new GH_Path(i);
                eWTree.AppendRange(embedWater[i].Select(x => new GH_Curve(x)), path);
            }

            for (int i = 0; i < curWater.Count; i++)
            {
                var path = new GH_Path(i);
                cWTree.AppendRange(curWater[i].Select(x => new GH_Curve(x)), path);
            }

            DA.SetDataTree(0, eWTree);
            DA.SetDataTree(1, cWTree);

        }

        // define the MEF container
        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("F6D8797A-674F-442B-B1BF-606D18B5277A");
    }

    public class BALsoilWaterHatch : GH_BAL
    {
        // import func collection from MEF.
        [Import(typeof(IPlugin))]
        public IPlugin mFunc;

        // constructor
        public BALsoilWaterHatch()
          : base("BAL Soil Water Hatch", "soilWaterHatch",
            "Generate hatch for the water info in the soil diagram.",
            "BAL", "01::soil")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Soil Core", "soilCore", "Soil core triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Wilting Point", "soilWP", "Soil wilting point triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Field Capacity", "soilFC", "Soil field capacity triangles.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Current Water ratio", "rCurWater", "The current water ratio[0, 1] in the soil for visualization purposes.", GH_ParamAccess.item);
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Embedded Water", "embedWater", "The embedded water of the soil.", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Current Water Stage", "curWater", "The current water stage in the soil.", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.LoadDll();

            // get data
            List<Curve> soilCore = new List<Curve>();
            List<Curve> soilWP = new List<Curve>();
            List<Curve> soilFC = new List<Curve>();
            double rWater = 0.5;

            if (!DA.GetDataList(0, soilCore)) { return; }
            if (!DA.GetDataList(1, soilWP)) { return; }
            if (!DA.GetDataList(2, soilFC)) { return; }
            if (!DA.GetData(3, ref rWater)) { return; }

            // compute offseted curves 
            var (embedWater, curWater) = mFunc.HatchWater(soilCore, soilWP, soilFC, rWater);

            GH_Structure<GH_Curve> eWTree = new GH_Structure<GH_Curve>();
            GH_Structure<GH_Curve> cWTree = new GH_Structure<GH_Curve>();

            for (int i = 0; i < embedWater.Count; i++)
            {
                var path = new GH_Path(i);
                eWTree.AppendRange(embedWater[i].Select(x => new GH_Curve(x)), path);
            }

            for (int i = 0; i < curWater.Count; i++)
            {
                var path = new GH_Path(i);
                cWTree.AppendRange(curWater[i].Select(x => new GH_Curve(x)), path);
            }

            DA.SetDataTree(0, eWTree);
            DA.SetDataTree(1, cWTree);

        }

        // define the MEF container
        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("af7ce1c6-d275-4961-8290-14f37814f44c");
    }
}
