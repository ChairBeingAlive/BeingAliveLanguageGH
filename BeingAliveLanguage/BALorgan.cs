using System;
using System.Windows.Forms;
using System.Collections.Generic;

using Grasshopper.Kernel;
using GH_IO.Serialization;
using Rhino.Geometry;
using BeingAliveLanguage.BalCore;
using Grasshopper.GUI;
using System.Linq;
using System.Xml.Xsl;
using MathNet.Numerics.LinearAlgebra;

namespace BeingAliveLanguage
{
    /// <summary>
    /// Base class to derive all organ components with similar procedure.
    /// </summary>
    public class BALorganBase : GH_Component
    {
        public BALorganBase(string name, string nickname, string description, string category, string subcategory)
          : base(name, nickname, description, "BAL", "04::organ")
        {
        }

        protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTree3D);
        public override Guid ComponentGuid => new Guid("b1a34eee-cb0f-4607-bf9f-037d65113be0");
        public override GH_Exposure Exposure => GH_Exposure.hidden;

        // variables
        protected int mNum;
        protected int mTotalNum;
        protected int mPhase;
        protected bool mActive;
        protected double mRadius = 1.0; // default radius for the organ geometry, can be changed in derived classes
        protected Plane mPln = Plane.WorldXY;
        protected Curve mGeo;

        protected bool mSym = false;
        protected double mScale;
        protected double mDistBelowSrf;
        protected double mHorizontalScale;
        protected double mDisSurfaceRatio;

        protected virtual void GetInputs(IGH_DataAccess DA)
        {
            // initialize with different values
            mNum = 3;
            mPhase = 1;
            mScale = 1.0;
            mPln = new Plane();

            // take Input
            if (!DA.GetData("Plane", ref mPln))
            { return; }
            if (!DA.GetData("Base Number", ref mNum))
            { return; }
            if (!DA.GetData("Phase", ref mPhase))
            { return; }
            if (!DA.GetData("Scale", ref mScale))
            { return; }
        }

        protected virtual void SetOutputs(IGH_DataAccess DA)
        {
            // Set output
            DA.SetData("State", mActive ? "active" : "inactive");
        }

        protected virtual void prepareGeo()
        {
            mGeo = new Circle().ToNurbsCurve();
        }

        protected virtual void prepareParam()
        {
            // compute current states
            mActive = mPhase % 2 == 1;

            // compute current total number of organs (based on symmetric or not)
            mTotalNum = mSym == true ? mNum + ((mPhase + 1) / 2 - 1) * 2 : mNum + ((mPhase + 1) / 2 - 1);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Plane", "pln", "Base plane to draw the organ.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Base Number", "num", "Number of the organ in the initial phase.", GH_ParamAccess.item, 3);
            pManager.AddIntegerParameter("Phase", "phase", "Phase of the organ.", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Scale", "s", "Scale of the organ.", GH_ParamAccess.item, 1.0);
            //pManager.AddBooleanParameter("Symmetric", "sym", "Symmetric or not.", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("State", "state", "State of the organ (active or inactive).", GH_ParamAccess.item);
            pManager.AddCurveParameter("ExistingOrgan", "exiOrg", "Existing organs from current or previous years.", GH_ParamAccess.list);
            pManager.AddCurveParameter("NewOrgan", "newOrg", "New organs from the current year.", GH_ParamAccess.list);
            pManager.AddLineParameter("ExistingGrassyPart", "exiGrass", "Existing grassy part of the organ.", GH_ParamAccess.list);
            pManager.AddLineParameter("NewGrassyPart", "newGrass", "Newly grown grassy part of the organ.", GH_ParamAccess.list);
            pManager.AddLineParameter("RootPart", "Root", "Root of the organ.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GetInputs(DA);
            SetOutputs(DA);
        }

        /// <summary>
        /// Function to draw the root or grass part of an organ
        /// </summary>
        /// <param name="pivot"></param>
        /// <param name="dir"></param>
        /// <param name="num"></param>
        /// <param name="proximateLen"></param>
        /// <param name="openingAngle"></param>
        /// <returns></returns>
        protected List<Line> DrawGrassOrRoot(Point3d pivot, Vector3d dir,
                                            int num = 2, double scale = 1, double proximateLen = 10, double openingAngle = 10)
        {
            var res = new List<Line>();

            double[] lengths = { proximateLen * 0.9 * scale, proximateLen * 1.1 * scale, proximateLen * 1.5 * scale };
            double[] angleVariations = { -openingAngle, openingAngle, 0 };

            // draw the grass/root part. Notice the sequence when constructing the param above
            for (int i = 0; i < num; i++)
            {
                double length = lengths[i];
                double angleVariation = angleVariations[i];
                var direction = dir;
                direction.Rotate(angleVariation * Math.PI / 180, mPln.ZAxis);
                var endPt = pivot + direction * length;
                res.Add(new Line(pivot, endPt));
            }

            return res;
        }
    }


    /// <summary>
    /// Organ Type: Tuft
    /// </summary>
    public class BALorganTuft : BALorganBase
    {
        public BALorganTuft()
          : base("Organ_Tuft", "balTuft",
                "Organ of resistance -- 'tuft'.",
                "BAL", "04::organ")
        {
            mSym = true; // Assigning the value in the constructor
            mHorizontalScale = 0.5;
            mDisSurfaceRatio = 1;
        }

        public BALorganTuft(string name, string nickname, string description, string category, string subcategory) : base(name, nickname, description, category, subcategory)
        {
            mSym = true; // Assigning the value in the constructor
        }

        protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTree3D);
        public override Guid ComponentGuid => new Guid("a7fdb09e-39e7-4ceb-a78f-b2b2ab71f572");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void prepareGeo()
        {
            var circle = new Circle(mPln, mRadius);
            mGeo = circle.ToNurbsCurve();

            var xform = Transform.Scale(mPln, mHorizontalScale * mScale, 1 * mScale, 1 * mScale);
            mGeo.Domain = new Interval(0, 1);

            mGeo.Transform(xform);
            mGeo.Translate(-mPln.YAxis * mRadius * mScale * mDisSurfaceRatio);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // get the inputs: plane, num, phase, etc.
            base.GetInputs(DA);

            // prepare the basic geometry & parameter
            prepareGeo();
            prepareParam();

            if (mNum < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Base number should be at least 1.");
            }

            var horizontalSpacing = mHorizontalScale * mScale * 2; // radius = 1, D = 2

            var geoCol = new List<Curve>() { mGeo };
            var exiOrganLst = new List<Curve>();
            var newOrganLst = new List<Curve>();
            var exiGrassLst = new List<Line>();
            var newGrassLst = new List<Line>();
            var rootLst = new List<Line>();


            if (mSym)
            {
                if (mNum % 2 == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "When `sym==TRUE`, even count will be rounded to the nearest odd number.");
                    return;
                }

                // Core Organ part
                for (int i = 0; i < mTotalNum / 2; i++)
                {
                    var newGeo = mGeo.Duplicate() as NurbsCurve;
                    if (newGeo == null)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Internal NURBS casting error.");
                    }
                    newGeo.Translate(horizontalSpacing * (i + 1), 0, 0);
                    geoCol.Add(newGeo);

                    var newGeoMirror = mGeo.Duplicate() as NurbsCurve;
                    newGeoMirror.Translate(-horizontalSpacing * (i + 1), 0, 0);
                    geoCol.Add(newGeoMirror);
                }

                if (mActive)
                {
                    exiOrganLst = geoCol.GetRange(0, geoCol.Count - 2);
                    newOrganLst = geoCol.GetRange(geoCol.Count - 2, 2);
                }
                else
                {
                    exiOrganLst = geoCol;
                }

            }
            else
            {
                for (int i = 0; i < mTotalNum; i++)
                {
                    var newGeo = mGeo.Duplicate() as NurbsCurve;
                    if (newGeo == null)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Internal NURBS casting error.");
                    }
                    newGeo.Translate(horizontalSpacing * i, 0, 0);
                    geoCol.Add(newGeo);
                }

                // no symmetry, only take 1 element as "new"
                if (mActive)
                {
                    exiOrganLst = geoCol.GetRange(0, geoCol.Count - 1);
                    newOrganLst = geoCol.GetRange(geoCol.Count - 1, 1);
                }
                else
                {
                    exiOrganLst = geoCol;
                }
            }

            // Global root/Grass build based on active state
            if (mActive)
            {
                // Existing organ: long grass, with roots
                foreach (var crv in exiOrganLst)
                {
                    var topPt = crv.PointAt(0.25);
                    var grassL = DrawGrassOrRoot(topPt, mPln.YAxis, 2, mScale, mRadius * 10, 5);
                    exiGrassLst.AddRange(grassL);

                    // root part (active): only on existing organs
                    var botPt = crv.PointAt(0.75);
                    var rootL = DrawGrassOrRoot(botPt, -mPln.YAxis, 3, mScale, mRadius * 3);
                    rootLst.AddRange(rootL);
                }

                // New organ: short grass, no roots
                foreach (var crv in newOrganLst)
                {
                    var topPt = crv.PointAt(0.25);
                    var grassL = DrawGrassOrRoot(topPt, mPln.YAxis, 2, mScale, mRadius * 2, 15);
                    newGrassLst.AddRange(grassL);
                }
            }
            else
            {
                // root part (inactive): on all organs
                foreach (var crv in exiOrganLst)
                {
                    var botPt = crv.PointAt(0.75);
                    var grassL = DrawGrassOrRoot(botPt, -mPln.YAxis, 3, mScale, mRadius * 3.5);
                    newGrassLst.AddRange(grassL);
                }
            }

            DA.SetDataList("ExistingOrgan", exiOrganLst);
            DA.SetDataList("NewOrgan", newOrganLst);
            DA.SetDataList("ExistingGrassyPart", exiGrassLst);
            DA.SetDataList("NewGrassyPart", newGrassLst);
            DA.SetDataList("RootPart", rootLst);

            base.SetOutputs(DA);
        }
    }


    /// <summary>
    /// Organ Type: Rhizome
    /// </summary>
    public class BALorganRhizome : BALorganTuft
    {
        public BALorganRhizome()
          : base("Organ_Rhizome", "balRhizome",
                "Organ of resistance -- 'rhizome'.",
                "BAL", "04::organ")
        {
            mHorizontalScale = 1.2;
            mDisSurfaceRatio = 2;
        }

        protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTree3D);
        public override Guid ComponentGuid => new Guid("50264c56-b65f-4181-a49e-25ad9815771d");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Plane", "pln", "Base plane to draw the organ.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Base Number", "num", "Number of the organ in the initial phase.", GH_ParamAccess.item, 3);
            pManager.AddIntegerParameter("Phase", "phase", "Phase of the organ.", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Scale", "s", "Scale of the organ.", GH_ParamAccess.item, 1.0);
            pManager.AddBooleanParameter("Symmetric", "sym", "Symmetric or not.", GH_ParamAccess.item, true);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            base.prepareGeo();
            base.GetInputs(DA);

            if (!DA.GetData("Symmetric", ref mSym))
            { return; }


            base.prepareParam();
            base.SolveInstance(DA);
        }
    }


    /// <summary>
    /// Organ Type: GroundRunner
    /// </summary>
    public class BALorganGroundRunner : BALorganBase
    {
        public BALorganGroundRunner()
            : base("Organ_GroundRunner", "balGroundRunner",
                 "Organ of resistance -- 'ground runner (below / above).'",
                 "BAL", "04::organ")
        {
            mHorizontalScale = 2;
            mDisSurfaceRatio = 1;
        }

        protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTree3D);
        public override Guid ComponentGuid => new Guid("23bc24ad-16d0-4812-bfd6-060e6eefc48f");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        private string organLocation = "aboveGround"; // Default to centerLine mode

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Plane", "pln", "Base plane to draw the organ.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Base Number", "num", "Number of the organ in the initial phase.", GH_ParamAccess.item, 3);
            pManager.AddIntegerParameter("Phase", "phase", "Phase of the organ.", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Scale", "s", "Scale of the organ.", GH_ParamAccess.item, 1.0);
            pManager.AddBooleanParameter("Symmetric", "sym", "Symmetric or not.", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("State", "state", "State of the organ (active or inactive).", GH_ParamAccess.item);
            pManager.AddCurveParameter("ExistingOrgan", "exiOrg", "Existing organs from current or previous years.", GH_ParamAccess.list);
            pManager.AddCurveParameter("NewOrgan", "newOrg", "New organs from the current year.", GH_ParamAccess.list);
            pManager.AddLineParameter("ExistingGrassyPart", "exiGrass", "Existing grassy part of the organ.", GH_ParamAccess.list);
            pManager.AddLineParameter("NewGrassyPart", "newGrass", "Newly grown grassy part of the organ.", GH_ParamAccess.list);
            pManager.AddLineParameter("RootPart", "Root", "Root of the organ.", GH_ParamAccess.list);
        }

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);

            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "Organ Location:", (sender, e) => { }, false).Font = GH_FontServer.StandardItalic;
            Menu_AppendItem(menu, " Above Ground", (sender, e) => Menu.SelectMode(this, sender, e, ref organLocation, "aboveGround"), true, CheckDrawingMode("aboveGround"));
            Menu_AppendItem(menu, " Below Ground", (sender, e) => Menu.SelectMode(this, sender, e, ref organLocation, "belowGround"), true, CheckDrawingMode("belowGround"));
        }

        private bool CheckDrawingMode(string mode) => organLocation == mode;

        public override bool Write(GH_IWriter writer)
        {
            writer.SetString("organLocation", organLocation);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            if (reader.ItemExists("organLocation"))
                organLocation = reader.GetString("organLocation");

            return base.Read(reader);
        }


        protected override void prepareGeo()
        {
            if (organLocation == "belowGround")
            {
                mDisSurfaceRatio = 0.5;

                var startPt = mPln.Origin;
                var endPt = mPln.Origin + mHorizontalScale * mPln.XAxis;

                mGeo = new Line(startPt, endPt).ToNurbsCurve();
                mGeo.Domain = new Interval(0, 1);

                var xform = Transform.Scale(mPln, mHorizontalScale * mScale, 1 * mScale, 1 * mScale);
                mGeo.Transform(xform);
                mGeo.Translate(mPln.YAxis * mRadius * mScale * -1 * mDisSurfaceRatio);
            }

            // above ground runner use curved Arc for base geometry
            else if (organLocation == "aboveGround")
            {
                mDisSurfaceRatio = 0;
                mHorizontalScale = 2;

                var startPt = mPln.Origin;
                var endPt = mPln.Origin + mHorizontalScale * mPln.XAxis;
                var midPt = 0.5 * (mPln.Origin + endPt) + 0.3 * mPln.YAxis;
                mGeo = new Arc(startPt, midPt, endPt).ToNurbsCurve();
                mGeo.Domain = new Interval(0, 1);

                var xform = Transform.Scale(mPln, mHorizontalScale * mScale, 1 * mScale, 1 * mScale);
                mGeo.Transform(xform);
                mGeo.Translate(mPln.YAxis * mRadius * mScale * mDisSurfaceRatio);
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            base.GetInputs(DA);
            if (!DA.GetData("Symmetric", ref mSym))
            { return; }

            prepareGeo();
            base.prepareParam();

            var geoCol = new List<Curve>() { mGeo };
            var exiOrganLst = new List<Curve>();
            var newOrganLst = new List<Curve>();
            var exiGrassLst = new List<Line>();
            var newGrassLst = new List<Line>();
            var rootLst = new List<Line>();

            var horizontalSpacing = mHorizontalScale * mScale * 2; // radius = 1, D = 2

            if (mSym)
            {
                if (mNum % 2 == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "When `sym==TRUE`, even count will be rounded to the nearest odd number.");
                    return;
                }

                // Core Organ part
                for (int i = 0; i < mTotalNum / 2; i++)
                {
                    var newGeo = mGeo.Duplicate() as NurbsCurve;
                    if (newGeo == null)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Internal NURBS casting error.");
                    }
                    newGeo.Translate(horizontalSpacing * (i + 1), 0, 0);
                    geoCol.Add(newGeo);

                    var newGeoMirror = mGeo.Duplicate() as NurbsCurve;
                    newGeoMirror.Translate(-horizontalSpacing * (i + 1), 0, 0);
                    geoCol.Add(newGeoMirror);
                }

                if (mActive)
                {
                    exiOrganLst = geoCol.GetRange(0, geoCol.Count - 2);
                    newOrganLst = geoCol.GetRange(geoCol.Count - 2, 2);
                }
                else
                {
                    exiOrganLst = geoCol;
                }

            }
            else
            {
                for (int i = 0; i < mTotalNum; i++)
                {
                    var newGeo = mGeo.Duplicate() as NurbsCurve;
                    if (newGeo == null)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Internal NURBS casting error.");
                    }
                    newGeo.Translate(horizontalSpacing * i, 0, 0);
                    geoCol.Add(newGeo);
                }

                // no symmetry, only take 1 element as "new"
                if (mActive)
                {
                    exiOrganLst = geoCol.GetRange(0, geoCol.Count - 1);
                    newOrganLst = geoCol.GetRange(geoCol.Count - 1, 1);
                }
                else
                {
                    exiOrganLst = geoCol;
                }
            }

            // Global root/Grass build based on active state
            if (mActive)
            {
                // Existing organ: long grass, with roots
                foreach (var crv in exiOrganLst)
                {
                    var grass0 = DrawGrassOrRoot(crv.PointAt(0), mPln.YAxis, 2, mScale, mRadius * 10, 5);
                    exiGrassLst.AddRange(grass0);
                    var grass1 = DrawGrassOrRoot(crv.PointAt(0.5), mPln.YAxis, 2, mScale, mRadius * 10, 5);
                    exiGrassLst.AddRange(grass1);
                    var grass2 = DrawGrassOrRoot(crv.PointAt(1), mPln.YAxis, 2, mScale, mRadius * 10, 5);
                    exiGrassLst.AddRange(grass2);

                    // root part (active): only on existing organs
                    var root0 = DrawGrassOrRoot(crv.PointAt(0.0), -mPln.YAxis, 3, mScale, mRadius * 3);
                    rootLst.AddRange(root0);
                    var root1 = DrawGrassOrRoot(crv.PointAt(1.0), -mPln.YAxis, 3, mScale, mRadius * 3);
                    rootLst.AddRange(root1);
                }

                // New organ: short grass, no roots
                foreach (var crv in newOrganLst)
                {

                    var endPt = crv.PointAtStart.DistanceTo(mPln.Origin) > crv.PointAtEnd.DistanceTo(mPln.Origin) ? crv.PointAtStart : crv.PointAtEnd;
                    var grass1 = DrawGrassOrRoot(endPt, mPln.YAxis, 2, mScale, mRadius * 2, 15);
                    newGrassLst.AddRange(grass1);
                }
            }
            else
            {
                // root part (inactive): on all organs
                foreach (var crv in exiOrganLst)
                {
                    var grass0 = DrawGrassOrRoot(crv.PointAt(0.0), -mPln.YAxis, 3, mScale, mRadius * 3.5);
                    newGrassLst.AddRange(grass0);
                    var grass1 = DrawGrassOrRoot(crv.PointAt(1.0), -mPln.YAxis, 3, mScale, mRadius * 3.5);
                    newGrassLst.AddRange(grass1);
                }

            }

            DA.SetDataList("ExistingOrgan", exiOrganLst);
            DA.SetDataList("NewOrgan", newOrganLst);
            DA.SetDataList("ExistingGrassyPart", exiGrassLst);
            DA.SetDataList("NewGrassyPart", newGrassLst);
            DA.SetDataList("RootPart", rootLst);
            base.SetOutputs(DA);

        }
    }

    /// <summary>
    /// Organ Type: GroundRunner
    /// </summary>
    public class BALorganCreepingShoot : BALorganBase
    {
        public BALorganCreepingShoot()
            : base("Organ_CreepingShoot", "balCreepingShoot",
                 "Organ of resistance -- 'creeping shoot.'",
                 "BAL", "04::organ")
        {

            mHorizontalScale = 2;
            mDisSurfaceRatio = 1;
        }

        protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTree3D);
        public override Guid ComponentGuid => new Guid("9017348d-1243-433e-834c-c684ce082c10");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Plane", "pln", "Base plane to draw the organ.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Base Number", "num", "Number of the organ in the initial phase.", GH_ParamAccess.item, 3);
            pManager.AddIntegerParameter("Phase", "phase", "Phase of the organ.", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Scale", "s", "Scale of the organ.", GH_ParamAccess.item, 1.0);
            pManager.AddBooleanParameter("Symmetric", "sym", "Symmetric or not.", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("State", "state", "State of the organ (active or inactive).", GH_ParamAccess.item);
            pManager.AddCurveParameter("ExistingOrgan", "exiOrg", "Existing organs from current or previous years.", GH_ParamAccess.list);
            pManager.AddCurveParameter("NewOrgan", "newOrg", "New organs from the current year.", GH_ParamAccess.list);
            pManager.AddLineParameter("ExistingGrassyPart", "exiGrass", "Existing grassy part of the organ.", GH_ParamAccess.list);
            pManager.AddLineParameter("NewGrassyPart", "newGrass", "Newly grown grassy part of the organ.", GH_ParamAccess.list);
            pManager.AddLineParameter("RootPart", "Root", "Root of the organ.", GH_ParamAccess.list);
        }
        protected override void prepareGeo()
        {

            mDisSurfaceRatio = 0.5;
            mHorizontalScale = 2;

            var startPt = mPln.Origin;
            var endPt = mPln.Origin + mHorizontalScale * mPln.XAxis;
            mGeo = new Line(startPt, endPt).ToNurbsCurve();
            mGeo.Domain = new Interval(0, 1);

            var xform = Transform.Scale(mPln, mHorizontalScale * mScale, 1 * mScale, 1 * mScale);
            mGeo.Transform(xform);
            mGeo.Translate(mPln.YAxis * mRadius * mScale * mDisSurfaceRatio);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            base.GetInputs(DA);
            if (!DA.GetData("Symmetric", ref mSym))
            { return; }

            prepareGeo();
            base.prepareParam();

            var geoCol = new List<Curve>() { mGeo };
            var exiOrganLst = new List<Curve>();
            var newOrganLst = new List<Curve>();
            var exiGrassLst = new List<Line>();
            var newGrassLst = new List<Line>();
            var rootLst = new List<Line>();

            var horizontalSpacing = mHorizontalScale * mScale * 2; // radius = 1, D = 2

            if (mSym)
            {
                geoCol.Clear();
                if (mNum % 2 == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "When `sym==TRUE`, even count will be rounded to the nearest odd number.");
                    return;
                }

                // Core Organ part
                for (int i = 0; i < mTotalNum / 2; i++)
                {
                    var newGeo = mGeo.Duplicate() as NurbsCurve;
                    if (newGeo == null)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Internal NURBS casting error.");
                    }
                    newGeo.Translate(horizontalSpacing * i, 0, 0);
                    geoCol.Add(newGeo);

                    var newGeoMirror = mGeo.Duplicate() as NurbsCurve;
                    newGeoMirror.Translate(-horizontalSpacing * (i + 1), 0, 0);
                    geoCol.Add(newGeoMirror);
                }

                // For creeping shoot's last element, use a curve. we use an arc as the end-side new geometry for "newOrganLst"
                var pt0 = mGeo.PointAtStart;
                var pt1 = mGeo.PointAtEnd;
                var pt2 = pt1 + mGeo.GetLength() * 0.5 * mPln.YAxis;

                var newGeoLastSeg = new Arc(pt0, pt1 - pt0, pt2).ToNurbsCurve();
                var newGeoLastSegMirror = newGeoLastSeg.Duplicate() as NurbsCurve;

                newGeoLastSeg.Translate(horizontalSpacing * (mTotalNum / 2), 0, 0);
                geoCol.Add(newGeoLastSeg);

                var xform = Transform.Mirror(mPln.Origin, mPln.XAxis);
                newGeoLastSegMirror.Transform(xform);
                newGeoLastSegMirror.Translate(-horizontalSpacing * (mTotalNum / 2), 0, 0);
                geoCol.Add(newGeoLastSegMirror);

                if (mActive)
                {
                    exiOrganLst = geoCol.GetRange(0, geoCol.Count - 2);
                    newOrganLst = geoCol.GetRange(geoCol.Count - 2, 2);
                }
                else
                {
                    exiOrganLst = geoCol;
                }

            }
            else
            {
                // For n-1 element, use straight line
                for (int i = 0; i < mTotalNum - 1; i++)
                {
                    var newGeo = mGeo.Duplicate() as NurbsCurve;
                    if (newGeo == null)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Internal NURBS casting error.");
                    }
                    newGeo.Translate(horizontalSpacing * i, 0, 0);
                    geoCol.Add(newGeo);
                }

                // For creeping shoot's last element, use a curve. we use an arc as the end-side new geometry for "newOrganLst"
                var pt0 = mGeo.PointAtStart;
                var pt1 = mGeo.PointAtEnd;
                var pt2 = pt1 + mGeo.GetLength() * 0.5 * mPln.YAxis;

                var newGeoLastSeg = new Arc(pt0, pt1 - pt0, pt2).ToNurbsCurve();
                newGeoLastSeg.Translate(horizontalSpacing * (mTotalNum - 1), 0, 0);
                geoCol.Add(newGeoLastSeg);

                // no symmetry, only take 1 element as "new"
                if (mActive)
                {
                    exiOrganLst = geoCol.GetRange(0, geoCol.Count - 1);
                    newOrganLst = geoCol.GetRange(geoCol.Count - 1, 1);
                }
                else
                {
                    exiOrganLst = geoCol;
                }
            }

            // Global root/Grass build based on active state
            if (mActive)
            {
                // Existing organ: long grass, with roots
                foreach (var crv in exiOrganLst)
                {
                    var grass0 = DrawGrassOrRoot(crv.PointAt(0), mPln.YAxis, 2, mScale, mRadius * 10, 5);
                    exiGrassLst.AddRange(grass0);
                    var grass1 = DrawGrassOrRoot(crv.PointAt(0.5), mPln.YAxis, 2, mScale, mRadius * 10, 5);
                    exiGrassLst.AddRange(grass1);
                    var grass2 = DrawGrassOrRoot(crv.PointAt(1), mPln.YAxis, 2, mScale, mRadius * 10, 5);
                    exiGrassLst.AddRange(grass2);

                    // root part (active): only on existing organs
                    var root0 = DrawGrassOrRoot(crv.PointAt(0.0), -mPln.YAxis, 3, mScale, mRadius * 3);
                    rootLst.AddRange(root0);
                    var root1 = DrawGrassOrRoot(crv.PointAt(1.0), -mPln.YAxis, 3, mScale, mRadius * 3);
                    rootLst.AddRange(root1);
                }

                // New organ: short grass, no roots
                foreach (var crv in newOrganLst)
                {
                    var endPt = crv.PointAtStart.DistanceTo(mPln.Origin) > crv.PointAtEnd.DistanceTo(mPln.Origin) ? crv.PointAtStart : crv.PointAtEnd;
                    var grass1 = DrawGrassOrRoot(endPt, mPln.YAxis, 2, mScale, mRadius * 2, 15);
                    newGrassLst.AddRange(grass1);
                }
            }
            else
            {
                // root part (inactive): on all organs
                foreach (var crv in exiOrganLst)
                {
                    var grass0 = DrawGrassOrRoot(crv.PointAtStart, -mPln.YAxis, 3, mScale, mRadius * 3.5);
                    newGrassLst.AddRange(grass0);
                    var grass1 = DrawGrassOrRoot(crv.PointAtEnd, -mPln.YAxis, 3, mScale, mRadius * 3.5);
                    newGrassLst.AddRange(grass1);
                }

            }

            DA.SetDataList("ExistingOrgan", exiOrganLst);
            DA.SetDataList("NewOrgan", newOrganLst);
            DA.SetDataList("ExistingGrassyPart", exiGrassLst);
            DA.SetDataList("NewGrassyPart", newGrassLst);
            DA.SetDataList("RootPart", rootLst);
            base.SetOutputs(DA);

        }
    }


    /// <summary>
    /// Organ Type: Cushion
    /// </summary>
    public class BALorganCushion : BALorganBase
    {
        public BALorganCushion()
            : base("Organ_Cushion", "balCushion",
                 "Organ of resistance -- 'cushion.'",
                 "BAL", "04::organ")
        {

            mHorizontalScale = 2;
            mDisSurfaceRatio = 1;
            mSym = true;
        }

        protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTree3D);
        public override Guid ComponentGuid => new Guid("4df84657-3f92-46b1-a5f8-ecd9ca5ce269");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Plane", "pln", "Base plane to draw the organ.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Base Number", "num", "Number of the organ in the initial phase.", GH_ParamAccess.item, 3);
            pManager.AddIntegerParameter("Phase", "phase", "Phase of the organ.", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Scale", "s", "Scale of the organ.", GH_ParamAccess.item, 1.0);
            //pManager.AddBooleanParameter("Symmetric", "sym", "Symmetric or not.", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("State", "state", "State of the organ (active or inactive).", GH_ParamAccess.item);
            pManager.AddCurveParameter("ExistingOrgan", "exiOrg", "Existing organs from current or previous years.", GH_ParamAccess.list);
            pManager.AddCurveParameter("NewOrgan", "newOrg", "New organs from the current year.", GH_ParamAccess.list);
            pManager.AddLineParameter("ExistingGrassyPart", "exiGrass", "Existing grassy part of the organ.", GH_ParamAccess.list);
            pManager.AddLineParameter("NewGrassyPart", "newGrass", "Newly grown grassy part of the organ.", GH_ParamAccess.list);
            pManager.AddLineParameter("RootPart", "Root", "Root of the organ.", GH_ParamAccess.list);
        }
        protected override void prepareGeo()
        {

            mDisSurfaceRatio = 0.5;
            mHorizontalScale = 2;

            var startPt = mPln.Origin;
            var endPt = mPln.Origin + mHorizontalScale * mPln.XAxis;
            mGeo = new Line(startPt, endPt).ToNurbsCurve();
            mGeo.Domain = new Interval(0, 1);

            var xform = Transform.Scale(mPln, mHorizontalScale * mScale, 1 * mScale, 1 * mScale);
            mGeo.Transform(xform);
            mGeo.Translate(mPln.YAxis * mRadius * mScale * mDisSurfaceRatio);
        }
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            base.GetInputs(DA);

            prepareGeo();
            base.prepareParam();

            var geoCol = new List<Curve>() { mGeo };
            var exiOrganLst = new List<Curve>();
            var newOrganLst = new List<Curve>();
            var exiGrassLst = new List<Line>();
            var newGrassLst = new List<Line>();
            var rootLst = new List<Line>();

            var horizontalSpacing = mHorizontalScale * mScale * 2; // radius = 1, D = 2

            // always symetry for cushion
            if (!mSym)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cushion should always be symmetric."); }
            else
            {
                geoCol.Clear();
                if (mNum % 2 == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "When `sym==TRUE`, even count will be rounded to the nearest odd number.");
                    return;
                }

                geoCol.Clear();

                for (int i = 0; i < mTotalNum / 2; i++)
                {
                    // we need to compose the permanent curve using two part: the horizontal line + the curved arc
                    var baseCrv = new Line(mGeo.PointAtStart, mGeo.PointAtStart + mPln.XAxis * 0.5 * mGeo.GetLength() * (i + 1)).ToNurbsCurve();
                    var xform = Transform.Scale(mPln.Origin, mScale);
                    baseCrv.Transform(xform);

                    var ptEnd = baseCrv.PointAtEnd;
                    var ptHigh = ptEnd + mScale * (0.5 * mPln.XAxis + 0.4 * mPln.YAxis);
                    var endArc = new Arc(ptEnd, mPln.XAxis, ptHigh).ToNurbsCurve();
                    //endArc.Translate(horizontalSpacing * i, 0, 0);
                    var joinCrvRes = Curve.JoinCurves(new NurbsCurve[] { baseCrv, endArc });
                    if (joinCrvRes.Length > 1)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Organ curves cannot be joined.");
                    }
                    var composeCrv = joinCrvRes[0] as Curve;
                    geoCol.Add(composeCrv);

                    var xformMirror = Transform.Mirror(mPln.Origin, mPln.XAxis);
                    var composeCrvMirror = composeCrv.Duplicate() as Curve;
                    composeCrvMirror.Transform(xformMirror);

                    geoCol.Add(composeCrvMirror);
                }

                // For active phase, add small extended lines along each arc to represent the active part
                if (mActive)
                {
                    exiOrganLst = geoCol; // All lines and arcs are permanent/existing

                    // Create small extended lines along each arc for active phase
                    for (int i = 0; i < geoCol.Count; i++) // Skip base curve (index 0)
                    {
                        var arc = geoCol[i];
                        var endPoint = arc.PointAtEnd;
                        var tangent = arc.TangentAtEnd;

                        // Create a small extension line in the direction of the tangent
                        var extensionLine = new Line(
                            endPoint,
                            endPoint + tangent * mScale * mRadius * 0.75
                        ).ToNurbsCurve();

                        newOrganLst.Add(extensionLine);
                    }
                }
                else
                {
                    exiOrganLst = geoCol; // All geometry is existing when inactive
                }
            }


            // Global root/Grass build based on active state
            if (mActive)
            {
                // Existing organ: long grass, with roots
                foreach (var crv in newOrganLst)
                {
                    var grass0 = DrawGrassOrRoot(crv.PointAtEnd, mPln.YAxis, 2, mScale, mRadius * 10, 5);
                    exiGrassLst.AddRange(grass0);
                }

                // New organ: short grass, no roots
                foreach (var crv in newOrganLst)
                {
                    var endPt = crv.PointAtStart.DistanceTo(mPln.Origin) > crv.PointAtEnd.DistanceTo(mPln.Origin) ? crv.PointAtStart : crv.PointAtEnd;
                    var grass1 = DrawGrassOrRoot(endPt, mPln.YAxis, 2, mScale, mRadius * 2, 15);
                    newGrassLst.AddRange(grass1);
                }
            }

            // Root: no matter active/inactive
            var root0 = DrawGrassOrRoot(exiOrganLst[0].PointAtStart, -mPln.YAxis, 3, mScale, mRadius * 3.5);
            rootLst.AddRange(root0);

            DA.SetDataList("ExistingOrgan", exiOrganLst);
            DA.SetDataList("NewOrgan", newOrganLst);
            DA.SetDataList("ExistingGrassyPart", exiGrassLst);
            DA.SetDataList("NewGrassyPart", newGrassLst);
            DA.SetDataList("RootPart", rootLst);
            base.SetOutputs(DA);
        }
    }


    /// <summary>
    /// Organ Type: TapRoot
    /// </summary>
    public class BALorganTapRoot : BALorganBase
    {
        public BALorganTapRoot()
            : base("Organ_TapRoot", "balTapRoot",
                 "Organ of resistance -- 'Tap Root.'",
                 "BAL", "04::organ")
        {

            mHorizontalScale = 2;
            mDisSurfaceRatio = 1;
        }

        protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTree3D);
        public override Guid ComponentGuid => new Guid("7366c871-8267-4087-a9d4-9bb8edaa40df");
        public override GH_Exposure Exposure => GH_Exposure.primary;

        Polyline inPoly = new Polyline();
        Polyline outPoly = new Polyline();

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Plane", "pln", "Base plane to draw the organ.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Base Number", "num", "Number of the organ in the initial phase.", GH_ParamAccess.item, 3);
            pManager.AddIntegerParameter("Phase", "phase", "Phase of the organ.", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Scale", "s", "Scale of the organ.", GH_ParamAccess.item, 1.0);
            //pManager.AddBooleanParameter("Symmetric", "sym", "Symmetric or not.", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("State", "state", "State of the organ (active or inactive).", GH_ParamAccess.item);
            pManager.AddCurveParameter("ExistingOrgan", "exiOrg", "Existing organs from current or previous years.", GH_ParamAccess.list);
            pManager.AddCurveParameter("NewOrgan", "newOrg", "New organs from the current year.", GH_ParamAccess.list);
            pManager.AddLineParameter("ExistingGrassyPart", "exiGrass", "Existing grassy part of the organ.", GH_ParamAccess.list);
            pManager.AddLineParameter("NewGrassyPart", "newGrass", "Newly grown grassy part of the organ.", GH_ParamAccess.list);
            pManager.AddLineParameter("RootPart", "Root", "Root of the organ.", GH_ParamAccess.list);
        }

        protected override void prepareGeo()
        {

            mDisSurfaceRatio = 0.5;
            mHorizontalScale = 2;

            // compose inner semi-circle of tapRoot's top
            var semiCircle = new Arc(mPln, 1, Math.PI).ToNurbsCurve();
            semiCircle.Rebuild(6, 1, false).TryGetPolyline(out Polyline polyCrv);

            outPoly = polyCrv.Duplicate();
            var outPolyPts = outPoly.GetRange(1, outPoly.Count - 2);
            outPolyPts.Reverse();

            var outPolyPtsTranslated = new List<Point3d>();
            foreach (var pt in outPolyPts)
            {
                var xTrans = Transform.Translation(0.7 * (pt - mPln.Origin));
                pt.Transform(xTrans);
                outPolyPtsTranslated.Add(pt);
            }

            outPoly.AddRange(outPolyPtsTranslated);
            outPoly.Add(outPoly[0]);

            inPoly = polyCrv.Duplicate();
            var  downPt = mPln.Origin - 10 * mPln.YAxis;
            inPoly.Add(downPt);
            inPoly.Add(inPoly[0]);

            mGeo = new Line(mPln.Origin, downPt).ToNurbsCurve();
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            base.GetInputs(DA);

            prepareGeo();
            base.prepareParam();

            var geoCol = new List<Curve>() { mGeo };
            var exiOrganLst = new List<Curve>();
            var newOrganLst = new List<Curve>();
            var exiGrassLst = new List<Line>();
            var newGrassLst = new List<Line>();
            var rootLst = new List<Line>();

            var xScale = Transform.Scale(mPln.Origin, mScale);
            inPoly.Transform(xScale);
            outPoly.Transform(xScale);

            for (int i = 0; i < mPhase; i++)
            {
                xScale = Transform.Scale(mPln.Origin, 1.15);
                inPoly.Transform(xScale);
                outPoly.Transform(xScale);
            }

            // Global root/Grass build based on active state
            exiOrganLst.Add(inPoly.ToPolylineCurve());
            if (mActive)
            {
                // draw the grass of active state
                newOrganLst.Add(outPoly.ToPolylineCurve());
            }

            // Root on the existing part of tapRoot
            //var root0 = DrawGrassOrRoot(exiOrganLst[0].PointAtStart, -mPln.YAxis, 3, mScale, mRadius * 3.5);
            //rootLst.AddRange(root0);


            DA.SetDataList("ExistingOrgan", exiOrganLst);
            DA.SetDataList("NewOrgan", newOrganLst);
            DA.SetDataList("ExistingGrassyPart", exiGrassLst);
            DA.SetDataList("NewGrassyPart", newGrassLst);
            DA.SetDataList("RootPart", rootLst);
            base.SetOutputs(DA);

        }
    }
}
