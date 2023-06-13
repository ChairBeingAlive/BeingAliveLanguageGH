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
using BeingAliveLanguage;
using GH_IO.Serialization;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net.Mail;
using Rhino.UI.ObjectProperties;

namespace BeingAliveLanguage
{
    public class BALRootSoilMap : GH_Component
    {

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
        protected override System.Drawing.Bitmap Icon => Properties.Resources.balRootMap;
        public override Guid ComponentGuid => new Guid("B17755A9-2101-49D3-8535-EC8F93A8BA01");

        public string mapMode = "sectional";

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Plane", "P", "Base plane where the soil map exists." +
                "For soil that is not aligned with the world coordinates, please use the soil boundary.",
                GH_ParamAccess.item, Rhino.Geometry.Plane.WorldXY);
            pManager.AddGenericParameter("Soil Geometry", "soilGeo", "Soil geometry that representing the soil. " +
                "For sectional soil, this should be triangle grids;" +
                "for planar soil, this can be any tessellation or just a point collection.", GH_ParamAccess.list);

            pManager[0].Optional = true;
            pManager[1].DataMapping = GH_DataMapping.Flatten; // flatten the triangle list by default
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SoilMap", "sMap", "The soil map class to build root upon.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var pln = new Plane();
            DA.GetData(0, ref pln);


            var inputGeo = new List<IGH_Goo>();
            if (!DA.GetDataList(1, inputGeo) || inputGeo.Count == 0)
            { return; }

            var conPt = new ConcurrentBag<Point3d>();
            var conPoly = new ConcurrentBag<Polyline>();
            SoilMap sMap = new SoilMap(pln, mapMode);

            // TODO: find better ways to convert IGH_GOO to polyline
            if (inputGeo[0].CastTo<Point3d>(out Point3d pt))
            {
                Parallel.ForEach(inputGeo, goo =>
                {
                    goo.CastTo<Point3d>(out Point3d p);
                    conPt.Add(p);
                });

                sMap.BuildMap(conPt);
            }
            else if (inputGeo[0].CastTo<Polyline>(out Polyline pl))
            {
                Parallel.ForEach(inputGeo, goo =>
                {
                    goo.CastTo<Polyline>(out Polyline p);
                    conPoly.Add(p);
                });

                sMap.BuildMap(conPoly);
            }
            else if (inputGeo[0].CastTo<Curve>(out Curve crv))
            {
                Parallel.ForEach(inputGeo, goo =>
                {
                    goo.CastTo<Curve>(out Curve c);
                    if (c.TryGetPolyline(out Polyline ply))
                    {
                        conPoly.Add(ply);
                    }
                });
                sMap.BuildMap(conPoly);
            }
            else if (inputGeo[0].CastTo<Rectangle3d>(out Rectangle3d rec))
            {
                Parallel.ForEach(inputGeo, goo =>
                {
                    goo.CastTo<Rectangle3d>(out Rectangle3d c);
                    conPoly.Add(c.ToPolyline());
                });
                sMap.BuildMap(conPoly);
            }

            DA.SetData(0, sMap);

        }

        protected override void BeforeSolveInstance()
        {
            Message = mapMode.ToUpper();
        }

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);

            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "Map Mode:", (sender, e) => { }, false).Font = GH_FontServer.StandardItalic;
            Menu_AppendItem(menu, " Sectional", (sender, e) => Menu.SelectMode(this, sender, e, ref mapMode, "sectional"), true, CheckMode("sectional"));
            Menu_AppendItem(menu, " Planar", (sender, e) => Menu.SelectMode(this, sender, e, ref mapMode, "planar"), true, CheckMode("planar"));
        }

        private bool CheckMode(string _modeCheck) => mapMode == _modeCheck;

        public override bool Write(GH_IWriter writer)
        {
            if (mapMode != "")
                writer.SetString("mapMode", mapMode);
            return base.Write(writer);
        }
        public override bool Read(GH_IReader reader)
        {
            if (reader.ItemExists("mapMode"))
                mapMode = reader.GetString("mapMode");

            Message = reader.GetString("mapMode").ToUpper();

            return base.Read(reader);
        }
    }

    /// <summary>
    /// Draw the root in sectional soil grid.
    /// </summary>
    public class BALRootSec : GH_Component
    {
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

            pManager.AddCurveParameter("Env Attractor", "envA", "Environmental attracting area (water, resource, etc.).", GH_ParamAccess.list);
            pManager.AddCurveParameter("Env Repeller", "envR", "Environmental repelling area (dryness, poison, etc.).", GH_ParamAccess.list);
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
            var sMap = new SoilMap();
            var anchor = new Point3d();
            double radius = 10.0;

            if (!DA.GetData(0, ref sMap) || sMap.mapMode != "sectional")
            { return; }
            if (!DA.GetData(1, ref anchor))
            { return; }
            if (!DA.GetData(2, ref radius))
            { return; }


            // optional param
            List<Curve> envAtt = new List<Curve>();
            List<Curve> envRep = new List<Curve>();
            double envRange = 5;
            bool envToggle = false;
            DA.GetDataList("Env Attractor", envAtt);
            DA.GetDataList("Env Repeller", envRep);
            DA.GetData("Env DetectionRange", ref envRange);
            DA.GetData("EnvAffector Toggle", ref envToggle);

            if (envToggle)
            {
                foreach (var crv in envAtt)
                    if (!crv.IsClosed)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Attractors contain non-closed curve.");
                        return;
                    }

                foreach (var crv in envRep)
                    if (!crv.IsClosed)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Repellers contain non-closed curve.");
                        return;
                    }
            }


            var root = new RootSec(sMap, anchor, formMode);
            root.GrowRoot(radius);

            DA.SetDataList(0, root.crv);
        }

        string formMode = "multi";  // s-single, m-multi
        protected override System.Drawing.Bitmap Icon => Properties.Resources.balRootSectional;
        public override Guid ComponentGuid => new Guid("E2D1F590-4BE8-4AAD-812E-4BF682F786A4");
    }

    /// <summary>
    /// Draw the planar roots.
    /// </summary>
    public class BALRootPlanar : GH_Component
    {
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

        protected override System.Drawing.Bitmap Icon => Properties.Resources.balRootPlanar;
        public override Guid ComponentGuid => new Guid("8F8C6D2B-22F2-4511-A7C0-AA8CF2FDA42C");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SoilMap", "sMap", "The soil map class to build root upon.", GH_ParamAccess.item);
            pManager.AddPointParameter("Anchor", "A", "Anchor locations of the root(s).", GH_ParamAccess.item);

            pManager.AddNumberParameter("Scale", "S", "Root scaling.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Phase", "P", "Current root phase.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Division Num", "divN", "The number of initial root branching.", GH_ParamAccess.item);

            // 5-8
            pManager.AddCurveParameter("Env Attractor", "envA", "Environmental attracting area (water, resource, etc.).", GH_ParamAccess.list);
            pManager.AddCurveParameter("Env Repeller", "envR", "Environmental repelling area (dryness, poison, etc.).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Env DetectionRange", "envD", "The range (to unit length of the grid) that a root can detect surrounding environment.", GH_ParamAccess.item, 5);
            pManager.AddBooleanParameter("EnvAffector Toggle", "envToggle", "Toggle the affects caused by environmental factors.", GH_ParamAccess.item, false);

            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("RootPlanar", "rootAll", "The planar root drawing, collection of all level branches.", GH_ParamAccess.list);

            pManager.AddLineParameter("RootPlanarLevel-1", "rootLv1", "Level 1 root components.", GH_ParamAccess.list);
            pManager.AddLineParameter("RootPlanarLevel-2", "rootLv2", "Level 2 root components.", GH_ParamAccess.list);
            pManager.AddLineParameter("RootPlanarLevel-3", "rootLv3", "Level 3 root components.", GH_ParamAccess.list);
            pManager.AddLineParameter("RootPlanarLevel-4", "rootLv4", "Level 4 root components.", GH_ParamAccess.list);
            pManager.AddLineParameter("RootPlanarLevel-5", "rootLv5", "Level 5 root components.", GH_ParamAccess.list);

            pManager.AddLineParameter("RootPlanarAbsorb", "rootAbsorb", "Absorbant roots.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {

            var sMap = new SoilMap();
            DA.GetData(0, ref sMap);

            if (!DA.GetData(0, ref sMap))
            { return; }
            if (sMap.mapMode != "planar")
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Soil map type is not 'planar'.");
                return;
            }

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
                    if (!crv.IsClosed)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Attractors contain non-closed curve.");
                        return;
                    }

                foreach (var crv in envRep)
                    if (!crv.IsClosed)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Repellers contain non-closed curve.");
                        return;
                    }
            }

            var root = new RootPlanar(sMap, anchor, scale, phase, divN, envAtt, envRep, envRange, envToggle);
            var (rtRes, rtAbs) = root.GrowRoot();

            var allRt = rtRes.Aggregate(new List<Line>(), (x, y) => x.Concat(y).ToList());

            DA.SetDataList(0, allRt);
            DA.SetDataList(1, rtRes[0]);
            DA.SetDataList(2, rtRes[1]);
            DA.SetDataList(3, rtRes[2]);
            DA.SetDataList(4, rtRes[3]);
            DA.SetDataList(5, rtRes[4]);
            DA.SetDataList(6, rtAbs);
        }
    }

    /// <summary>
    /// Draw tree roots 
    /// </summary>
    public class BALtreeRoot : GH_Component
    {
        public BALtreeRoot()
        : base("TreeRoot", "balTreeRoot",
              "Generate the BAL tree-root drawing using the BAL tree and soil information.",
              "BAL", "02::root")
        { }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => Properties.Resources.balTree; //todo: update img
        public override Guid ComponentGuid => new Guid("27C279E0-08C9-4110-AE40-81A59C9D9EB8");
        private bool rootDense = false;
        private int scalingFactor = 1;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("TreeInfo", "tInfo", "Information about the tree.", GH_ParamAccess.item);
            pManager.AddGenericParameter("SoilMap", "sMap", "The soil map class to build root upon.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("All Roots", "rootAll", "The planar root drawing, collection of all level roots.", GH_ParamAccess.list);

            pManager.AddLineParameter("RootMain", "rootM", "Primary roots.", GH_ParamAccess.list);
            pManager.AddLineParameter("RootNew", "rootN", "Secondary roots.", GH_ParamAccess.list);
            pManager.AddLineParameter("Dead Roots", "rootDead", "Dead roots in later phases of a tree's life.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            //! Get data
            var tInfo = new TreeProperty();
            var sMap = new SoilMap();

            if (!DA.GetData<TreeProperty>("TreeInfo", ref tInfo))
            { return; }

            if (!DA.GetData<SoilMap>("SoilMap", ref sMap))
            { return; }

            if (sMap.mapMode != "sectional")
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A tree root need a sectional soil map to grow upon.");
            }

            if (sMap.pln != tInfo.pln)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Tree plane and SoilMap plane does not match. This may cause issues for the root drawing.");
            }

            // ! get anchor + determin root size based on tree size
            var anchorPt = sMap.GetNearestPoint(tInfo.pln.Origin);
            if (anchorPt.DistanceTo(tInfo.pln.Origin) > sMap.unitLen)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Tree anchor point is too far from the soil. Please plant your tree near the soil grid.");
            }

            // if the unit length of the soil grid is small enough, we allow the drawing of detailed root.
            if (sMap.unitLen < tInfo.height * 0.05)
            {
                rootDense = true;
                scalingFactor = 2;
            }
            else
            {
                rootDense = false;
                scalingFactor = 1;

                // send warning of the soil grid is too big
                if (sMap.unitLen > tInfo.height * 0.15)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Soil grid too big. You will get unbalanced relationship between the tree and its roots.");
                }
            }


            // ! get parameter of the map and start drawing based on the phase
            var uL = sMap.unitLen; // unit length, side length of the triangle
            var vL = uL * Math.Sqrt(3) * 0.5; // vertical unit length, height of the triangle
            var mainRoot = new List<Line>();
            var newRoot = new List<Line>();
            var hairRoot = new List<Line>();
            var deadRoot = new List<Line>();

            if (tInfo.phase < 1 || tInfo.phase > 12)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Tree phase is out of range [0, 12].");

            var vVec = -sMap.pln.YAxis * vL * scalingFactor;
            var hVec = sMap.pln.XAxis * uL * scalingFactor;

            int vSideParam = 3;

            /////////////////////////////////////////////
            /// As the root is largely manually defined in different phases,
            /// a majority of the diagram here is hard-coded and not fully rule-based
            ///  - the hairy roots are even more hard-coded
            ////////////////////////////////////////////

            //! Main Root
            // vertical tap root (central layer)
            // --------------------
            //          *
            //          *
            int verticalTapRootParam = 0;
            if (tInfo.phase == 1)
            {
                verticalTapRootParam = 3;
                newRoot.Add(new Line(anchorPt, vVec * verticalTapRootParam));
            }
            else if (tInfo.phase > 1 && tInfo.phase <= 8)
            {
                verticalTapRootParam = 4;

                if (tInfo.phase == 2) // at phase 2, tap root grows longer
                {
                    var preVerticalParam = verticalTapRootParam - 1;
                    mainRoot.Add(new Line(anchorPt, vVec * preVerticalParam));
                    newRoot.Add(new Line(anchorPt + vVec * preVerticalParam, vVec));
                }
                else
                {
                    mainRoot.Add(new Line(anchorPt, vVec * verticalTapRootParam));
                }
            }
            else if (tInfo.phase > 8 && tInfo.phase <= 11)
            {
                verticalTapRootParam = 2;

                if (tInfo.phase == 9)
                {
                    var tmpPt = anchorPt + vVec * verticalTapRootParam;
                    deadRoot.Add(new Line(tmpPt, vVec * verticalTapRootParam));
                }
                mainRoot.Add(new Line(anchorPt, vVec * verticalTapRootParam));
            }

            if (tInfo.phase == 12)
            {
                deadRoot.Add(new Line(anchorPt, vVec * 2));
            }

            //! vertical root (1nd layer)
            // ---------------------
            //        *  |  *
            //        *  |  *
            int vTmpParam = 0;
            var lAnchor = anchorPt - hVec * 4;
            var rAnchor = anchorPt + hVec * 4;
            if (tInfo.phase == 5)
            {
                vTmpParam = 2;
                newRoot.Add(new Line(lAnchor, vVec * vTmpParam));
                newRoot.Add(new Line(rAnchor, vVec * vTmpParam));
            }
            else if (tInfo.phase > 5 && tInfo.phase <= 9)
            {
                vTmpParam = vSideParam;
                if (tInfo.phase == 6)
                {
                    var preVerticalParam = vTmpParam - 1;
                    mainRoot.Add(new Line(lAnchor, vVec * preVerticalParam));
                    mainRoot.Add(new Line(rAnchor, vVec * preVerticalParam));
                    newRoot.Add(new Line(lAnchor + vVec * preVerticalParam, vVec));
                    newRoot.Add(new Line(rAnchor + vVec * preVerticalParam, vVec));
                }
                else
                {
                    mainRoot.Add(new Line(lAnchor, vVec * vTmpParam));
                    mainRoot.Add(new Line(rAnchor, vVec * vTmpParam));
                }
            }
            else if (tInfo.phase == 10)
            {
                // phase 10, dead root shown
                deadRoot.Add(new Line(lAnchor, vVec * vSideParam));
                deadRoot.Add(new Line(rAnchor, vVec * vSideParam));
            }


            // ! vertical root (2rd layer)
            // ---------------------
            //     *  |  |  |  *
            //     *  |  |  |  *
            lAnchor = anchorPt - hVec * 7;
            rAnchor = anchorPt + hVec * 7;
            vTmpParam = 0;
            if (tInfo.phase == 6)
            {
                vTmpParam = 2;
                newRoot.Add(new Line(lAnchor, vVec * vTmpParam));
                newRoot.Add(new Line(rAnchor, vVec * vTmpParam));
            }
            else if (tInfo.phase > 6 && tInfo.phase <= 11)
            {
                vTmpParam = vSideParam;
                if (tInfo.phase == 7)
                {
                    var preVerticalParam = vTmpParam - 1;
                    mainRoot.Add(new Line(lAnchor, vVec * preVerticalParam));
                    mainRoot.Add(new Line(rAnchor, vVec * preVerticalParam));
                    newRoot.Add(new Line(lAnchor + vVec * preVerticalParam, vVec));
                    newRoot.Add(new Line(rAnchor + vVec * preVerticalParam, vVec));
                }
                else
                {
                    mainRoot.Add(new Line(lAnchor, vVec * vTmpParam));
                    mainRoot.Add(new Line(rAnchor, vVec * vTmpParam));
                }
            }
            else if (tInfo.phase == 12)
            {
                // phase 12, dead root shown
                deadRoot.Add(new Line(lAnchor, vVec * vSideParam));
                deadRoot.Add(new Line(rAnchor, vVec * vSideParam));
            }


            //! vertical root (3rd layer)
            // ---------------------
            //  *  |  |  |  |  |  *
            //  *  |  |  |  |  |  *
            lAnchor = anchorPt - hVec * 10;
            rAnchor = anchorPt + hVec * 10;
            vTmpParam = 0;
            if (tInfo.phase >= 7 && tInfo.phase <= 11)
            {
                vTmpParam = 2;
                if (tInfo.phase == 7)
                {
                    newRoot.Add(new Line(lAnchor, vVec * vTmpParam));
                    newRoot.Add(new Line(rAnchor, vVec * vTmpParam));
                }
                else
                {
                    mainRoot.Add(new Line(lAnchor, vVec * vTmpParam));
                    mainRoot.Add(new Line(rAnchor, vVec * vTmpParam));
                }
            }
            else if (tInfo.phase == 12)
            {
                // phase 12, dead root shown
                deadRoot.Add(new Line(lAnchor, vVec * vSideParam));
                deadRoot.Add(new Line(rAnchor, vVec * vSideParam));
            }

            //! vertical root (secondary 1st layer)
            // ---------------------
            //  |  |  |  |  |  |  |
            //     -------------
            //  |  |  | *|* |  |  |
            lAnchor = anchorPt - hVec * 2 + vVec * 2;
            rAnchor = anchorPt + hVec * 2 + vVec * 2;
            vTmpParam = 0;
            if (tInfo.phase >= 8 && tInfo.phase <= 11)
            {
                vTmpParam = 1;

                if (tInfo.phase == 8)
                {
                    newRoot.Add(new Line(lAnchor, vVec * vTmpParam));
                    newRoot.Add(new Line(rAnchor, vVec * vTmpParam));
                }
                if (tInfo.phase == 9)
                {
                    mainRoot.Add(new Line(lAnchor, vVec * vTmpParam));
                    mainRoot.Add(new Line(rAnchor, vVec * vTmpParam));
                }
                else if (tInfo.phase == 10)
                {
                    mainRoot.Add(new Line(lAnchor, vVec * vTmpParam));
                    deadRoot.Add(new Line(rAnchor, vVec * vTmpParam));
                }
                else if (tInfo.phase == 11)
                {
                    deadRoot.Add(new Line(lAnchor, vVec * vTmpParam));
                }
            }


            //! horizontal tap root (central)
            // ********************
            //          |
            //          |
            int totalHorizontalTapRootParam = 0;
            int prevHorTapRootParam = 0;
            int curHorTapRootParam = 0;
            if (tInfo.phase > 1 && tInfo.phase <= 8)
            {
                //totalHorizontalTapRootParam = (tInfo.phase - 1) * 2 + 1;
                curHorTapRootParam = 2;
                if (tInfo.phase == 1)
                { prevHorTapRootParam = 0; }
                else
                { prevHorTapRootParam = (tInfo.phase - 2) * 2 + 1; }

                newRoot.Add(new Line(anchorPt + hVec * prevHorTapRootParam, hVec * curHorTapRootParam));
                newRoot.Add(new Line(anchorPt - hVec * prevHorTapRootParam, -hVec * curHorTapRootParam));

                mainRoot.Add(new Line(anchorPt, hVec * prevHorTapRootParam));
                mainRoot.Add(new Line(anchorPt, -hVec * prevHorTapRootParam));
            }
            else
            {
                if (tInfo.phase > 8 && tInfo.phase <= 10)
                {
                    totalHorizontalTapRootParam = 15;
                }
                else if (tInfo.phase > 10)
                {
                    totalHorizontalTapRootParam = 10;

                    if (tInfo.phase == 11)
                    {
                        var lPt = anchorPt + hVec * totalHorizontalTapRootParam;
                        deadRoot.Add(new Line(lPt, hVec * 5));
                        var rPt = anchorPt - hVec * totalHorizontalTapRootParam;
                        deadRoot.Add(new Line(rPt, -hVec * 5));

                    }
                }
                mainRoot.Add(new Line(anchorPt, hVec * totalHorizontalTapRootParam));
                mainRoot.Add(new Line(anchorPt, -hVec * totalHorizontalTapRootParam));
            }

            //! horizontal tap root (2nd layer)
            // -------------------
            //         |
            //       *****
            //         |
            int lParam = 0;
            int rParam = 0;
            int preParam = 0;
            int curParam = 0;

            var startPtH2 = anchorPt - sMap.pln.YAxis * vL * 2;
            if (tInfo.phase >= 4 && tInfo.phase <= 6)
            {
                if (tInfo.phase == 4)
                {
                    curParam = 1;
                    preParam = 0;
                }
                else
                {
                    preParam = (tInfo.phase - 5) * 2 + 1;
                    curParam = 2;
                }

                newRoot.Add(new Line(startPtH2 - hVec * preParam, -hVec * curParam));
                newRoot.Add(new Line(startPtH2 + hVec * preParam, hVec * curParam));

                mainRoot.Add(new Line(startPtH2, -hVec * preParam));
                mainRoot.Add(new Line(startPtH2, hVec * preParam));

            }
            else if (tInfo.phase > 6)
            {

                lParam = 5;
                rParam = 5;
                if (tInfo.phase <= 11)
                {
                    if (tInfo.phase == 9)
                    {
                        rParam = 2;
                        deadRoot.Add(new Line(startPtH2 + hVec * 2, hVec * 3));
                    }
                    else if (tInfo.phase == 10)
                    {
                        rParam = 0;
                        deadRoot.Add(new Line(startPtH2, hVec * 2));
                    }
                    else if (tInfo.phase == 11)
                    {
                        rParam = 0;
                    }
                    mainRoot.Add(new Line(startPtH2, hVec * rParam));
                    mainRoot.Add(new Line(startPtH2, -hVec * lParam));

                }
                else
                {
                    deadRoot.Add(new Line(startPtH2, -hVec * lParam));
                }

            }


            //! horizontal tap root (3rd layer)
            // -------------------
            //         |
            //       -----
            //         |
            //       *****

            int prevParam = 0;
            var startPtH3 = anchorPt - sMap.pln.YAxis * vL * 4;
            if (tInfo.phase == 6)
            {
                curParam = 3;
                newRoot.Add(new Line(startPtH3, hVec * curParam));
                newRoot.Add(new Line(startPtH3, -hVec * curParam));
            }
            else if (tInfo.phase == 7)
            {
                prevParam = 3;
                curParam = 1;
                newRoot.Add(new Line(startPtH3 + hVec * prevParam, hVec * curParam));
                newRoot.Add(new Line(startPtH3 - hVec * prevParam, -hVec * curParam));

                mainRoot.Add(new Line(startPtH3, hVec * prevParam));
                mainRoot.Add(new Line(startPtH3, -hVec * prevParam));
            }
            else if (tInfo.phase == 8)
            {
                deadRoot.Add(new Line(startPtH3, hVec * 4));
                deadRoot.Add(new Line(startPtH3, -hVec * 4));
            }

            // export all 
            DA.SetDataList("RootMain", mainRoot);
            DA.SetDataList("RootNew", newRoot);
            DA.SetDataList("Dead Roots", deadRoot);
        }
    }

}