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
        public static Polyline CvtCrvToPoly(in Curve c)
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

        // get a signed angle value from two vectors given a normal vector
        public static Func<Vector3d, Vector3d, Vector3d, double> SignedVecAngle = (v0, v1, vN) =>
        {
            v0.Unitize();
            v1.Unitize();
            var dotValue = v0 * v1 * 0.9999999; // tolerance issue
            var angle = ToDegree(Math.Acos(dotValue));
            var crossValue = Vector3d.CrossProduct(v0, v1);

            return angle * (crossValue * vN < 0 ? -1 : 1);
        };

        // get the two vector defining the facing cone between a testing point and a curve
        public static (Vector3d, Vector3d) GetPtCrvFacingVector(in Point3d pt, in Plane P, Curve crv, int N = 100)
        {
            var ptList = crv.DivideByCount(N, true).Select(t => crv.PointAt(t)).ToList();

            // get centroid
            var cen = new Point3d(0, 0, 0);
            foreach (var p in ptList)
            {
                cen += p;
            }
            cen /= ptList.Count;

            var refVec = cen - pt;
            var sortingDict = new SortedDictionary<double, Point3d>();

            foreach (var (p, i) in ptList.Select((p, i) => (p, i)))
            {
                var curVec = pt - p;
                var key = SignedVecAngle(refVec, curVec, P.ZAxis);
                if (!sortingDict.ContainsKey(key))
                    sortingDict.Add(key, p);
            }
            var v0 = sortingDict.First().Value - pt;
            var v1 = sortingDict.Last().Value - pt;
            v0.Unitize();
            v1.Unitize();

            return (v0, v1);
        }


        // ! Climate Related

        // hard-coded ETP correction factor, new data can be interpolated from the chart
        static readonly Dictionary<int, List<double>> correctionFactorETP =
            new Dictionary<int, List<double>>() {
                {  0, new List<double>{ 1.04, 0.94, 1.04, 1.01, 1.04, 1.01, 1.04, 1.01, 1.01, 1.04, 1.01, 1.04 } },
                { 27, new List<double>{ 0.92, 0.88, 1.03, 1.07, 1.16, 1.15, 1.18, 1.13, 1.02, 0.99, 0.90, 0.90 } },
                { 28, new List<double>{ 0.91, 0.88, 1.03, 1.07, 1.16, 1.16, 1.18, 1.13, 1.02, 0.98, 0.90, 0.90 } },
                { 29, new List<double>{ 0.91, 0.87, 1.03, 1.07, 1.17, 1.16, 1.19, 1.13, 1.03, 0.98, 0.90, 0.89 } },
                { 30, new List<double>{ 0.90, 0.87, 1.03, 1.08, 1.18, 1.17, 1.20, 1.14, 1.03, 0.98, 0.89, 0.88 } },
                { 35, new List<double>{ 0.87, 0.85, 1.03, 1.09, 1.21, 1.21, 1.23, 1.16, 1.03, 0.97, 0.86, 0.85 } },
                { 36, new List<double>{ 0.87, 0.85, 1.03, 1.10, 1.21, 1.22, 1.24, 1.16, 1.03, 0.97, 0.86, 0.84 } },
                { 37, new List<double>{ 0.86, 0.84, 1.03, 1.10, 1.22, 1.23, 1.25, 1.17, 1.03, 0.97, 0.85, 0.83 } },
                { 38, new List<double>{ 0.85, 0.84, 1.03, 1.10, 1.23, 1.24, 1.25, 1.17, 1.04, 0.96, 0.84, 0.83 } },
                { 39, new List<double>{ 0.85, 0.84, 1.03, 1.11, 1.23, 1.24, 1.26, 1.18, 1.04, 0.96, 0.84, 0.82 } },
                { 40, new List<double>{ 0.84, 0.83, 1.03, 1.11, 1.24, 1.25, 1.27, 1.18, 1.04, 0.96, 0.83, 0.81 } },
                { 41, new List<double>{ 0.83, 0.83, 1.03, 1.11, 1.25, 1.26, 1.27, 1.19, 1.04, 0.96, 0.82, 0.80 } },
                { 42, new List<double>{ 0.82, 0.83, 1.03, 1.12, 1.26, 1.27, 1.28, 1.19, 1.04, 0.95, 0.82, 0.79 } },
                { 43, new List<double>{ 0.81, 0.82, 1.02, 1.12, 1.26, 1.28, 1.29, 1.20, 1.04, 0.95, 0.81, 0.77 } },
                { 44, new List<double>{ 0.81, 0.82, 1.02, 1.13, 1.27, 1.29, 1.30, 1.20, 1.04, 0.95, 0.80, 0.76 } },
                };

        public static List<double> GetCorrectionFactorETP(double lat)
        {
            //int latInt = (Math.Abs(lat - (int)lat) < 1e-5)  
            int lBound = correctionFactorETP.Keys.Where(x => x <= lat).Max(); // min = 5
            int uBound = correctionFactorETP.Keys.Where(x => x >= lat).Min(); // max = 7

            if (lBound == uBound)
                return correctionFactorETP[lBound].ToList();

            var factorL = new List<double>();
            for (int i = 0; i < 12; i++)
            {
                var dat = correctionFactorETP[lBound][i] +
                    (lat - lBound) / (uBound - lBound) * (correctionFactorETP[uBound][i] - correctionFactorETP[lBound][i]);
                factorL.Add(dat);
            }

            return factorL;
        }
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
