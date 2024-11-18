using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeingAliveLanguage
{
  /// <summary>
  /// Base class to derive all organ components with similar procedure.
  /// </summary>
  public class BALorganBase : GH_Component
  {
    public BALorganBase(string name, string nickname, string description, string category, string subcategory)
      : base(name, nickname, description, "BAL", "04::organ")
    {
    }

    protected override System.Drawing.Bitmap Icon => Properties.Resources.balTree3D;
    public override Guid ComponentGuid => new Guid("b1a34eee-cb0f-4607-bf9f-037d65113be0");
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddIntegerParameter("Number", "num", "Number of the organ.", GH_ParamAccess.item, 3);
      pManager.AddIntegerParameter("Phase", "phase", "Phase of the organ.", GH_ParamAccess.item, 1);
      pManager.AddNumberParameter("Scale", "s", "Scale of the organ.", GH_ParamAccess.item, 1.0);
      pManager.AddBooleanParameter("Symmetric", "sym", "Symmetric or not.", GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddTextParameter("State", "state", "State of the organ (active or inactive).", GH_ParamAccess.item);
      pManager.AddCurveParameter("ExistingOrgan", "exiOrg", "Existing organs from current or previous years.", GH_ParamAccess.list);
      pManager.AddCurveParameter("NewOrgan", "newOrg", "New organs from the current year.", GH_ParamAccess.list);
      pManager.AddCurveParameter("Grassy Part", "grass", "Grassy part of the organ.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      int num = 3;
      int phase = 1;
      double scale = 1.0;
      bool sym = true;

      if (!DA.GetData("Number", ref phase))
      { return; }
      if (!DA.GetData("Phase", ref phase))
      { return; }
      if (!DA.GetData("Scale", ref scale))
      { return; }
      if (!DA.GetData("Symmetric", ref sym))
      { return; }

      // compute current states
      string curState = "active";
      curState = phase % 2 == 0 ? "inactive" : "active";
      DA.SetData("State", curState);
    }

  }

  public class BALorganTuft : BALorganBase
  {
    public BALorganTuft()
      : base("Organ_Tuft", "balOrganTuft", "Organ of resistance -- 'tuft'.", "BAL", "04::organ")
    {
    }

    protected override System.Drawing.Bitmap Icon => Properties.Resources.balTree3D;
    public override Guid ComponentGuid => new Guid("a7fdb09e-39e7-4ceb-a78f-b2b2ab71f572");
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      base.SolveInstance(DA);


    }
  }
}
