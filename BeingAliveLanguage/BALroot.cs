using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Windows.Forms;
using BALcontract;

namespace BeingAliveLanguage
{
    public class BALRootSoilMapSec : GH_BAL
    {
        // import func collection from MEF.
        [Import(typeof(IPlugin))]
        public IPlugin mFunc;

        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public BALRootSoilMapSec()
          : base("RootSoilMap_Sectional", "balSoilMap_S",
              "Build the sectional soil map for root drawing.",
              "BAL", "02::root")
        {
        }
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Plane", "P", "Base plan where the soil map exists.", GH_ParamAccess.item, Rhino.Geometry.Plane.WorldXY);
            pManager.AddCurveParameter("Soil Tri", "soilT", "Soil triangles of the soil map.", GH_ParamAccess.list);

            pManager[0].Optional = true;
            pManager[1].DataMapping = GH_DataMapping.Flatten; // flatten the triangle list by default
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SoilMap", "sMap", "The soil map class to build root upon.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.LoadDll();

            Plane pln = new Plane();
            List<Curve> triL = new List<Curve>();
            DA.GetData(0, ref pln);
            if (!DA.GetDataList(1, triL))
            { return; }

            SoilMap sMap = new SoilMap(pln);

            var triPoly = triL.Select(x => Utils.CvtCrvToTriangle(x)).ToList();
            sMap.BuildMap(triPoly);

            DA.SetData(0, sMap);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("B17755A9-2101-49D3-8535-EC8F93A8BA01");
    }

    /// <summary>
    /// Draw the root in sectional soil grid.
    /// </summary>
    public class BALRootSec : GH_BAL
    {
        // import func collection from MEF.
        [Import(typeof(IPlugin))]
        public IPlugin mFunc;

        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public BALRootSec()
          : base("Root_Sectional", "balRoot_S",
              "Draw root in sectional soil map.",
              "BAL", "02::root")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SoilMap", "sMap", "The soil map class to build root upon.", GH_ParamAccess.item);
            pManager.AddPointParameter("Anchor", "A", "Anchor locations of the root(s).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Radius", "R", "Root Radius.", GH_ParamAccess.item);
        }


        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("RootSectional", "rootS", "The sectional root drawing.", GH_ParamAccess.list);
        }

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
            Menu_AppendItem(menu, "Single Form", (sender, e) => singleForm = !singleForm, true, singleForm);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.LoadDll();

            var sMap = new SoilMap();
            var anchor = new Point3d();
            double radius = 10.0;
            DA.GetData(0, ref sMap);
            if (!DA.GetData(1, ref anchor))
            { return; }
            if (!DA.GetData(2, ref radius))
            { return; }



            var res = new List<Line>();
            DA.SetData(0, res);
        }

        bool singleForm = false;
        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("A0E63559-41E8-4353-B78E-510E3FCEB577");
    }

    /// <summary>
    /// Draw the root map in planar soil grid
    /// </summary>
    public class BALRootPlanar : GH_BAL
    {
        // import func collection from MEF.
        [Import(typeof(IPlugin))]
        public IPlugin mFunc;

        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public BALRootPlanar()
          : base("Root_Planar", "balRoot_P",
              "Draw root in planar soil map.",
              "BAL", "02::root")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
        }


        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("RootPlanar", "rootP", "The planar root drawing.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("8F8C6D2B-22F2-4511-A7C0-AA8CF2FDA42C");
    }

}