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
    public class BALsoilBase : GH_Component
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
            "BAL", "01::base")
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
            Rectangle3d rec = new Rectangle3d();
            int rsl = 1;

            if (!DA.GetData(0, ref rec)) { return; }
            if (!DA.GetData(1, ref rsl)) { return; }

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
                //catalog.Catalogs.Add(new AssemblyCatalog(typeof(IPlugin).Assembly));

                // Create the CompositionContainer with the parts in the catalog.
                _container = new CompositionContainer(catalog);
                _container.ComposeParts(this);

            }
            catch (CompositionException compositionException)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, compositionException.ToString());
                return;
            }

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
        // define the MEF container
        private CompositionContainer _container;


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

    public class BALmapDiv3 : GH_Component
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
        public BALmapDiv3()
          : base("BAL Soil Content", "soilContent",
            "Generate soil map based on the ratio of 3 contents, and add rocks if provided.",
            "BAL", "01::base")
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
            pManager.AddCurveParameter("sand Tri", "sandT", "Sand triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("silt Tri", "siltT", "Silt triangles.", GH_ParamAccess.list);
            pManager.AddCurveParameter("clay Tri", "clayT", "Clay triangles.", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
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
                //catalog.Catalogs.Add(new AssemblyCatalog(typeof(IPlugin).Assembly));

                // Create the CompositionContainer with the parts in the catalog.
                _container = new CompositionContainer(catalog);
                _container.ComposeParts(this);

            }
            catch (CompositionException compositionException)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, compositionException.ToString());
                return;
            }

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

            List<Polyline> triPoly = new List<Polyline>();
            foreach (var t in triL)
            {
                Polyline tmp = new Polyline();
                if (t.TryGetPolyline(out tmp) && tmp.IsClosed)
                    triPoly.Add(tmp);
            }

            double[] ratio = new double[3] { rSand, rSilt, rClay };

            // call the actural function
            var (sandT, siltT, clayT, msg) = mFunc.divBaseMap(in triPoly, in ratio, in rock); ;

            DA.SetDataList(0, sandT);
            DA.SetDataList(1, siltT);
            DA.SetDataList(2, clayT);

            if (!String.IsNullOrEmpty(msg))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, msg);
            }

        }
        // define the MEF container
        private CompositionContainer _container;


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
}