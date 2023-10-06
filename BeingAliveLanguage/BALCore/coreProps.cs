using System;
using System.Linq;
using System.Collections.Generic;
using Rhino.Geometry;
using Grasshopper.Kernel;
using System.Diagnostics;

namespace BeingAliveLanguage
{
  // for passing the soil base grid state
  public enum BaseGridState
  {
    NonScaledVertical, // default
    NonScaledHorizontal,
    ScaledVertical,
    ScaledHorizontal
  }

  /// <summary>
  /// The base information of initialized soil, used for soil/root computing.
  /// </summary>
  public struct SoilBase
  {
    public List<Polyline> soilT;
    public double unitL;
    public Plane pln;
    public Rectangle3d bnd;
    public BaseGridState gridState;
    public Transform trans;

    public SoilBase(Rectangle3d bound, Plane plane, List<Polyline> poly, double uL, BaseGridState isScaled = BaseGridState.NonScaledVertical, Transform trans = new Transform())
    {
      this.bnd = bound;
      this.pln = plane;
      this.soilT = poly;
      this.unitL = uL;
      this.gridState = isScaled;
      this.trans = trans;
    }
  }

  /// <summary>
  /// a basic soil info container.
  /// </summary>
  public struct SoilProperty
  {
    public double rSand;
    public double rSilt;
    public double rClay;

    public string soilType;
    public double fieldCapacity;
    public double wiltingPoint;
    public double saturation;


    public void setInfo(string st, double fc, double wp, double sa)
    {
      soilType = st;
      fieldCapacity = fc;
      wiltingPoint = wp;
      saturation = sa;
    }

    public void SetRatio(double sand, double silt, double clay)
    {
      rSand = sand;
      rSilt = silt;
      rClay = clay;
    }
  }

  /// <summary>
  /// a basic struct holding organic matter properties to draw top OM
  /// </summary>
  public struct OrganicMatterProperty
  {
    public SoilBase sBase;
    public Rectangle3d sBnd;
    //public double distDen; // control the gradiently changed density. Only for inner OM.
    public double dOM;

    //public OrganicMatterProperty(in SoilBase sBase, double distDen, double dOM)
    public OrganicMatterProperty(in SoilBase sBase, double dOM)
    {
      this.sBase = sBase;
      this.sBnd = sBase.bnd;
      //this.distDen = distDen;
      this.dOM = dOM;
    }
  }

  /// <summary>
  /// the base struct holding tree property info, supposed to be used by different components (tree root, etc.)
  /// </summary>
  public struct TreeProperty
  {
    public Plane pln;
    public double height;
    public int phase;

    public TreeProperty(in Plane plane, double h, int phase)
    {
      this.pln = plane;
      this.height = h;
      this.phase = phase;
    }
  }

  struct StoneCluster
  {
    public Point3d cen;
    public List<Polyline> T;
    //public Polyline bndCrv;
    public List<Polyline> bndCrvCol;
    public int typeId;

    public HashSet<string> strIdInside;
    public HashSet<string> strIdNeigh;
    public Dictionary<string, double> distMap; // store the distances of other pts to the current stone centre

    public Dictionary<string, (Point3d, Polyline)> ptMap;
    public Dictionary<string, HashSet<string>> nbMap;


    public StoneCluster(
        int id, in Point3d cenIn,
        ref Dictionary<string, (Point3d, Polyline)> ptMap,
        ref Dictionary<string, HashSet<string>> nbMap)
    {
      typeId = id;
      cen = cenIn;
      T = new List<Polyline>();
      //bndCrv = new Polyline();
      bndCrvCol = new List<Polyline>();

      strIdInside = new HashSet<string>();
      strIdNeigh = new HashSet<string>();
      distMap = new Dictionary<string, double>();

      this.ptMap = ptMap;
      this.nbMap = nbMap;

      var key = Utils.PtString(cenIn);

      strIdInside.Add(key);
      distMap.Add(key, cen.DistanceTo(cenIn));

      if (nbMap != null)
      {
        foreach (var it in nbMap[key])
        {
          strIdNeigh.Add(it);
          AddToDistMap(it, cen.DistanceTo(ptMap[it].Item1));
        }
      }
    }

    public void AddToDistMap(string id, double dist)
    {
      if (!distMap.ContainsKey(id))
        distMap.Add(id, dist);
      else
      {
        Debug.Assert(Math.Abs(distMap[id] - dist) < 1e-2);
        distMap[id] = dist;
      }
    }

    public void MakeBoolean()
    {
      if (strIdInside.Count != 0)
      {
        List<Curve> tmpCollection = new List<Curve>();
        foreach (var s in strIdInside)
        {
          var crv = ptMap[s].Item2.ToPolylineCurve();
          tmpCollection.Add(crv);
        }

        tmpCollection[0].TryGetPlane(out Plane pln);
        var boolRgn = Curve.CreateBooleanRegions(tmpCollection, pln, true, 0.5);
        var crvLst = boolRgn.RegionCurves(0);

        foreach (var cv in crvLst)
        {
          cv.TryGetPolyline(out Polyline tmpC);
          bndCrvCol.Add(tmpC);
        }
      }
    }

    public double GetAveRadius()
    {
      double sum = 0;
      foreach (var it in strIdNeigh)
      {
        sum += cen.DistanceTo(ptMap[it].Item1);
      }

      return sum / strIdNeigh.Count;
    }
  }


  /// <summary>
  /// Menu enhancement
  /// </summary>
  static class Menu
  {
    public static void SelectMode(GH_Component _this, object sender, EventArgs e, ref string _mode, string _setTo)
    {
      _mode = _setTo;
      _this.Message = _mode.ToUpper();
      _this.ExpireSolution(true);
    }
  }
}
