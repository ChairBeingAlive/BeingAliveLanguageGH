using Rhino.Geometry;
using Rhino.Runtime;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BeingAliveLanguageRC
{
    public static class Utils
    {
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

        public static double Addition(double a, double b)
        {
            var c = cppBAL.BAL_Addition(a, b);
            return c;
        }
    }
}
