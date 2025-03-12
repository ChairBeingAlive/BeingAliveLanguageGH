using Grasshopper.Kernel;
using System;
using Rhino.Geometry;
using System.Collections.Generic;
using BeingAliveLanguage.BalCore;

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

    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTree3D);
    public override Guid ComponentGuid => new Guid("b1a34eee-cb0f-4607-bf9f-037d65113be0");
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    // variables
    protected int mNum;
    protected int mTotalNum;
    protected int mPhase;
    protected bool mActive;
    protected Plane mPln;
    protected double mScale;
    protected double mDistBelowSrf;

    // property
    protected virtual bool mSym { get; set; }
    protected virtual double mHorizontalScale { get; set; }
    protected virtual double mBelowSurfaceRatio { get; set; } // the ratio to radius

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddPlaneParameter("Plane", "pln", "Base plane to draw the organ.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Base Number", "num", "Number of the organ in the initial phase.", GH_ParamAccess.item, 3);
      pManager.AddIntegerParameter("Phase", "phase", "Phase of the organ.", GH_ParamAccess.item, 1);
      pManager.AddNumberParameter("Scale", "s", "Scale of the organ.", GH_ParamAccess.item, 1.0);
      //pManager.AddBooleanParameter("Symmetric", "sym", "Symmetric or not.", GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddTextParameter("State", "state", "State of the organ (active or inactive).", GH_ParamAccess.item);
      pManager.AddCurveParameter("ExistingOrgan", "exiOrg", "Existing organs from current or previous years.", GH_ParamAccess.list);
      pManager.AddCurveParameter("NewOrgan", "newOrg", "New organs from the current year.", GH_ParamAccess.list);
      pManager.AddLineParameter("ExistingGrassyPart", "exiGrass", "Existing grassy part of the organ.", GH_ParamAccess.list);
      pManager.AddLineParameter("NewGrassyPart", "newGrass", "Newly grown grassy part of the organ.", GH_ParamAccess.list);
      pManager.AddLineParameter("RootPart", "Root", "Root of the organ.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      // initialize with different values
      mNum = 3;
      mPhase = 1;
      mScale = 1.0;
      mPln = new Plane();

      if (!DA.GetData("Plane", ref mPln))
      { return; }
      if (!DA.GetData("Base Number", ref mNum))
      { return; }
      if (!DA.GetData("Phase", ref mPhase))
      { return; }
      if (!DA.GetData("Scale", ref mScale))
      { return; }

      // compute current states
      mActive = mPhase % 2 == 0;
      DA.SetData("State", mActive ? "active" : "inactive");

      // compute current total number of organs (based on symmetric or not)
      mTotalNum = mSym == true ? mNum + ((mPhase + 1) / 2 - 1) * 2 : mNum + ((mPhase + 1) / 2 - 1);
    }

    /// <summary>
    /// Function to draw the root or grass part of an organ
    /// </summary>
    /// <param name="pivot"></param>
    /// <param name="dir"></param>
    /// <param name="num"></param>
    /// <param name="proximateLen"></param>
    /// <param name="openingAngle"></param>
    /// <returns></returns>
    protected List<Line> DrawGrassOrRoot(Point3d pivot, Vector3d dir,
                                        int num = 2, double scale = 1, double proximateLen = 10, double openingAngle = 10)
    {
      var res = new List<Line>();

      double[] lengths = { proximateLen * 0.9 * scale, proximateLen * 1.1 * scale, proximateLen * 1.5 * scale };
      double[] angleVariations = { -openingAngle, openingAngle, 0 };

      // draw the grass/root part. Notice the sequence when constructing the param above
      for (int i = 0; i < num; i++)
      {
        double length = lengths[i];
        double angleVariation = angleVariations[i];
        var direction = dir;
        direction.Rotate(angleVariation * Math.PI / 180, mPln.ZAxis);
        var endPt = pivot + direction * length;
        res.Add(new Line(pivot, endPt));
      }

      return res;
    }
  }


  public class BALorganTuft : BALorganBase
  {
    public BALorganTuft()
      : base("Organ_Tuft", "balOrganTuft", "Organ of resistance -- 'tuft'.", "BAL", "04::organ")
    {
    }

    public BALorganTuft(string name, string nickname, string description, string category, string subcategory) : base(name, nickname, description, category, subcategory)
    {
    }

    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTree3D);
    public override Guid ComponentGuid => new Guid("a7fdb09e-39e7-4ceb-a78f-b2b2ab71f572");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    // Symmetry, scaling properties
    protected override bool mSym => true;
    protected override double mHorizontalScale => 0.5;
    protected override double mBelowSurfaceRatio => 1;

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      base.SolveInstance(DA);

      if (mNum < 1)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Base number should be at least 1.");
      }

      double radius = 1;
      var circle = new Circle(mPln, radius);
      var horizontalSpacing = mHorizontalScale * mScale * 2; // radius = 1, D = 2

      var xform = Transform.Scale(mPln, mHorizontalScale * mScale, 1 * mScale, 1 * mScale);
      var geo = circle.ToNurbsCurve();
      geo.Domain = new Interval(0, 1);

      geo.Transform(xform);
      geo.Translate(-mPln.YAxis * radius * mScale * mBelowSurfaceRatio);

      var geoCol = new List<NurbsCurve>() { geo };
      var exiOrganLst = new List<NurbsCurve>();
      var newOrganLst = new List<NurbsCurve>();
      var exiGrassLst = new List<Line>();
      var newGrassLst = new List<Line>();
      var rootLst = new List<Line>();


      if (mSym)
      {
        if (mNum % 2 == 0)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "When `sym==TRUE`, even count will be rounded to the nearest odd number.");
          return;
        }

        // Core Organ part
        for (int i = 0; i < mTotalNum / 2; i++)
        {
          var newGeo = geo.Duplicate() as NurbsCurve;
          if (newGeo == null)
          {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Internal NURBS casting error.");
          }
          newGeo.Translate(horizontalSpacing * (i + 1), 0, 0);
          geoCol.Add(newGeo);

          var newGeoMirror = geo.Duplicate() as NurbsCurve;
          newGeoMirror.Translate(-horizontalSpacing * (i + 1), 0, 0);
          geoCol.Add(newGeoMirror);
        }

        if (mActive)
        {
          exiOrganLst = geoCol.GetRange(0, geoCol.Count - 2);
          newOrganLst = geoCol.GetRange(geoCol.Count - 2, 2);
        }
        else
        {
          exiOrganLst = geoCol;
        }

      }
      else
      {
        for (int i = 0; i < mTotalNum; i++)
        {
          var newGeo = geo.Duplicate() as NurbsCurve;
          if (newGeo == null)
          {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Internal NURBS casting error.");
          }
          newGeo.Translate(horizontalSpacing * i, 0, 0);
          geoCol.Add(newGeo);
        }

        // no symmetry, only take 1 element as "new"
        if (mActive)
        {
          exiOrganLst = geoCol.GetRange(0, geoCol.Count - 1);
          newOrganLst = geoCol.GetRange(geoCol.Count - 1, 1);
        }
        else
        {
          exiOrganLst = geoCol;
        }
      }

      // Global root/Grass build based on active state
      if (mActive)
      {
        // Existing organ: long grass, with roots
        foreach (var crv in exiOrganLst)
        {
          var topPt = crv.PointAt(0.25);
          var grassL = DrawGrassOrRoot(topPt, mPln.YAxis, 2, mScale, radius * 10, 5);
          exiGrassLst.AddRange(grassL);

          // root part (active): only on existing organs
          var botPt = crv.PointAt(0.75);
          var rootL = DrawGrassOrRoot(botPt, -mPln.YAxis, 3, mScale, radius * 3);
          rootLst.AddRange(rootL);
        }

        // New organ: short grass, no roots
        foreach (var crv in newOrganLst)
        {
          var topPt = crv.PointAt(0.25);
          var grassL = DrawGrassOrRoot(topPt, mPln.YAxis, 2, mScale, radius * 2, 15);
          newGrassLst.AddRange(grassL);
        }
      }
      else
      {
        // root part (inactive): on all organs
        foreach (var crv in exiOrganLst)
        {
          var botPt = crv.PointAt(0.75);
          var grassL = DrawGrassOrRoot(botPt, -mPln.YAxis, 3, mScale, radius * 3.5);
          newGrassLst.AddRange(grassL);
        }

      }

      DA.SetDataList("ExistingOrgan", exiOrganLst);
      DA.SetDataList("NewOrgan", newOrganLst);
      DA.SetDataList("ExistingGrassyPart", exiGrassLst);
      DA.SetDataList("NewGrassyPart", newGrassLst);
      DA.SetDataList("RootPart", rootLst);

    }
  }

  public class BALorganRhizome : BALorganTuft
  {
    public BALorganRhizome()
      : base("Organ_Rhizome", "balOrganRhizome", "Organ of resistance -- 'rhizome.", "BAL", "04::organ")
    {
    }

    protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.balTree3D);
    public override Guid ComponentGuid => new Guid("50264c56-b65f-4181-a49e-25ad9815771d");

    protected override bool mSym => false;
    protected override double mHorizontalScale => 1.2;
    protected override double mBelowSurfaceRatio => 2; // the ratio to radius

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      base.SolveInstance(DA);
    }

  }


}
