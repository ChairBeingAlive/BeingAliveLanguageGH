using Eto.Forms;
using Rhino.Geometry;
using Rhino.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BeingAliveLanguageRC
{
    public static class Utils
    {

        // testing func
        public static double Addition(double a, double b)
        {
            var c = cppBAL.BAL_Addition(a, b);
            return c;
        }

        // Poisson Elimination Sampling
        public static void SampleElim(in List<Point3d> inPt, double area, int num, out List<Point3d> outPt)
        {
            var Parray = new List<double>();
            foreach (var p in inPt)
            {
                Parray.Add(p.X);
                Parray.Add(p.Y);
                Parray.Add(p.Z);
            }

            var inPcpp = new Rhino.Runtime.InteropWrappers.SimpleArrayDouble(Parray);
            var outPcpp = new Rhino.Runtime.InteropWrappers.SimpleArrayPoint3d();

            cppBAL.BAL_possionDiskElimSample(inPcpp.ConstPointer(), area, num, outPcpp.NonConstPointer());

            // assign to the output
            outPt = new List<Point3d>(outPcpp.ToArray());
        }

        // Poisson Elimination Sampling overload, given rectangle bound
        public static void SampleElim(in Rectangle3d bnd, int num, out List<Point3d> genPt, out List<Point3d> outPt, int seed = -1, double bndScale = 1.0)
        {

            var rnd = seed >= 0 ? new Random(seed) : new Random(Guid.NewGuid().GetHashCode());


            var toLocal = Transform.ChangeBasis(Plane.WorldXY, bnd.Plane);
            var toWorld = Transform.ChangeBasis(bnd.Plane, Plane.WorldXY);

            var curBnd = bnd;
            // we transform "scale" into "offset" to have equal distance in borders
            if (bndScale != 1.0)
            {
                curBnd.Transform(toWorld);
                var w = (curBnd.Corner(1) - curBnd.Corner(0)).Length;
                var h = (curBnd.Corner(3) - curBnd.Corner(0)).Length;

                double wScale = (w - h * (1 - bndScale)) / w;

                var tmpPln = Plane.WorldXY;
                tmpPln.Translate(curBnd.Center - Plane.WorldXY.Origin);
                curBnd.Transform(Transform.Scale(tmpPln, wScale, bndScale, 1));
                //bnd.Transform(Transform.Scale(bnd.Center, bndScale));

                curBnd.Transform(toLocal);
            }

            // generate random points
            var lowerLeft = curBnd.Corner(0);
            var diagVec = curBnd.Corner(2) - curBnd.Corner(0);

            genPt = new List<Point3d>();
            for (int i = 0; i < num * 15; i++)
            {
                genPt.Add(new Point3d(rnd.NextDouble() * diagVec.X, rnd.NextDouble() * diagVec.Y, rnd.NextDouble() * diagVec.Z) + lowerLeft);
            }

            SampleElim(genPt, curBnd.Area, num, out outPt);
        }

        // helper func
        //public static Random rnd = new Random(Guid.NewGuid().GetHashCode());
    }
}
