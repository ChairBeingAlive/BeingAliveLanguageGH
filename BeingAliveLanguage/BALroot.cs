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
    public class BALRootSoilMap : GH_BAL
    {
        // import func collection from MEF.
        [Import(typeof(IPlugin))]
        public IPlugin mFunc;

        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public BALRootSoilMap()
          : base("Root_SoilMap (tri-Based)", "balSoilMap_S",
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

        public override GH_Exposure Exposure => GH_Exposure.secondary;

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

            var rType = singleForm ? 0 : 1;
            var root = new RootSec(sMap, anchor, rType);

            root.GrowRoot(radius);
            //var res = root.crv;

            DA.SetDataList(0, root.crv);
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

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SoilMap", "sMap", "The soil map class to build root upon.", GH_ParamAccess.item);
            pManager.AddPointParameter("Anchor", "A", "Anchor locations of the root(s).", GH_ParamAccess.item);

            pManager.AddNumberParameter("Scale", "scale", "Root scaling.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Phase", "phase", "Current root phase.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Division Num", "divN", "The number of initial root branching.", GH_ParamAccess.item);

            // 5-6
            pManager.AddCurveParameter("Env Attractor", "envAtt", "Environmental attracting area (water, resource, etc.).", GH_ParamAccess.list);
            pManager.AddCurveParameter("Env Repeller", "envRep", "Environmental repelling area (dryness, poison, etc.).", GH_ParamAccess.list);
            pManager.AddBooleanParameter("ToggleEnvAffector", "envToggle", "Toggle the affects caused by environmental factors.", GH_ParamAccess.item, false);

            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
        }


        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("RootPlanar", "rootAll", "The planar root drawing.", GH_ParamAccess.list);

            pManager.AddGenericParameter("RootPlanarLevel-1", "rootLv1", "Level 1 root components.", GH_ParamAccess.list);
            pManager.AddGenericParameter("RootPlanarLevel-2", "rootLv2", "Level 2 root components.", GH_ParamAccess.list);
            pManager.AddGenericParameter("RootPlanarLevel-3", "rootLv3", "Level 3 root components.", GH_ParamAccess.list);
            pManager.AddGenericParameter("RootPlanarLevel-4", "rootLv4", "Level 4 root components.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("8F8C6D2B-22F2-4511-A7C0-AA8CF2FDA42C");
    }

}