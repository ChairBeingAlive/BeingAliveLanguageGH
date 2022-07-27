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
using BeingAliveLanguage;
using GH_IO.Serialization;

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
          : base("Root_SoilMap", "balSoilMap",
              "Build the soil map for root drawing.",
              "BAL", "02::root")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Plane", "P", "Base plan where the soil map exists.", GH_ParamAccess.item, Rhino.Geometry.Plane.WorldXY);
            pManager.AddCurveParameter("Soil Polygon", "soilPoly", "Soil polygons that representing the soil. " +
                "For sectional soil, this should be triangles; for planar soil, this can be any tessellation.", GH_ParamAccess.list);

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
            List<Curve> polyCrvs = new List<Curve>();
            DA.GetData(0, ref pln);
            if (!DA.GetDataList(1, polyCrvs))
            { return; }

            SoilMap sMap = new SoilMap(pln, mapMode);

            var polyL = polyCrvs.Select(x => Utils.CvtCrvToPoly(x)).ToList();
            sMap.BuildMap(polyL);

            DA.SetData(0, sMap);
        }

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
            Menu_AppendItem(menu, "Sectional", (sender, e) => Menu.SelectMode(this, sender, e, ref mapMode, "sectional"), true, CheckMode("sectional"));
            Menu_AppendItem(menu, "Planar", (sender, e) => Menu.SelectMode(this, sender, e, ref mapMode, "planar"), true, CheckMode("planar"));
        }

        private bool CheckMode(string _modeCheck) => mapMode == _modeCheck;

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("B17755A9-2101-49D3-8535-EC8F93A8BA01");

        private string mapMode = "sectional";
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
            Menu_AppendItem(menu, "Single Form", (sender, e) => Menu.SelectMode(this, sender, e, ref formMode, "s"), true, CheckMode("s"));
            Menu_AppendItem(menu, "Multi  Form", (sender, e) => Menu.SelectMode(this, sender, e, ref formMode, "m"), true, CheckMode("m"));
        }

        private bool CheckMode(string _modeCheck) => formMode == _modeCheck;

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.LoadDll();

            var sMap = new SoilMap();
            var anchor = new Point3d();
            double radius = 10.0;

            if (!DA.GetData(0, ref sMap) || sMap.mapMode != "sectional")
            { return; }
            if (!DA.GetData(1, ref anchor))
            { return; }
            if (!DA.GetData(2, ref radius))
            { return; }


            var root = new RootSec(sMap, anchor, formMode);
            root.GrowRoot(radius);

            DA.SetDataList(0, root.crv);

        }

        string formMode = "m";  // s-single, m-multi
        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("A0E63559-41E8-4353-B78E-510E3FCEB577");
    }

    /// <summary>
    /// Draw the root map in planar soil grid.
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

            pManager.AddNumberParameter("Scale", "S", "Root scaling.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Phase", "P", "Current root phase.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Division Num", "divN", "The number of initial root branching.", GH_ParamAccess.item);

            // 5-8
            pManager.AddCurveParameter("Env Attractor", "envAtt", "Environmental attracting area (water, resource, etc.).", GH_ParamAccess.list);
            pManager.AddCurveParameter("Env Repeller", "envRep", "Environmental repelling area (dryness, poison, etc.).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Env DetectionRange", "envRange", "The range (to unit length of the grid) that a root can detect surrounding environment.", GH_ParamAccess.item, 5);
            pManager.AddBooleanParameter("ToggleEnvAffector", "envToggle", "Toggle the affects caused by environmental factors.", GH_ParamAccess.item, false);

            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("RootPlanar", "rootAll", "The planar root drawing, collection of all level branches.", GH_ParamAccess.list);

            pManager.AddGenericParameter("RootPlanarLevel-1", "rootLv1", "Level 1 root components.", GH_ParamAccess.list);
            pManager.AddGenericParameter("RootPlanarLevel-2", "rootLv2", "Level 2 root components.", GH_ParamAccess.list);
            pManager.AddGenericParameter("RootPlanarLevel-3", "rootLv3", "Level 3 root components.", GH_ParamAccess.list);
            pManager.AddGenericParameter("RootPlanarLevel-4", "rootLv4", "Level 4 root components.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {

            var sMap = new SoilMap();
            DA.GetData(0, ref sMap);

            if (!DA.GetData(0, ref sMap) || sMap.mapMode != "planar")
            { return; }

            var anchor = new Point3d();
            if (!DA.GetData(1, ref anchor))
            { return; }

            double scale = 0;
            if (!DA.GetData(2, ref scale))
            { return; }

            int phase = 0;
            if (!DA.GetData(3, ref phase))
            { return; }

            int divN = 1;
            if (!DA.GetData(4, ref divN))
            { return; }

            // optional param
            List<Curve> envAtt = new List<Curve>();
            List<Curve> envRep = new List<Curve>();
            double envRange = 5;
            bool envToggle = false;
            DA.GetDataList(5, envAtt);
            DA.GetDataList(6, envRep);
            DA.GetData(7, ref envRange);
            DA.GetData(8, ref envToggle);

            if (envToggle)
            {
                foreach (var crv in envAtt)
                {
                    if (!crv.IsClosed)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Attractors contain non-closed curve.");
                        return;
                    }

                }

                foreach (var crv in envRep)
                {
                    if (!crv.IsClosed)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Repellers contain non-closed curve.");
                        return;
                    }

                }
            }

            var root = new RootPlanar(sMap, anchor, scale, phase, divN, envAtt, envRep, envRange, envToggle);
            var rtRes = root.GrowRoot();

            DA.SetDataList(1, rtRes[0]);
            DA.SetDataList(2, rtRes[1]);
            DA.SetDataList(3, rtRes[2]);
            DA.SetDataList(4, rtRes[3]);

            var all = rtRes.Aggregate(new List<Line>(), (x, y) => x.Concat(y).ToList());

        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("8F8C6D2B-22F2-4511-A7C0-AA8CF2FDA42C");
    }

}