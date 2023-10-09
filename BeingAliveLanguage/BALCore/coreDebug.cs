using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace BeingAliveLanguage
{
  public static class DebugStore
  {
    public static List<double> num = new List<double>();
    public static List<Point3d> pt = new List<Point3d>();
    public static List<Vector3d> vec = new List<Vector3d>();
    public static List<Curve> crv = new List<Curve>();

    public static void Clear()
    {
      num.Clear();
      pt.Clear();
      vec.Clear();
      crv.Clear();
    }
  }

  public class BALdebug : GH_Component
  {
    public BALdebug()
        : base("balDebug", "balDebugComponent", "debugging component, default hidden.", "BAL", "99::debug")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.hidden;
    public override Guid ComponentGuid => new Guid("d24bc4b1-646b-4642-b684-d053f489e5e1");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddPointParameter("Num", "Numbers", "Number output for debug.", GH_ParamAccess.list);
      pManager.AddPointParameter("Pt", "Points", "Points output for debug.", GH_ParamAccess.list);
      pManager.AddPointParameter("Vec", "Vectors", "Vector output for debug.", GH_ParamAccess.list);
      pManager.AddPointParameter("Crv", "Curves", "Curve output for debug.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      DA.SetDataList(0, DebugStore.num);
      DA.SetDataList(1, DebugStore.pt);
      DA.SetDataList(2, DebugStore.vec);
      DA.SetDataList(3, DebugStore.crv);
    }
  }
}
