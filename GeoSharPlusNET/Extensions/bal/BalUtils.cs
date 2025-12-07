using System;
using System.Collections.Generic;
using GSP.Adapters.Rhino;
using GSP.Core;
using Rhino.Geometry;

namespace GSP.Extensions.Bal {
  /// <summary>
  /// High-level utility functions for BAL (BeingAliveLanguage) operations.
  /// These functions wrap the low-level P/Invoke calls with Rhino type conversions.
  /// </summary>
  public static class BalUtils {
    /// <summary>
    /// Perform Poisson disk elimination sampling on a list of points.
    /// </summary>
    /// <param name="inPt">Input point cloud</param>
    /// <param name="generalArea">General area for sampling density calculation</param>
    /// <param name="num">Target number of output points</param>
    /// <param name="outPt">Output sampled points</param>
    /// <param name="dim">Dimension (2 or 3, default 3)</param>
    public static void SampleElim(
        in List<Point3d> inPt, double generalArea, int num,
        out List<Point3d> outPt, int dim = 3) {
      outPt = new List<Point3d>();

      var buf = RhinoAdapter.ToBuffer(inPt.ToArray());
      var success = BalBridge.PoissonDiskElimSample(
          buf, buf.Length, generalArea, dim, num, out IntPtr outBuffer, out int outSize);

      if (!success) {
        return;
      }

      // Copy result and free unmanaged memory
      var byteArray = MarshalHelper.CopyAndFree(outBuffer, outSize);
      outPt = RhinoAdapter.PointArrayFromBuffer(byteArray).ToList();
    }

    /// <summary>
    /// Perform Poisson disk elimination sampling given a rectangle boundary.
    /// The seed parameter is only for generating initial points; elimination is still random.
    /// </summary>
    /// <param name="bnd">Rectangle boundary</param>
    /// <param name="num">Target number of output points</param>
    /// <param name="genPt">Generated initial points (before elimination)</param>
    /// <param name="outPt">Output sampled points</param>
    /// <param name="seed">Random seed (-1 for random)</param>
    /// <param name="bndScale">Boundary scale factor</param>
    /// <param name="initPtRange">Initial point multiplier</param>
    /// <param name="dim">Dimension (2 or 3, default 2)</param>
    public static void SampleElim(
        in Rectangle3d bnd, int num,
        out List<Point3d> genPt, out List<Point3d> outPt,
        int seed = -1, double bndScale = 1.0, int initPtRange = 8, int dim = 2) {
      var rnd = seed >= 0 ? new Random(seed) : new Random(Guid.NewGuid().GetHashCode());

      var toLocal = Transform.ChangeBasis(Plane.WorldXY, bnd.Plane);
      var toWorld = Transform.ChangeBasis(bnd.Plane, Plane.WorldXY);

      var curBnd = bnd;
      // Transform "scale" into "offset" to have equal distance in borders
      if (bndScale != 1.0) {
        curBnd.Transform(toWorld);
        var w = (curBnd.Corner(1) - curBnd.Corner(0)).Length;
        var h = (curBnd.Corner(3) - curBnd.Corner(0)).Length;

        double wScale = (w - h * (1 - bndScale)) / w;

        var tmpPln = Plane.WorldXY;
        tmpPln.Translate(curBnd.Center - Plane.WorldXY.Origin);
        curBnd.Transform(Transform.Scale(tmpPln, wScale, bndScale, 1));

        curBnd.Transform(toLocal);
      }

      // Generate random points
      var lowerLeft = curBnd.Corner(0);
      var diagVec = curBnd.Corner(2) - curBnd.Corner(0);

      genPt = new List<Point3d>();
      for (int i = 0; i < num * initPtRange * 2; i++) {
        genPt.Add(new Point3d(
            rnd.NextDouble() * diagVec.X,
            rnd.NextDouble() * diagVec.Y,
            rnd.NextDouble() * diagVec.Z) + lowerLeft);
      }

      SampleElim(genPt, curBnd.Area, num, out outPt, dim);
    }
  }
}
