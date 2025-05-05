using Rhino.Geometry;
using System.Collections.Generic;
using GSP;

namespace BeingAliveLanguage
{

  static class cppUtils
  {
    public static void SampleElim(in List<Point3d> inPt, double generalArea, int dim, int num, out List<Point3d> outPt)
    {
      outPt = new List<Point3d>();
      BalFuncWrapper.SampleElim(inPt, generalArea, num, out outPt);
    }


    public static void SampleElim(in Rectangle3d bnd, int num, out List<Point3d> genPt, out List<Point3d> outPt, int seed = -1, double bndScale = 1.0, int initPtRange = 8)
    {

      genPt = new List<Point3d>();
      outPt = new List<Point3d>();
      BalFuncWrapper.SampleElim(bnd, num, out genPt, out outPt, seed, bndScale, initPtRange);
    }
  }

}