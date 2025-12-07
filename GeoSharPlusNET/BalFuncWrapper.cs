using System.Runtime.InteropServices;
using Rhino.Geometry;

// This file is kept for backward compatibility.
// New code should use GSP.Extensions.Bal.BalUtils instead.

namespace GSP
{
  /// <summary>
  /// Legacy BalFuncWrapper class. Use GSP.Extensions.Bal.BalUtils for new code.
  /// </summary>
  [System.Obsolete("Use GSP.Extensions.Bal.BalUtils instead")]
  public static class BalFuncWrapper
  {

    // Poisson Elimination Sampling
    public static void SampleElim(in List<Point3d> inPt, double generalArea, int num, out List<Point3d> outPt, int dim = 3)
    {
      outPt = new List<Point3d>();
      var buf = Wrapper.ToPointArrayBuffer(inPt.ToArray());
      var success = NativeBridge.BALpossionDiskElimSample(buf, buf.Length, generalArea, dim, num, out IntPtr outBuffer, out int outSize);

      if (!success)
      {
        return;
      }

      // Copy the result from unmanaged memory to a managed byte array
      var byteArray = new byte[outSize];
      Marshal.Copy(outBuffer, byteArray, 0, outSize);
      Marshal.FreeCoTaskMem(outBuffer); // Free the unmanaged memory

      outPt = Wrapper.FromPointArrayBuffer(byteArray).ToList();
    }

    // Poisson Elimination Sampling overload, given rectangle bound
    // the use of sample seed here is only for generating the initial points, elimination process is still random
    public static void SampleElim(in Rectangle3d bnd, int num, out List<Point3d> genPt, out List<Point3d> outPt, int seed = -1, double bndScale = 1.0, int initPtRange = 8, int dim = 2)
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

      SampleElim(genPt, curBnd.Area, num, out outPt, dim);
    }

  }
}
