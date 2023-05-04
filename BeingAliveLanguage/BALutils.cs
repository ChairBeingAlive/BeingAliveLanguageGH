using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;

namespace BeingAliveLanguage
{
    public class BALsoilInfo : GH_Component
    {
        public BALsoilInfo() :
            base("Soil_Information", "balSoilInfoText",
                "Export the soil information in text format.",
                "BAL", "09::utils")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilInfo;
        public override Guid ComponentGuid => new Guid("af64a14a-6795-469c-b044-7db972d5bd84");

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

    }

    public class BALmorphFan : GH_Component
    {
        public BALmorphFan() :
            base("Bound Morph (Fan)", "balMorphFan",
            "Morph the soil diagram from a rectangle shape into a fan shape.",
            "BAL", "09::utils")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => Properties.Resources.balSoilInfo;
        public override Guid ComponentGuid => new Guid("e08cd0fa-c27d-4d1b-85fc-da0353bda292");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Base", "soilBase", "The base object used for soil diagram generation.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Soil Geo", "soilG", "Any line- or polyline-based geometries from the soil system.", GH_ParamAccess.list);

            pManager.AddCurveParameter("Target Fan", "tFan", "The target closed fan shape curve to morph to.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Transformed Geo", "fSoilG", "The morphed soil geometries inside the given fan shape.", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Transformed Plane", "fP", "The based plane for the polar coordinate system used during the mapping process", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // load input, check validility
            var sBase = new SoilBase();
            var soilG = new List<Curve>();
            Curve tFan = null;
            if (!DA.GetData("Soil Base", ref sBase))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "no soil base detected, mapping results might be incorrect."); }

            if (!DA.GetDataList("Soil Geo", soilG))
            { return; }

            if (!DA.GetData("Target Fan", ref tFan))
            { return; }

            var polyCrvFan = tFan as PolyCurve;

            polyCrvFan.TryGetPlane(out Plane tPln);
            if (!polyCrvFan.IsPlanar() || tPln == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "target boundary is not planar.");
                return;
            }

            // ! explode and detect arc, new original point
            var segFan = polyCrvFan.Explode().ToList();

            List<Arc> tArc = new List<Arc>();
            double polarLenRange = 0.0;
            foreach (var seg in segFan)
            {
                if (seg.TryGetArc(out Arc t))
                {
                    tArc.Add(t);
                }
                else
                {
                    polarLenRange = seg.GetLength();
                }
            }

            var innerArc = tArc.OrderBy(x => x.Length).First();
            //innerArc.Plane = tPln;

            // make sure the direction is always CW
            if (innerArc.Plane.ZAxis * tPln.ZAxis < 0)
            //if (Vector3d.CrossProduct((innerArc.PointAt(0) - innerArc.Center), innerArc.TangentAt(0)) * tPln.ZAxis < 0)
            { innerArc.Reverse(); }

            //var yDir = innerArc.TangentAt(0);
            //var xDir = Vector3d.CrossProduct(tPln.ZAxis, innerArc.TangentAt(0));
            var xDir = innerArc.StartPoint - innerArc.Center;
            var yDir = Vector3d.CrossProduct(tPln.ZAxis, xDir);
            var basePln = new Plane(innerArc.Center, xDir, yDir);

            var polarAngRange = Math.Abs(innerArc.EndAngle - innerArc.StartAngle);
            var s0 = innerArc.Radius;


            /* 
             * The approach here is to:
             * 1. parametrize the original geometry in the original bounds
             * 2. convert the parametrized coordinates into the new polar system
             * 3. convert the polar coordinates into the local Euclidian system
             * 4. convert the local to the world global system
             */

            var newSoilG = new List<PolylineCurve>();
            foreach (var crv in soilG)
            {
                if (crv.TryGetPolyline(out Polyline polyln))
                {
                    var newPoly = new Polyline();
                    foreach (var pt in polyln)
                    {
                        // step 1
                        sBase.pln.ClosestParameter(pt, out double s, out double t);
                        var sParam = s / sBase.bnd.Width;
                        var tParam = t / sBase.bnd.Height;

                        // step 2
                        var u = s0 + tParam * polarLenRange;
                        var v = (1 - sParam) * polarAngRange;

                        // step 3
                        var localX = u * Math.Cos(v);
                        var localY = u * Math.Sin(v);

                        // step 4
                        var mappedPt = basePln.PointAt(localX, localY);
                        newPoly.Add(mappedPt);
                    }

                    newSoilG.Add(newPoly.ToPolylineCurve());
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Soil geometries contain non-polyline-based object(s).");
                    return;
                }
            }


            DA.SetDataList(0, newSoilG);
            DA.SetData(1, basePln);

        }

    }


}
