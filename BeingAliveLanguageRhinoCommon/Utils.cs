using Eto.Forms;
using Rhino.Geometry;
using Rhino.Runtime;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BeingAliveLanguageRhinoCommon
{
    internal static class Utils
    {
        public static void SampleElim(in List<Point3d> inPt, int num, out List<Point3d> outPt)
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

            cppBAL.BAL_possionDiskElimSample(inPcpp.ConstPointer(), num, outPcpp.NonConstPointer());

            // assign to the output
            outPt = new List<Point3d>(outPcpp.ToArray());
        }
    }
}
