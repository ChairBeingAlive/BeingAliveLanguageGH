using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rhino.Geometry;

namespace BALcontract
{

    /// <summary>
    /// a basic soil info container used for both coreLib and ghLib
    /// </summary>
    public struct SoilProperty
    {
        public string soilType;
        public double fieldCapacity;
        public double wiltingPoint;
        public double saturation;

        public SoilProperty(string st, double fc, double wp, double sa)
        {
            soilType = st;
            fieldCapacity = fc;
            wiltingPoint = wp;
            saturation = sa;
        }
    }

    /// <summary>
    /// a basic struct holding organic matter properties to draw top OM
    /// </summary>
    public struct OrganicMatterProperty
    {
        public Rectangle3d bnd;
        public double distDen;
        public double omDen;
        public double uL;

        public OrganicMatterProperty(in Rectangle3d bound, double dDen, double dOM, double unitL)
        {
            bnd = bound;
            distDen = dDen;
            omDen = dOM;
            uL = unitL;
        }

    }

    public static class Utils
    {
        // convert the "Curve" type taken in by GH to a Rhino.Geometry.Polyline
        public static Polyline CvtCrvToTriangle(in Curve c)
        {
            if (c.TryGetPolyline(out Polyline tmp) && tmp.IsClosed)
                return tmp;
            else
                return null;
        }

        public static string PtString(in Point3d pt, int dec = 3)
        {
            var tmpPt = pt * Math.Pow(10, dec);
            return $"{tmpPt[0]:F0} {tmpPt[1].ToString("F0")} {tmpPt[2].ToString("F0")}";
        }

        // radian <=> degree
        public static double ToDegree(double x) => x / Math.PI * 180;
        public static double ToRadian(double x) => x * Math.PI / 180;
    }


    public interface IPlugin
    {
        // make base triangle map
        (double, List<List<PolylineCurve>>) MakeTriMap(ref Rectangle3d rec, int re);

        // subdiv triangle into different content
        (List<Polyline>, List<Polyline>, List<Polyline>, SoilProperty) DivBaseMap(
            in List<Polyline> triL, in double[] ratio, in List<Curve> rock);

        // offset triangle based on soil property
        (List<Polyline>, List<Polyline>, List<Polyline>, List<Polyline>, List<List<Polyline>>, List<List<Polyline>>)
            OffsetWater(in List<Curve> tri, SoilProperty sType, double rWater, int denEmbedWater, int denAvailWater);

        // convert soil properties into text format
        string SoilText(SoilProperty sProperty);

        // generate soil inner organic matter
        (List<List<Line>>, OrganicMatterProperty) GenOrganicMatterInner(in Rectangle3d bnd, in SoilProperty sInfo, in List<Curve> tri, double dOM);

        // generate soil inner organic matter
        List<List<Line>> GenOrganicMatterTop(in Rectangle3d bnd, double uL, int type, double den, int layer);

        // alternative approach to generate top organic matter
        List<List<Line>> GenOrganicMatterTop(in OrganicMatterProperty omP, int type, int layer);
    }
}
