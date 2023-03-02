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
            var Parray = new List<float>();
            foreach (var p in inPt)
            {
                Parray.Add((float)p.X);
                Parray.Add((float)p.Y);
                Parray.Add((float)p.Z);
            }

            var inPcpp = new Rhino.Runtime.InteropWrappers.SimpleArrayFloat(Parray);
            var outPcpp = new Rhino.Runtime.InteropWrappers.SimpleArrayPoint3d();

            cppBAL.BAL_possionDiskElimSample(inPcpp.ConstPointer(), area, num, outPcpp.NonConstPointer());

            // assign to the output
            outPt = new List<Point3d>(outPcpp.ToArray());
        }


        // Poisson Elimination Sampling overload, given rectangle bound
        public static void SampleElim(Rectangle3d bnd, int num, out List<Point3d> genPt, out List<Point3d> outPt)
        {
            //var minMaxCorner = new List<Point3d> { bnd.Corner(0), bnd.Corner(2) };
            var lowerLeft = bnd.Corner(0);
            //var upperRight = bnd.Corner(2);
            var diagVec = bnd.Corner(2) - bnd.Corner(0);

            genPt = new List<Point3d>();
            for (int i = 0; i < num * 10; i++)
            {
                genPt.Add(new Point3d(rnd.NextDouble() * diagVec.X, rnd.NextDouble() * diagVec.Y, rnd.NextDouble() * diagVec.Z) + lowerLeft);
            }

            SampleElim(genPt, bnd.Area, num, out outPt);
        }


        // helper func
        public static Random rnd = new Random(Guid.NewGuid().GetHashCode());
    }
}
