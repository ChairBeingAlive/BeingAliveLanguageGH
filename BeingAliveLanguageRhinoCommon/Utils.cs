using Rhino.Geometry;
using Rhino.Runtime;
using System;
using System.Collections.Generic;

namespace BeingAliveLanguageRC
{
  public static class Utils
  {

    // testing func
    //public static double Addition(double a, double b)
    //{
    //    var c = cppBAL.BAL_Addition(a, b);
    //    return c;
    //}

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

      // ! disable when developing nonCPP-required functions, so that MS's hotReload can work
      cppBAL.BAL_possionDiskElimSample(inPcpp.ConstPointer(), area, num, outPcpp.NonConstPointer());

      // assign to the output
      outPt = new List<Point3d>(outPcpp.ToArray());
    }

    // Poisson Elimination Sampling overload, given rectangle bound
    // the use of sample seed here is only for generating the initial points, elimination process is still random
    public static void SampleElim(in Rectangle3d bnd, int num, out List<Point3d> genPt, out List<Point3d> outPt, int seed = -1, double bndScale = 1.0, int initPtRange = 8)
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
      for (int i = 0; i < num * initPtRange * 2; i++)
      {
        genPt.Add(new Point3d(rnd.NextDouble() * diagVec.X, rnd.NextDouble() * diagVec.Y, rnd.NextDouble() * diagVec.Z) + lowerLeft);
      }

      SampleElim(genPt, curBnd.Area, num, out outPt);
    }

    //  // Compute the convex hull of a set of points
    //  public static Mesh ComputeHull(in List<Point3d> inPt)
    //  {
    //    var Parray = new List<double>();
    //    foreach (var p in inPt)
    //    {
    //      Parray.Add(p.X);
    //      Parray.Add(p.Y);
    //      Parray.Add(p.Z);
    //    }

    //    var inPcpp = new Rhino.Runtime.InteropWrappers.SimpleArrayDouble(Parray);
    //    var outVcpp = new Rhino.Runtime.InteropWrappers.SimpleArrayPoint3d();
    //    var outFcpp = new Rhino.Runtime.InteropWrappers.SimpleArrayInt();

    //    // ! disable when developing nonCPP-required functions, so that MS's hotReload can work
    //    cppBAL.BAL_computeHull(inPcpp.ConstPointer(), outVcpp.NonConstPointer(), outFcpp.NonConstPointer());

    //    // assign the output from cpp side
    //    var outV = new List<Point3d>(outVcpp.ToArray());
    //    var outF = new List<int>(outFcpp.ToArray());

    //    // create mesh
    //    var outMesh = new Mesh();

    //    foreach (var v in outV)
    //    {
    //      outMesh.Vertices.Add(v);
    //    }
    //    for (int i = 0; i < outF.Count; i += 3)
    //    {
    //      outMesh.Faces.AddFace(outF[i * 3], outF[i * 3 + 1], outF[i * 3 + 2]);
    //    }
    //    outMesh.Normals.ComputeNormals();
    //    outMesh.Compact();

    //    return outMesh;
    //  }
  }
}
