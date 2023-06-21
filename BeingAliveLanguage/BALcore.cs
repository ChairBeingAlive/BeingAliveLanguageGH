using System;
using System.Linq;
using System.Collections.Generic;
using Rhino.Geometry;
using KdTree;
using System.Collections.Concurrent;
using MathNet.Numerics.Distributions;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using System.Diagnostics;
using Rhino.Geometry.Intersect;
using System.Runtime.InteropServices;
using System.Threading;
using Grasshopper.GUI;
using System.Windows.Forms.Layout;

namespace BeingAliveLanguage
{
    /// <summary>
    /// The base information of initialized soil, used for soil/root computing.
    /// </summary>
    public struct SoilBase
    {
        public List<Polyline> soilT;
        public double unitL;
        public Rhino.Geometry.Plane pln;
        public Rectangle3d bnd;

        public SoilBase(Rectangle3d bound, Rhino.Geometry.Plane plane, List<Polyline> poly, double uL)
        {
            this.bnd = bound;
            this.pln = plane;
            this.soilT = poly;
            this.unitL = uL;
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
        public Rectangle3d bnd;
        public double distDen; // control the gradiently changed density. Only for inner OM.
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

    /// <summary>
    /// Utility Class, containing all funcs that needed by the BAL system
    /// </summary>
    static class Utils
    {
        // get the random core
        public static Random balRnd = new Random(Guid.NewGuid().GetHashCode());

        // remap a number from one range to another
        public static double remap(double val, double originMin, double originMax, double targetMin, double targetMax)
        {
            // of original range is 0 length, return 0
            if (originMax - originMin < 1e-5)
            { return 0; }

            // numerical issue
            if (Math.Abs(val - originMin) < 1e-5)
                return targetMin;

            if (Math.Abs(val - originMax) < 1e-5)
                return targetMax;

            return targetMin + (val - originMin) / (originMax - originMin) * (targetMax - targetMin);
        }

        // create a range of values with step size
        public static IEnumerable<double> Range(double min, double max, int nStep)
        {
            double sz = (max - min) / nStep;
            double i;
            for (i = min; i <= max * 1.02; i += sz)
                yield return Math.Min(i, max);

            //if (i - max > sz * 0.01) // added only because you want max to be returned as last item
            //    yield return max;
        }

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

        public static List<T> Rotate<T>(this List<T> list, int offset)
        {
            return list.Skip(offset).Concat(list.Take(offset)).ToList();
        }


        // ! Climate Related
        // hard-coded ETP correction factor, new data can be interpolated from the chart
        static readonly Dictionary<int, List<double>> correctionFactorPET =
            new Dictionary<int, List<double>>() {
                // north hemisphere
                {  0, new List<double>{ 1.04, 0.94, 1.04, 1.01, 1.04, 1.01, 1.04, 1.04, 1.01, 1.04, 1.01, 1.04 } },
                {  5, new List<double>{ 1.02, 0.93, 1.03, 1.02, 1.06, 1.03, 1.06, 1.05, 1.01, 1.03, 0.99, 1.02 } },
                { 10, new List<double>{ 1.00, 0.91, 1.03, 1.03, 1.08, 1.06, 1.08, 1.07, 1.02, 1.02, 0.98, 0.99 } },
                { 15, new List<double>{ 0.97, 0.91, 1.03, 1.04, 1.11, 1.08, 1.12, 1.08, 1.02, 1.01, 0.95, 0.97 } },
                { 20, new List<double>{ 0.95, 0.90, 1.03, 1.05, 1.13, 1.11, 1.14, 1.11, 1.02, 1.00, 0.93, 0.94 } },
                { 25, new List<double>{ 0.93, 0.89, 1.03, 1.06, 1.15, 1.14, 1.17, 1.12, 1.02, 0.99, 0.91, 0.91 } },
                { 26, new List<double>{ 0.92, 0.88, 1.03, 1.06, 1.15, 1.15, 1.17, 1.12, 1.02, 0.99, 0.91, 0.91 } },
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
                { 45, new List<double>{ 0.80, 0.81, 1.02, 1.13, 1.28 ,1.29 ,1.31 ,1.21, 1.04, 0.94, 0.79, 0.75 } },
                { 46, new List<double>{ 0.79, 0.81, 1.02, 1.13, 1.29, 1.31, 1.32, 1.22, 1.04, 0.94, 0.79, 0.74 } },
                { 47, new List<double>{ 0.77, 0.80, 1.02, 1.14, 1.30, 1.32, 1.33, 1.22, 1.04, 0.93, 0.78, 0.73 } },
                { 48, new List<double>{ 0.76, 0.80, 1.02, 1.14, 1.31, 1.33, 1.34, 1.23, 1.05, 0.93, 0.77, 0.72 } },
                { 49, new List<double>{ 0.75, 0.79, 1.02, 1.14, 1.32, 1.34, 1.35, 1.24, 1.05, 0.93, 0.76, 0.71 } },
                { 50, new List<double>{ 0.74, 0.78, 1.02, 1.15, 1.33, 1.36, 1.37, 1.25, 1.06, 0.92, 0.76, 0.70 } },


                // south hemisphere (use negative deg)
                {-5,  new List<double>{1.06, 0.95, 1.04, 1.00, 1.02, 0.99, 1.02, 1.03, 1.00, 1.05, 1.03, 1.06}},
                {-10, new List<double>{1.08, 0.97, 1.05, 0.99, 1.01, 0.96, 1.00, 1.01, 1.00, 1.06, 1.05, 1.10}},
                {-15, new List<double>{1.12, 0.98, 1.05, 0.98, 0.98, 0.94, 0.97, 1.00, 1.00, 1.07, 1.07, 1.12}},
                {-20, new List<double>{1.14, 1.00, 1.05, 0.97, 0.96, 0.91, 0.95, 0.99, 1.00, 1.08, 1.09, 1.15}},
                {-25, new List<double>{1.17, 1.01, 1.05, 0.96, 0.94, 0.88, 0.93, 0.98, 1.00, 1.10, 1.11, 1.18}},
                {-30, new List<double>{1.20, 1.03, 1.06, 0.95, 0.92, 0.85, 0.90, 0.96, 1.00, 1.12, 1.14, 1.21}},
                {-35, new List<double>{1.23, 1.04, 1.06, 0.94, 0.89, 0.82, 0.87, 0.94, 1.00, 1.13, 1.17, 1.25}},
                {-40, new List<double>{1.27, 1.06, 1.07, 0.93, 0.86, 0.78, 0.84, 0.92, 1.00, 1.15, 1.20, 1.29}},
                {-42, new List<double>{1.28, 1.07, 1.07, 0.92, 0.85, 0.76, 0.82, 0.92, 1.00, 1.16, 1.22, 1.31}},
                {-44, new List<double>{1.30, 1.08, 1.07, 0.92, 0.83, 0.74, 0.81, 0.91, 0.99, 1.17, 1.23, 1.33}},
                {-46, new List<double>{1.32, 1.10, 1.07, 0.91, 0.82, 0.72, 0.79, 0.90, 0.99, 1.17, 1.25, 1.35}},
                {-48, new List<double>{1.34, 1.11, 1.08, 0.90, 0.80, 0.70, 0.76, 0.89, 0.99, 1.18, 1.27, 1.37}},
                {-50, new List<double>{1.37, 1.12, 1.08, 0.89, 0.77, 0.67, 0.74, 0.88, 0.99, 1.19, 1.29, 1.41}},
                };


        public static List<double> GetCorrectionFactorPET(double lat)
        {
            // for lat larger than 50 or smaller than -50, use the +/-50 value.
            lat = lat > 50 ? 50 : lat;
            lat = lat < -50 ? -50 : lat;

            // find upper/lower bound for interpolation
            int lBound = correctionFactorPET.Keys.Where(x => x <= lat).Max();
            int uBound = correctionFactorPET.Keys.Where(x => x >= lat).Min();

            if (lBound == uBound)
                return correctionFactorPET[lBound].ToList();

            var factorL = new List<double>();
            for (int i = 0; i < 12; i++)
            {
                var dat = correctionFactorPET[lBound][i] +
                    (lat - lBound) / (uBound - lBound) * (correctionFactorPET[uBound][i] - correctionFactorPET[lBound][i]);
                factorL.Add(dat);
            }

            return factorL;
        }


        // compute transformation between local coordinates and world coordinates
        public static (Transform, Transform) GetTransformation(in Plane localPln, in Plane worldPln)
        {
            var toLocal = Transform.ChangeBasis(worldPln, localPln);
            var toWorld = Transform.ChangeBasis(localPln, worldPln);
            return (toLocal, toWorld);
        }
    }

    class BalCore
    {
        //  create a position vector from given 2D coordinates in a plane.
        private static readonly Func<Plane, double, double, Vector3d> createVec = (pln, x, y) =>
            pln.XAxis * x + pln.YAxis * y;

        // create a triangle polyline from a set of position vectors.
        private static readonly Func<Point3d, List<Vector3d>, Polyline> createTri = (cen, vecs) =>
        {
            Polyline ply = new Polyline(4);
            foreach (var v in vecs)
            {
                ply.Add(Point3d.Add(cen, v));
            }
            ply.Add(ply[0]);

            return ply;
        };

        // align the triangles on the border with vertical boundary.
        // associate with the triUp/triDown point order. type: 0 - startTri, 1 - endTri.
        private static void AlignTri(ref Polyline tri, in Plane pln, int type = 0)
        {
            // moveV is different for triUP / triDown
            bool isTriUp = Math.Abs(Vector3d.Multiply(tri[1] - tri[0], pln.YAxis)) < 1e-5;

            var moveV = 0.5 * (isTriUp ? tri[1] - tri[0] : tri[2] - tri[0]);

            if (type == 0)
            {
                tri[0] += moveV;
                tri[3] += moveV;
            }

            if (type == 1)
            {
                if (isTriUp)
                    tri[1] -= moveV;
                else // triDown
                    tri[2] -= moveV;
            }
        }

        // create a list of triangles from the starting point. Used to generate one row of the given bound
        private static List<PolylineCurve> CreateTriLst(in Point3d pt, in Plane pln, in Vector3d dirVec, int num, int type, in List<List<Vector3d>> triType)
        {
            List<PolylineCurve> triLst = new List<PolylineCurve>();

            for (int i = 0; i < num; i++)
            {
                var tmpPt = Point3d.Add(pt, dirVec / 2 * i);
                var triTypeIdx = (type + i % 2) % 2;
                var triPolyline = createTri(tmpPt, triType[triTypeIdx]);

                // modify the beginning and end triangle so that the border aligns
                if (i == 0)
                    AlignTri(ref triPolyline, in pln, 0);
                if (i == num - 1)
                    AlignTri(ref triPolyline, in pln, 1);


                triLst.Add(triPolyline.ToPolylineCurve());
            }

            return triLst;
        }

        /// MainFunc: make a triMap from given rectangle boundary.
        /// </summary>
        public static (double, List<List<PolylineCurve>>) MakeTriMap(ref Rectangle3d rec, int resolution, string resMode = "vertical", bool gridScale = false)
        {
            // basic param
            var pln = rec.Plane;
            var refPln = rec.Plane.Clone();

            // move plane to the starting corner of the rectangle
            var refPt = rec.Corner(0);
            refPln.Translate(refPt - rec.Plane.Origin);

            double hTri = 1.0;
            double sTri = 1.0;
            int nHorizontal = 1;
            int nVertical = 1;

            // make sure recW > recH
            //double recH = rec.Width < rec.Height ? rec.Width : rec.Height;
            //double recW = rec.Width < rec.Height ? rec.Height : rec.Width;
            double recH = rec.Height;
            double recW = rec.Width;

            if (resMode == "vertical")
            {
                hTri = recH / resolution; // height of base triangle
                sTri = hTri * 2 * Math.Sqrt(3.0) / 3; // side length of base triangle

                nHorizontal = (int)(recW / sTri * 2);
                nVertical = resolution;
            }
            else if (resMode == "horizontal")
            {
                sTri = recW / resolution;
                hTri = sTri / 2.0 * Math.Sqrt(3.0);

                nHorizontal = resolution * 2;
                nVertical = (int)(recH / hTri);
            }

            // up-triangle's three position vector from bottom left corner
            var vTop = createVec(refPln, sTri / 2, hTri);
            var vForward = createVec(refPln, sTri, 0);
            List<Vector3d> triUp = new List<Vector3d> { createVec(refPln, 0, 0), vForward, vTop };

            // down-triangle's three position vector from top left corner
            var vTopLeft = createVec(refPln, 0, hTri);
            var vTopRight = createVec(refPln, sTri, hTri);
            List<Vector3d> triDown = new List<Vector3d> { vTopLeft, vForward / 2, vTopRight };

            // collection of the two types
            List<List<Vector3d>> triType = new List<List<Vector3d>> { triUp, triDown };

            // start making triGrid
            List<List<PolylineCurve>> gridMap = new List<List<PolylineCurve>>();
            for (int i = 0; i < nVertical; i++)
            {
                var pt = Point3d.Add(refPt, vTopLeft * i);
                pt = Point3d.Add(pt, -0.5 * sTri * refPln.XAxis); // compensate for the alignment
                gridMap.Add(CreateTriLst(in pt, in refPln, vForward, nHorizontal + 1, i % 2, in triType));
            }

            // scale if needed

            if (gridScale)
            {
                Transform sca = new Transform();
                // build transformation
                if (resMode == "vertical")
                {
                    // scale horizontal
                    sca = Transform.Scale(refPln, recW / (sTri * nHorizontal * 0.5), 1, 1);
                }
                else if (resMode == "horizontal")
                {
                    // scle vertical
                    sca = Transform.Scale(refPln, 1, recH / (hTri * nVertical), 1);
                }


                // scale the triangles
                foreach (var lst in gridMap)
                {
                    foreach (var tri in lst)
                    {
                        tri.Transform(sca);
                    }
                }
            }

            return (sTri, gridMap);
        }


        // lambda func to compute triangle area using Heron's Formula
        public static readonly Func<Polyline, double> triArea = poly =>
        {
            var dA = poly[1].DistanceTo(poly[2]);
            var dB = poly[2].DistanceTo(poly[0]);
            var dC = poly[0].DistanceTo(poly[1]);
            var p = (dA + dB + dC) * 0.5;
            return Math.Sqrt(p * (p - dA) * (p - dB) * (p - dC));
        };

        // compute the soil type and water ratio
        public static readonly Func<double, double, double, SoilProperty> SoilType = (rSand, rSilt, rClay) =>
        {
            var sPro = new SoilProperty();
            sPro.SetRatio(rSand, rSilt, rClay);

            bool isSand = (rClay <= 0.1 && rSand > 0.5 * rClay + 0.85);
            // for loamy sand, use the upper inclined line of loamy sand and exclude the sand part
            bool isLoamySand = (rClay <= 0.15 && rSand > rClay + 0.7) && (!isSand);

            if (rClay > 0.4 && rSand <= 0.45 && rSilt <= 0.4)
                sPro.setInfo("clay", 0.42, 0.30, 0.5);

            else if (rClay > 0.35 && rSand > 0.45)
                sPro.setInfo("sandy clay", 0.36, 0.25, 0.44);

            else if (rClay > 0.4 && rSilt > 0.4)
                sPro.setInfo("silty clay", 0.41, 0.27, 0.52);

            else if (rClay > 0.27 && rClay <= 0.4 && rSand > 0.2 && rSand <= 0.45)
                sPro.setInfo("clay loam", 0.36, 0.22, 0.48);

            else if (rClay > 0.27 && rClay <= 0.4 && rSand <= 0.2)
                sPro.setInfo("silty clay loam", 0.38, 0.22, 0.51);

            else if (rClay > 0.2 && rClay <= 0.35 && rSand > 0.45 && rSilt <= 0.28)
                sPro.setInfo("sandy clay loam", 0.27, 0.17, 0.43);

            else if (rClay > 0.07 && rClay <= 0.27 && rSand <= 0.53 && rSilt > 0.28 && rSilt <= 0.5)
                sPro.setInfo("loam", 0.28, 0.14, 0.46);

            else if (rClay <= 0.27 && ((rSilt > 0.5 && rSilt <= 0.8) || (rSilt > 0.8 && rClay > 0.14)))
                sPro.setInfo("silt loam", 0.31, 0.11, 0.48);

            else if (rClay <= 0.14 && rSilt > 0.8)
                sPro.setInfo("silt", 0.3, 0.06, 0.48);

            // three special cases for conditioning
            else if (isSand)
                sPro.setInfo("sand", 0.1, 0.05, 0.46);

            else if (isLoamySand)
                sPro.setInfo("loamy sand", 0.18, 0.08, 0.45);

            else if (((!isLoamySand) && rClay <= 0.2 && rSand > 0.53) || (rClay <= 0.07 && rSand > 0.53 && rSilt <= 0.5))
                sPro.setInfo("sandy loam", 0.18, 0.08, 0.45);

            else // default check
                sPro.setInfo("errorSoil", 0, 0, 0);

            return sPro;
        };

        // subdiv a big triangle into 4 smaller ones
        public static Func<List<Polyline>, List<Polyline>> subDivTriLst = triLst =>
        {
            List<Polyline> resLst = new List<Polyline>();

            foreach (var tri in triLst)
            {

                var rightAngIdx = -1;
                for (int i = 1; i < 4; i++)
                {
                    if (Math.Abs(Vector3d.Multiply(tri[i] - tri[i - 1], tri[i] - tri[(i + 1) % 3])) < 1e-5)
                    {
                        rightAngIdx = i;
                        break;
                    }
                }

                if (rightAngIdx < 0) // no right angle, divide into 4 sub-tri
                {
                    var mid = Enumerable.Range(0, 3).Select(i => 0.5 * (tri[i] + tri[i + 1])).ToArray();
                    resLst.Add(new Polyline(new List<Point3d> { tri[0], mid[0], mid[2], tri[0] }));
                    resLst.Add(new Polyline(new List<Point3d> { mid[0], tri[1], mid[1], mid[0] }));
                    resLst.Add(new Polyline(new List<Point3d> { mid[0], mid[1], mid[2], mid[0] }));
                    resLst.Add(new Polyline(new List<Point3d> { mid[2], mid[1], tri[2], mid[2] }));
                }
                else // right-angle tri (starter/ender), divide into 3 sub-tri
                {
                    var curIdx = rightAngIdx % 3;
                    var p0 = tri[curIdx];
                    var p1 = tri[curIdx + 1];
                    var p2 = tri[(curIdx + 2) % 3];

                    if ((p1 - p0).Length < (p2 - p0).Length)
                        (p1, p2) = (p2, p1); // swap p1, p2

                    var mid = new Point3d[] { 0.5 * (p0 + p1), 0.5 * (p1 + p2) };

                    resLst.Add(new Polyline(new List<Point3d> { p0, mid[0], mid[1], p0 }));
                    resLst.Add(new Polyline(new List<Point3d> { mid[0], p1, mid[1], mid[0] }));
                    resLst.Add(new Polyline(new List<Point3d> { p0, mid[1], p2, p0 }));
                }

            }

            return resLst;
        };

        public static void CreateCentreMap(in List<Polyline> polyIn, out Dictionary<string, ValueTuple<Point3d, Polyline>> cenMap)
        {
            cenMap = new Dictionary<string, ValueTuple<Point3d, Polyline>>();

            foreach (var x in polyIn)
            {
                Point3d cenPt = (x[0] + x[1] + x[2]) / 3;
                var cen = Utils.PtString(cenPt);
                (Point3d pt, Polyline poly) val = (cenPt, x);
                cenMap.Add(cen, val);
            }
        }

        public static void CreateNeighbourMap(in List<Polyline> polyIn, out Dictionary<string, HashSet<string>> nMap)
        {
            var edgeMap = new Dictionary<string, HashSet<string>>();

            // add 3 edges as key and associate the edge to the centre of the triangle
            foreach (var x in polyIn)
            {
                var cen = Utils.PtString((x[0] + x[1] + x[2]) / 3);

                for (int i = 0; i < 3; i++)
                {
                    var p0 = Utils.PtString(x[i]);
                    var p1 = Utils.PtString(x[i + 1]);

                    // do the following for both dir, in case point order in triangles not CCW
                    if (!edgeMap.ContainsKey(p0 + p1))
                        edgeMap.Add(p0 + p1, new HashSet<string>());

                    edgeMap[p0 + p1].Add(cen);

                    if (!edgeMap.ContainsKey(p1 + p0))
                        edgeMap.Add(p1 + p0, new HashSet<string>());

                    edgeMap[p1 + p0].Add(cen);
                }
            }

            // each triangle has three edges, collect their shared triangle as neighbours, using their centre PtString
            nMap = new Dictionary<string, HashSet<string>>();
            foreach (var x in polyIn)
            {
                var cen = Utils.PtString((x[0] + x[1] + x[2]) / 3);

                for (int i = 0; i < 3; i++)
                {
                    var p0 = Utils.PtString(x[i]);
                    var p1 = Utils.PtString(x[i + 1]);

                    if (!nMap.ContainsKey(cen))
                        nMap.Add(cen, new HashSet<string>());

                    foreach (var item in edgeMap[p0 + p1])
                    {
                        if (cen != item)
                            nMap[cen].Add(item);
                    }
                }
            }
        }


        // offset using scale mechanism
        private static readonly Func<Polyline, double, Polyline> OffsetTri = (tri, ratio) =>
        {
            var cen = (tri[0] + tri[1] + tri[2]) / 3;
            var trans = Transform.Scale(cen, ratio);

            tri.Transform(trans);
            return tri;
        };

        /// <summary>
        /// Main Func: offset triangles for soil water data: wilting point, field capacity, etc.
        /// </summary>
        public static (List<Polyline>, List<Polyline>, List<Polyline>, List<Polyline>, List<List<Polyline>>, List<List<Polyline>>)
            OffsetWater(in List<Curve> tri, SoilProperty sInfo, double rWater, int denEmbedWater, int denAvailWater)
        {
            // convert to polyline 
            var triPoly = tri.Select(x => Utils.CvtCrvToPoly(x)).ToList();

            // Datta, Sumon, Saleh Taghvaeian, and Jacob Stivers. Understanding Soil Water Content and Thresholds For Irrigation Management, 2017. https://doi.org/10.13140/RG.2.2.35535.89765.
            var coreRatio = 1 - sInfo.saturation;
            var wpRatio = coreRatio + sInfo.wiltingPoint;
            var fcRatio = coreRatio + sInfo.fieldCapacity;

            // offset the triangles for the 3 specific ratio
            var triCore = triPoly.Select(x => OffsetTri(x.Duplicate(), coreRatio)).ToList();
            var triWP = triPoly.Select(x => OffsetTri(x.Duplicate(), wpRatio)).ToList();
            var triFC = triPoly.Select(x => OffsetTri(x.Duplicate(), fcRatio)).ToList();

            // current water stage start from the core -- need a remap
            var rWaterRemap = Utils.remap(rWater, 0.0, 1.0, coreRatio, 1.0);

            var curWaterLn = triPoly.Select(x => OffsetTri(x.Duplicate(), rWaterRemap)).ToList();

            // creating hatches for the water content
            List<List<Polyline>> hatchCore = new List<List<Polyline>>();
            List<List<Polyline>> hatchPAW = new List<List<Polyline>>();

            for (int i = 0; i < triCore.Count; i++)
            {
                var tmpL = new List<Polyline>();
                for (int j = 1; j < denEmbedWater + 1; j++)
                {
                    double ratio = (double)j / (denEmbedWater + 1);
                    tmpL.Add(new Polyline(triCore[i].Zip(triWP[i], (x, y) => x * ratio + y * (1 - ratio))));
                }
                hatchCore.Add(tmpL);
            }

            // if current water stage <= wilting point, don't generate PAW hatch -- there's no PAW.
            if (rWater > wpRatio)
            {
                for (int i = 0; i < triCore.Count; i++)
                {
                    var tmpL2 = new List<Polyline>();
                    for (int j = 1; j < denAvailWater + 1; j++)
                    {
                        double ratio = (double)j / (denAvailWater + 1);
                        tmpL2.Add(new Polyline(triWP[i].Zip(curWaterLn[i], (x, y) => x * ratio + y * (1 - ratio))));
                    }
                    hatchPAW.Add(tmpL2);
                }
            }

            return (triCore, triWP, triFC, curWaterLn, hatchCore, hatchPAW);
        }


        //! Get string-based soil info
        public static string SoilText(SoilProperty sProperty)
        {
            string pattern = @"::Soil Info::
    soil type:      {0}
    wilting point:  {1}
    field capacity: {2}
    saturation:     {3}
";

            return string.Format(pattern, sProperty.soilType, sProperty.wiltingPoint, sProperty.fieldCapacity, sProperty.saturation);

        }


        // exponential fit {{0, 0}, {0.4, 0.1}, {0.5, 0.15}, {1, 1}}, customized for the organic matter purpose.
        // only works for [0-1] range.
        private static readonly Func<double, double> CustomExpFit = x => 0.0210324 * Math.Exp(3.8621 * x);

        // create organic matter around a triangle
        private static readonly Func<Polyline, Polyline, double, List<Line>> createOM = (polyout, polyin, divN) =>
        {
            // ! important: adjust and align seams
            var nonRepLst = polyout.Take(polyout.Count - 1);
            var disLst = nonRepLst.Select(x => x.DistanceTo(polyin[0])).ToList();
            int minIdx = disLst.IndexOf(disLst.Min());
            var rotatedLst = nonRepLst.Skip(minIdx).Concat(nonRepLst.Take(minIdx)).ToList();
            rotatedLst.Add(rotatedLst[0]);
            var polyoutRot = new Polyline(rotatedLst);

            // ! for each segment, decide divN and make subdivision
            // relOM: 20 - 50

            List<Line> res = new List<Line>();
            // omitting the last overlapping point
            for (int i = 0; i < polyoutRot.Count - 1; i++)
            {
                var segOutter = new Line(polyoutRot[i], polyoutRot[i + 1]);
                var segInner = new Line(polyin[i], polyin[i + 1]);

                int subdivN = (int)Math.Round(segOutter.Length / polyoutRot.Length * divN);


                var nurbIn = segOutter.ToNurbsCurve();
                nurbIn.Domain = new Interval(0, 1);

                var nurbOut = segInner.ToNurbsCurve();
                nurbOut.Domain = new Interval(0, 1);

                // make lines
                var param = nurbIn.DivideByCount(subdivN, true, out Point3d[] startPt);
                var endPt = param.Select(x => nurbOut.PointAt(x)).ToArray();

                var curLn = startPt.Zip(endPt, (s, e) => new Line(s, e)).ToList();

                res.AddRange(curLn);
            }

            return res;
        };


        // generate organic matter for soil inner
        public static (List<List<Line>>, OrganicMatterProperty) GenOrganicMatterInner(in Rectangle3d bnd, in SoilProperty sInfo, in List<Polyline> tri, double dOM)
        {
            var coreRatio = 1 - sInfo.saturation;
            var triCore = tri.Select(x => OffsetTri(x.Duplicate(), coreRatio)).ToList();

            // compute density based on distance to the soil surface
            List<double> denLst = new List<double>();
            foreach (var t in tri)
            {
                bnd.Plane.ClosestParameter(t.CenterPoint(), out double x, out double y);
                denLst.Add(bnd.Height - y);
            }

            // remap density
            var dMin = denLst.Min();
            var dMax = denLst.Max();

            var distDen = denLst.Select(x => CustomExpFit((x - dMin) / (dMax - dMin))).ToList();
            var triLen = tri.Select(x => x.Length).ToList();

            // generate lines
            List<List<Line>> res = new List<List<Line>>();
            for (int i = 0; i < tri.Count; i++)
            {
                // for each triangle, divide pts based on the density param, and create OM lines
                int divN = (int)Math.Round(tri[i].Length / distDen[i] * dOM / 10) * 3;
                if (divN == 0)
                    continue;

                var omLn = createOM(triCore[i], tri[i], divN);
                res.Add(omLn);
            }

            return (res, new OrganicMatterProperty(bnd, distDen.Min(), dOM, triLen.Max() / 3));

        }

        //! Main Func: (overload) Generate the top layer organic matter, using params from inner OM
        public static List<List<Line>> GenOrganicMatterTop(in OrganicMatterProperty omP, int type, int layer)
        {
            var height = omP.uL * 0.5 * Math.Sqrt(3) * 0.25 * Math.Pow(2, type);

            // create the top OM's boundary based on the soil boundary.
            int horizontalDivNdbl = (int)Math.Floor(omP.bnd.Corner(1).DistanceTo(omP.bnd.Corner(0)) / omP.uL * 2);
            var intWid = (horizontalDivNdbl / 2 + (horizontalDivNdbl % 2) * 0.5) * omP.uL;

            var cornerB = omP.bnd.Corner(3) + intWid * omP.bnd.Plane.XAxis * 1.001 + omP.bnd.Plane.YAxis * height * layer;
            Rectangle3d topBnd = new Rectangle3d(omP.bnd.Plane, omP.bnd.Corner(3), cornerB);

            var (_, omTri) = MakeTriMap(ref topBnd, layer);

            var flattenTri = omTri.SelectMany(x => x).ToList();
            var coreTri = flattenTri.Select(x => OffsetTri(x.ToPolyline().Duplicate(), 0.4));

            // generate division number and om lines
            //int divN = (int)Math.Round(flattenTri[1].ToPolyline().Length / omP.distDen * omP.omDen / 10) * 3;
            int divN = (int)Utils.remap(omP.omDen, 0.0, 1.0, 1, 30) * 3;
            var res = coreTri.Zip(flattenTri, (i, o) => createOM(i, o.ToPolyline(), divN)).ToList();

            return res;
        }

        //! Main Func: Generate the top layer organic matter
        public static List<List<Line>> GenOrganicMatterTop(in SoilBase sBase, int type, double dOM, int layer)
        {
            var dmRemap = Utils.remap(dOM, 0.0, 1.0, 0.001, 0.2);
            var omP = new OrganicMatterProperty(sBase.bnd, dmRemap, dOM, sBase.unitL);
            return GenOrganicMatterTop(omP, type, layer);
        }

        //! Main Func: Generate the urban soil organic matter
        public static List<Line> GenOrganicMatterUrban(in SoilBase sBase, in List<Polyline> polyIn, in List<Polyline> polyInOffset, double rOM)
        {
            var res = new List<Line>();
            if (rOM == 0)
                return res;

            // pt# match, then do the generation
            double relOM = Utils.remap(rOM, 0, 0.2, 5, 30);
            for (int i = 0; i < polyIn.Count; i++)
            {

                int divN = (int)Math.Round(polyIn[i].Length / sBase.unitL * relOM);

                // TODO: fix this issue or add warning
                // polygon pt# doesn't match, ignore 
                if (polyIn[i].Count != polyInOffset[i].Count)
                    continue;

                var omLn = createOM(polyIn[i], polyInOffset[i], divN);
                res.AddRange(omLn);
            }

            return res;
        }

        /// <summary>
        /// Main Func: Generate the biochar filling 
        /// </summary>
        public static List<Line> GenOrganicMatterBiochar(in SoilBase sBase, in List<Polyline> polyT)
        {

            List<Line> res = new List<Line>();

            polyT.ForEach(x =>
            {
                var cen = (x[0] + x[1] + x[2]) / 3;
                var param = x.ToNurbsCurve().DivideByCount(18, true);
                var og = param.Select(t => new Line(x.PointAt(t), cen)).ToList();
                res.AddRange(og);
            });

            return res;
        }

        /// <summary>
        /// Use environment to affect the EndPoint.
        /// If the startPt is inside any attractor / repeller area, that area will dominant the effect;
        /// Otherwise, we accumulate weighted (dist-based) effect of all the attractor/repeller area.
        /// </summary>
        public static Point3d ExtendDirByAffector(in Point3d pt, in Vector3d scaledDir,
            in SoilMap sMap, in bool envToggle = false,
            in double envDectectDist = 0.0,
            in List<Curve> envA = null, in List<Curve> envR = null)
        {

            // if the environment is not activated, directly output the extension
            if (!envToggle)
            {
                return pt + scaledDir;
            }

            // temporary value for rooth growth
            double forceInAttactor = 2;
            double forceOutAttractor = 1.5;
            double forceInRepeller = 0.3;
            double forceOutRepeller = 0.5;

            var sortingDict = new SortedDictionary<double, Tuple<Curve, char>>();

            // attractor
            foreach (var crv in envA)
            {
                var contain = crv.Contains(pt, sMap.mPln, 0.01);
                if (contain == PointContainment.Inside || contain == PointContainment.Coincident)
                    return Point3d.Add(pt, scaledDir * forceInAttactor); // grow faster
                else
                {
                    double dist;
                    if (crv.ClosestPoint(pt, out double t) && (dist = crv.PointAt(t).DistanceTo(pt)) < envDectectDist)
                        sortingDict[dist] = new Tuple<Curve, char>(crv, 'a');
                }
            }

            //repeller
            foreach (var crv in envR)
            {
                var contain = crv.Contains(pt, sMap.mPln, 0.01);
                if (contain == PointContainment.Inside || contain == PointContainment.Coincident)
                    return Point3d.Add(pt, scaledDir * forceInRepeller); // grow slower
                else
                {
                    double dist;
                    if (crv.ClosestPoint(pt, out double t) && (dist = crv.PointAt(t).DistanceTo(pt)) < envDectectDist)
                        sortingDict[dist] = new Tuple<Curve, char>(crv, 'r');
                }
            }

            // if not affected by the environment, return the original point
            if (sortingDict.Count == 0)
                return pt + scaledDir;

            // for how attractor and repeller affect the growth, considering the following cases:
            // 1. for a given area inside the detecting range, get the facing segment to the testing point.
            // 2. if the growing dir intersect the seg, growth intensity is affected;
            // 2.1 accumulate the forces
            // 3. if the growing dir doesn't interset the seg, but near the "facing cone", growing direction is affected;
            // 4. otherwise, growing is not affected.

            //                                                                                                                
            //                  +-------------------------+                                                      
            //                  |                         |                                                      
            //                  |                         |                                                      
            //                  |                         |                                                      
            //                  |                         |                                                      
            //                  |                         |                                                      
            //                  |                         |                                                      
            //                  |                         |                                                      
            //                  |                         |                                                      
            //                  |                         |                                                      
            //                  |                         |                                                      
            //                  ---------------------------                                                      
            //                   \                       /                                                       
            //                    \                     /                                                        
            //                     \                   /                                                         
            //                      \                 /                                                          
            //                       \               /                                                           
            //            --       v1 \-           -/ v0                                                         
            //              \--         \         /             --                                               
            //                 \--       \       /          ---/                                                 
            //                    \--     \     /        --/                                                     
            //              v1_rot   \--   \   /      --/  v0_rot                                                
            //                          \-- \ /   ---/                                                           
            //                             \-   -/                     

            // each attractor/repeller curve act independently ==> resolve one by one, and average afterwards
            var ptCol = new List<Point3d>();
            foreach (var pair in sortingDict)
            {
                var (v0, v1) = Utils.GetPtCrvFacingVector(pt, sMap.mPln, pair.Value.Item1);

                // enlarge the ray range by 15-deg
                var v0_enlarge = v0;
                var v1_enlarge = v1;
                v0_enlarge.Rotate(Utils.ToRadian(-15), sMap.mPln.Normal);
                v1_enlarge.Rotate(Utils.ToRadian(15), sMap.mPln.Normal);

                // calcuate angles between dir and the 4 vec
                var ang0 = Utils.SignedVecAngle(scaledDir, v0, sMap.mPln.Normal);
                var ang0_rot = Utils.SignedVecAngle(scaledDir, v0_enlarge, sMap.mPln.Normal);
                var ang1 = Utils.SignedVecAngle(scaledDir, v1, sMap.mPln.Normal);
                var ang1_rot = Utils.SignedVecAngle(scaledDir, v1_enlarge, sMap.mPln.Normal);

                // clamp force
                var K = envDectectDist * envDectectDist;
                var forceAtt = Math.Min(K / (pair.Key * pair.Key), forceOutAttractor);
                var forceRep = Math.Min(K / (pair.Key * pair.Key), forceOutRepeller);
                var newDir = scaledDir;

                // conditional decision:
                // dir in [vec0_enlarge, vec0] => rotate CCW
                if (ang0 * ang0_rot < 0 && Math.Abs(ang0) < 90 && Math.Abs(ang0_rot) < 90)
                {
                    var rotA = pair.Value.Item2 == 'a' ? -ang0_rot : ang0_rot;
                    newDir.Rotate(Utils.ToRadian(rotA), sMap.mPln.Normal);

                    newDir *= (pair.Value.Item2 == 'a' ? forceAtt : forceRep);
                }
                // dir in [vec1, vec1_enlarge] => rotate CW
                else if (ang1 * ang1_rot < 0 && Math.Abs(ang1) < 90 && Math.Abs(ang1_rot) < 90)
                {
                    var rotA = pair.Value.Item2 == 'a' ? -ang1_rot : ang1_rot;
                    newDir.Rotate(Utils.ToRadian(rotA), sMap.mPln.Normal);

                    newDir *= (pair.Value.Item2 == 'a' ? forceAtt : forceRep);
                }
                // dir in [vec0, vec1] => grow with force
                else if (ang0 * ang1 < 0 && Math.Abs(ang0) < 90 && Math.Abs(ang1) < 90)
                    newDir *= (pair.Value.Item2 == 'a' ? forceAtt : forceRep);


                ptCol.Add(pt + newDir);
            }

            return ptCol.Aggregate(new Point3d(0, 0, 0), (s, v) => s + v) / ptCol.Count;
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

    class SoilGeneral
    {
        public SoilGeneral(in SoilBase sBase, in SoilProperty sInfo, in List<Curve> stone, in int seed, in int stage = 5)
        {
            this.mBase = sBase;
            this.mInfo = sInfo;
            this.mStone = stone;
            this.mSeed = seed;
            this.mStage = stage;

            toLocal = Transform.ChangeBasis(Plane.WorldXY, sBase.pln);
            toWorld = Transform.ChangeBasis(sBase.pln, Plane.WorldXY);
        }

        public void Build(bool macOS = false)
        {
            var triLOrigin = mBase.soilT;

            // get area
            double totalArea = triLOrigin.Sum(x => BalCore.triArea(x));

            var totalASand = totalArea * mInfo.rSand;
            var totalASilt = totalArea * mInfo.rSilt;
            var totalAClay = totalArea * mInfo.rClay;

            // we randomize the triangle list's sequence to simulate a random-order Poisson Disk sampling 
            var rnd = mSeed >= 0 ? new Random(mSeed) : new Random(Guid.NewGuid().GetHashCode());

            var triL = triLOrigin;
            //var triL = triLOrigin.OrderBy(x => rnd.Next()).ToList();

            //var kdMap = new KdTree<double, Point3d>(2, new KdTree.Math.DoubleMath(), AddDuplicateBehavior.Skip);
            //foreach (var pl in triL)
            //{
            //    var cen = (pl[0] + pl[1] + pl[2]) / 3;
            //    var originalCen = cen;
            //    cen.Transform(toLocal);
            //    kdMap.Add(new[] { cen.X, cen.Y }, originalCen);
            //}

            // sand
            var triCen = triL.Select(x => (x[0] + x[1] + x[2]) / 3).ToList();
            List<Point3d> outSandCen = new List<Point3d>();
            BalCore.CreateCentreMap(triL, out cenMap);

            //! sand
            if (mStage == 0)
            {
                var nStep = (int)Math.Round(1 / mInfo.rSand);
                // a special case (hidden option to have very regularized grid, regardless of the ratio)
                outSandCen = triCen.Where((x, i) => i % nStep == 0).ToList();
            }
            else
            {
                // part 1
                /* 
                  * convert stage to randomness param: 1-10 --> 5% - 95% from Poisson's Disk Sampling, the rest from random sampling.
                  * 100% will cause all clay/silt triangle accumulated to the edge if sand ratio > 90%
                 */
                var numSand = (int)(Math.Round(triL.Count * mInfo.rSand));
                int numPoissonSand = Convert.ToInt32(numSand * Utils.remap(mStage, 1.0, 8.0, 1.0, 0.05));


                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    BeingAliveLanguageRC.Utils.SampleElim(triCen, mBase.bnd.Area, numPoissonSand, out outSandCen);
                    // part 2
                    var remainingTriCen = triCen.Except(outSandCen).ToList();
                    var randomTriCen = remainingTriCen.OrderBy(x => rnd.Next()).Take(numSand - numPoissonSand);

                    // combine the two parts
                    outSandCen.AddRange(randomTriCen);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    //! as OSX cannot import cpp lib, we use a non-poisson based approach for the sampling
                    outSandCen = triCen.OrderBy(x => rnd.Next()).Take(numSand).ToList();
                }


            }

            //#region method 2
            //  sample general points, then find the corresponding triangles, final results not as clean as method 1
            //List<Point3d> genPt = new List<Point3d>();
            //List<Point3d> sampledPt = new List<Point3d>();
            //BeingAliveLanguageRC.Utils.SampleElim(mBase.bnd, numSand, out genPt, out sampledPt, mSeed, 1, mStage);

            //var outSandCen = new List<Point3d>();
            //foreach (var pt in sampledPt)
            //{
            //    var tmpP = pt;
            //    tmpP.Transform(toLocal);
            //    var kdRes = kdMap.GetNearestNeighbours(new[] { tmpP.X, tmpP.Y }, 1);
            //    outSandCen.Add(kdRes[0].Value);
            //    kdMap.RemoveAt(kdRes[0].Point);
            //}
            //#endregion

            mSandT = outSandCen.Select(x => cenMap[Utils.PtString(x)].Item2).ToList();

            //! silt
            var preSiltT = triL.Except(mSandT).ToList();
            var preSiltTDiv = BalCore.subDivTriLst(preSiltT);
            var preSiltCen = preSiltTDiv.Select(x => (x[0] + x[1] + x[2]) / 3).ToList();
            BalCore.CreateCentreMap(preSiltTDiv, out cenMap);
            double avgPreSiltTArea = preSiltTDiv.Sum(x => BalCore.triArea(x)) / preSiltTDiv.Count;

            List<Point3d> outSiltCen = new List<Point3d>();

            // part 1
            var numSilt = (int)Math.Round(totalASilt / avgPreSiltTArea);
            int numPoissonSilt = Convert.ToInt32(numSilt * Utils.remap(mStage, 1.0, 8.0, 1.0, 0.05));

            // part 2
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                BeingAliveLanguageRC.Utils.SampleElim(preSiltCen, mBase.bnd.Area, numPoissonSilt, out outSiltCen);

                var curRemainTriCen = preSiltCen.Except(outSiltCen).ToList();
                var curRandomTriCen = curRemainTriCen.OrderBy(x => rnd.Next()).Take(numSilt - numPoissonSilt);

                // combine
                outSiltCen.AddRange(curRandomTriCen);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                outSiltCen = preSiltCen.OrderBy(x => rnd.Next()).Take(numSilt).ToList();

            }

            mSiltT = outSiltCen.Select(x => cenMap[Utils.PtString(x)].Item2).ToList();

            //! clay
            var preClayT = preSiltTDiv.Except(mSiltT).ToList();
            mClayT = BalCore.subDivTriLst(preClayT);


            // if rock exists, avoid it 
            if (mStone.Any() && mStone[0] != null)
            {
                var rockLocal = mStone;
                Func<Polyline, bool> hitRock = tri =>
                {
                    for (int i = 0; i < 3; i++)
                    {
                        foreach (var r in rockLocal)
                        {
                            r.TryGetPlane(out Plane pln);
                            var res = r.Contains(tri[i], pln, 0.01);
                            if (res == PointContainment.Inside || res == PointContainment.Coincident)
                                return true;
                        }
                    }

                    return false;
                };

                // avoid rock area
                mSandT = mSandT.Where(x => !hitRock(x)).ToList();
                mSiltT = mSiltT.Where(x => !hitRock(x)).ToList();
                mClayT = mClayT.Where(x => !hitRock(x)).ToList();
            }
        }

        public List<Polyline> Collect()
        {
            return mSandT.Concat(mSiltT).Concat(mClayT).ToList();
        }

        // in param
        SoilBase mBase;
        SoilProperty mInfo;
        List<Curve> mStone;
        int mSeed;
        int mStage;

        // out param
        public List<Polyline> mClayT, mSiltT, mSandT;

        // private
        private Dictionary<string, ValueTuple<Point3d, Polyline>> cenMap;

        Transform toLocal;
        Transform toWorld;
    }

    class SoilUrban
    {
        public SoilUrban(in SoilBase sBase, in double rSand, in double rClay, in double rBiochar, in List<double> rStone, in List<double> szStone)
        {
            this.sBase = sBase;
            this.rSand = rSand;
            this.rClay = rClay;
            this.rBiochar = rBiochar;
            this.rStone = rStone;
            this.szStone = szStone;

            this.totalArea = sBase.soilT.Sum(x => BalCore.triArea(x));

            sandT = new List<Polyline>();
            clayT = new List<Polyline>();
            biocharT = new List<Polyline>();
            stonePoly = new List<List<Polyline>>();

            toLocal = Transform.ChangeBasis(Plane.WorldXY, sBase.pln);
            toWorld = Transform.ChangeBasis(sBase.pln, Plane.WorldXY);

        }

        /// <summary>
        /// main func divide triMap into subdivisions based on the urban ratio
        /// </summary>
        public void Build()
        {
            #region Sand    
            //List<Polyline> sandT = new List<Polyline>();
            sandT.Clear();
            var postSandT = sBase.soilT;
            var totalASand = totalArea * rSand;

            if (totalASand > 0)
            {
                // sand
                var triCen = postSandT.Select(x => (x[0] + x[1] + x[2]) / 3).ToList();
                BalCore.CreateCentreMap(postSandT, out cenMap);

                // sand
                //todo: add OSX variation
                var numSand = (int)(Math.Round(postSandT.Count * rSand));
                BeingAliveLanguageRC.Utils.SampleElim(triCen, sBase.bnd.Area, numSand, out List<Point3d> outSandCen);
                sandT = outSandCen.Select(x => cenMap[Utils.PtString(x)].Item2).ToList();

                //var ptCen = SamplingUtils.uniformSampling(ref this.sBase, (int)(numSand * 1.2));
                //tmpPt = ptCen;
                //BalCore.CreateCentreMap(postSandT, out cenMap);

                // build a kd-map for polygon centre. We need to transform into 2d, otherwise, collision box will overlap
                //var kdMap = new KdTree<double, Polyline>(2, new KdTree.Math.DoubleMath(), AddDuplicateBehavior.Skip);
                //foreach (var pl in postSandT)
                //{
                //    var cen = (pl[0] + pl[1] + pl[2]) / 3;
                //    var originalCen = cen;
                //    cen.Transform(toLocal);
                //    kdMap.Add(new[] { cen.X, cen.Y }, pl);
                //}

                //HashSet<Polyline> sandTPrepare = new HashSet<Polyline>();
                //foreach (var pt in ptCen)
                //{
                //    var tmpP = pt;
                //    tmpP.Transform(toLocal);
                //    var kdRes = kdMap.GetNearestNeighbours(new[] { tmpP.X, tmpP.Y }, 1);

                //    sandTPrepare.Add(kdRes[0].Value);
                //}

                //sandT = sandTPrepare.OrderBy(x => Guid.NewGuid()).Take(numSand).ToList();
                //sandT = postSandT.OrderBy(x => Guid.NewGuid()).Take(numSand).ToList();
                postSandT = sBase.soilT.Except(sandT).ToList();
            }

            var lv3T = BalCore.subDivTriLst(BalCore.subDivTriLst(postSandT));
            #endregion

            #region Stone
            // at this stage, there are a collection of small-level triangles to be grouped into stones.
            var preStoneT = lv3T;
            var postStoneT = preStoneT;

            if (rStone.Sum() > 0)
            {
                postStoneT = PickAndCluster(preStoneT, rStone, szStone);
            }
            #endregion

            #region clay, biochar 
            var totalAclay = totalArea * rClay;
            var postClayT = postStoneT;
            if (totalAclay > 0)
            {
                var numClay = (int)Math.Round(lv3T.Count * rClay);
                clayT = postStoneT.OrderBy(x => Guid.NewGuid()).Take(numClay).ToList();
                postClayT = postStoneT.Except(clayT).ToList();
            }

            var totalABiochar = totalArea * rBiochar;
            var postBiocharT = postClayT;
            if (totalABiochar > 0)
            {
                var numBiochar = (int)(Math.Round(lv3T.Count * rBiochar));
                biocharT = postClayT.OrderBy(x => Guid.NewGuid()).Take(numBiochar).ToList();
                postBiocharT = postClayT.Except(biocharT).ToList();
            }

            #endregion

            // if there're small triangles left, give it to the bigger 
            var leftOverT = postBiocharT;
            if (leftOverT.Count > 0)
            {
                if (clayT == null)
                    biocharT = biocharT.Concat(leftOverT).ToList();

                else if (biocharT == null)
                    clayT = clayT.Concat(leftOverT).ToList();

                else if (clayT.Count > biocharT.Count)
                    clayT = clayT.Concat(leftOverT).ToList();

                else
                    biocharT = biocharT.Concat(leftOverT).ToList();
            }
        }


        /// <summary>
        /// Main func: pick and cluster lv3 triangles into stones, according to the input sz and ratio list
        /// 1. randomly generate evenly distributed stone centres
        /// 2. Expand triangle from these centres until stone area reached the target
        /// 3. Collect the rest triangle for clay, biochar, etc.
        /// </summary>
        public List<Polyline> PickAndCluster(in List<Polyline> polyIn, List<double> ratioLst, List<double> szLst)
        {
            var curPln = sBase.pln;
            var singleTriA = BalCore.triArea(polyIn[0]);
            //Transform toLocal = Transform.ChangeBasis(Plane.WorldXY, curPln);
            //Transform toWorld = Transform.ChangeBasis(curPln, Plane.WorldXY);

            // nbMap: mapping of each triangle to the neighbouring triangles
            BalCore.CreateNeighbourMap(polyIn, out nbMap);
            // cenMap: mapping of centre to the triangle centre Point3D and triangle polyline
            BalCore.CreateCentreMap(polyIn, out cenMap);

            List<double> areaLst = ratioLst.Select(x => x * totalArea).ToList(); // the target area for each stone type
            HashSet<string> allTriCenStr = new HashSet<string>(cenMap.Keys);
            HashSet<string> pickedTriCenStr = new HashSet<string>();

            // build a kd-map for polygon centre. We need to transform into 2d, otherwise, collision box will overlap
            var kdMap = new KdTree<double, Point3d>(2, new KdTree.Math.DoubleMath(), AddDuplicateBehavior.Skip);
            foreach (var pl in polyIn)
            {
                var cen = (pl[0] + pl[1] + pl[2]) / 3;
                var originalCen = cen;
                cen.Transform(toLocal);
                kdMap.Add(new[] { cen.X, cen.Y }, originalCen);
            }

            // convert relative stone radii for generating distributed points  24 ~ 64 triangles makes small ~ big stones
            var stoneSzTriLst = szLst.Select(x => (int)Utils.remap(x, 1, 5, 24, 64)).ToList();
            var stoneCntLst = areaLst.Zip(stoneSzTriLst, (a, n) => (int)Math.Round(a / (singleTriA * n))).ToList();


            // ! Poisson Disc sampling for stone centres

            var genCen = new List<Point3d>();
            var stoneCen = new List<Point3d>();

            // scale the bnd a bit to allow clay appears on borders
            BeingAliveLanguageRC.Utils.SampleElim(sBase.bnd, stoneCntLst.Sum(), out genCen, out stoneCen, -1, 0.93);

            // ! separate the stoneCen into several clusters according to the number of stone types, and collect the initial triangle
            // we use a new struct "StoneCluster" to store info related to the final stones
            var stoneCol = new List<StoneCluster>(stoneCen.Count);

            // ! Explanation:
            /// to decide the number of stones for each ratio, we actually need to solve a linear programming question of:
            ///
            /// Sum(N_i) = N
            /// Sum(N_i * f_area(stone_i)) = A_i
            /// N_i * f_area(stone_i) = A_i
            /// 
            /// to get multiple solutions.
            ///
            /// To get a usable solution, we need more assumptions, for instance, N_i ~ ratio_i, etc.
            ///
            /// 
            /// However, for the two stone type case, we can direct solve the only solution without the linear programming issue:
            /// 
            /// N_1 * Area(stone_1) = A_1
            /// N_2 * Area(stone_2) = A_2
            /// N_1 + N_2 = N
            /// 
            /// Area(stone_1) : Area(stone_2) = sz_1 : sz_2

            #region Initialize Stone Collection
            int idxCnt = 0;
            var tmpStoneCen = stoneCen;

            //for (int i = ratioLst.Count - 1; i >= 0; i--) // reverse the order, from bigger elements to smaller
            for (int i = 0; i < ratioLst.Count; i++)
            {
                var curLst = new List<Point3d>();
                BeingAliveLanguageRC.Utils.SampleElim(tmpStoneCen, sBase.bnd.Area, stoneCntLst[i], out curLst);
                //var curLst = tmpStoneCen.OrderBy(_ => Utils.balRnd.Next()).Take(stoneCntLst[i]).ToList();

                // record centre triangle
                foreach (var pt in curLst)
                {
                    var tmpP = pt;
                    tmpP.Transform(toLocal);
                    var kdRes = kdMap.GetNearestNeighbours(new[] { tmpP.X, tmpP.Y }, 1);

                    stoneCol.Add(new StoneCluster(idxCnt, kdRes[0].Value, ref cenMap, ref nbMap));
                    kdMap.RemoveAt(kdRes[0].Point);

                    // if added to the stone, then also store the picked cenId for all stones
                    pickedTriCenStr.Add(Utils.PtString(kdRes[0].Value));
                }

                tmpStoneCen = tmpStoneCen.Except(curLst).ToList();

                idxCnt++; // next stone type
            }
            #endregion

            // ! start to aggregate stone triangles and boolean into bigger ones. The target area for each cluster is stoneArea[i]
            bool areaReached = false;
            List<double> stoneTypeArea = Enumerable.Repeat(0.0, ratioLst.Count).ToList(); // store the total area of each stone type

            // add default centre tri area
            foreach (var st in stoneCol)
            {
                stoneTypeArea[st.typeId] += BalCore.triArea(cenMap[Utils.PtString(st.cen)].Item2);
            }

            // idx list, used for randomize sequence when growing stone
            var stoneIndices = Enumerable.Range(0, stoneCol.Count).ToList();
            stoneIndices = stoneIndices.OrderBy(_ => Guid.NewGuid()).ToList();

            while (!areaReached)
            {
                // the recordArea is used to guarantee that when stoneTypeArea cannot expand to targetArea, we also stop safely.
                double recordArea = stoneTypeArea.Sum();
                foreach (var i in stoneIndices)
                {
                    // ! 1. select a non-picked triangle in the neighbour set based on distance
                    var curStoneType = stoneCol[i].typeId;
                    var orderedNeigh = stoneCol[i].strIdNeigh.OrderBy(x => stoneCol[i].distMap[x]);

                    string nearestT = "";
                    foreach (var orderedId in orderedNeigh)
                    {
                        if (!pickedTriCenStr.Contains(orderedId))
                        {
                            nearestT = orderedId;
                            break;
                        }
                    }

                    // if no available neighbour, this stone is complete (cannot expand any more)
                    if (nearestT == "")
                        continue;

                    // ! 2. find a neighbour of this triangle, update the area of the stone type
                    //if (stoneTypeArea[curStoneType] < areaLst[curStoneType] && stoneCol[i].GetAveRadius() < stoneR[curStoneType])
                    if (stoneTypeArea[curStoneType] < areaLst[curStoneType])
                    {
                        stoneCol[i].strIdInside.Add(nearestT); // add to the collection
                        stoneCol[i].strIdNeigh.Remove(nearestT);

                        pickedTriCenStr.Add(nearestT);
                        stoneTypeArea[curStoneType] += BalCore.triArea(cenMap[nearestT].Item2); // add up area
                    }

                    // ! 3. expand, and update corresponding neighbouring set
                    foreach (var it in nbMap[nearestT])
                    {
                        if (!pickedTriCenStr.Contains(it))
                        {
                            // add all neighbour that are in the outer set
                            stoneCol[i].strIdNeigh.Add(it);
                            stoneCol[i].AddToDistMap(it, stoneCol[i].cen.DistanceTo(cenMap[it].Item1));
                        }
                    }

                    // ! 5. compare area condition
                    if (stoneTypeArea.Sum() >= areaLst.Sum())
                    {
                        areaReached = true;
                        break; // foreach loop
                    }
                }
                // randomize the stone list for the next iteration
                stoneIndices = stoneIndices.OrderBy(_ => Guid.NewGuid()).ToList();

                // stone cannot expand anymore
                if (recordArea == stoneTypeArea.Sum())
                    break;
            }

            // ! collect polyline for each stone and boolean
            stonePoly = Enumerable.Range(0, ratioLst.Count).Select(x => new List<Polyline>()).ToList();

            // stoneCollection: for debugging, collection of small-triangle in each stone cluster
            //stoneCollection = new List<List<Polyline>>();
            stoneCol.ForEach(x =>
            {
                x.T = x.strIdInside.Select(id => cenMap[id].Item2).ToList(); // optional
                                                                             //stoneCollection.Add(x.T);

                x.MakeBoolean();
                stonePoly[x.typeId].AddRange(x.bndCrvCol);
            });

            //todo: make correct set boolean of restPoly
            // add back the rest neighbouring triangle of the stone to the main collection
            var restPoly = allTriCenStr.Except(pickedTriCenStr).Select(id => cenMap[id].Item2).ToList();

            return restPoly;
        }


        public void CollectAll(out List<Polyline> allT)
        {
            allT = new List<Polyline>();
        }

        SoilBase sBase;
        readonly double rSand, rClay, rBiochar, totalArea;
        readonly List<double> rStone;
        readonly List<double> szStone;
        public List<Polyline> sandT, clayT, biocharT;
        public List<List<Polyline>> stonePoly;

        public List<Polyline> tmpT;

        public List<Point3d> tmpPt;
        public List<Point3d> stoneCen;
        public List<List<Polyline>> stoneCollection;

        public Dictionary<string, ValueTuple<Point3d, Polyline>> cenMap;
        public Dictionary<string, HashSet<string>> nbMap;

        Transform toLocal;
        Transform toWorld;
    }

    class SoilMap
    {
        public SoilMap()
        {
            this.mPln = Plane.WorldXY;
            this.kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath(), AddDuplicateBehavior.Skip);
            this.topoMap = new ConcurrentDictionary<string, List<Tuple<float, string>>>();
            this.ptMap = new ConcurrentDictionary<string, Point3d>();
            this.distNorm = new Normal(3.5, 0.5);
        }

        public SoilMap(in Plane pl, in string mapMode)
        {
            // kd-tree map
            this.kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath(), AddDuplicateBehavior.Skip);

            // topological map
            this.topoMap = new ConcurrentDictionary<string, List<Tuple<float, string>>>();

            // point map
            this.ptMap = new ConcurrentDictionary<string, Point3d>();

            this.mPln = pl;
            this.distNorm = new Normal(3.5, 0.5);
            this.mapMode = mapMode;
        }

        private void AddNeighbour(string strLoc, int idx, in Point3d refP, in Point3d P)
        {
            var dist = (float)refP.DistanceTo(P);
            if (topoMap[strLoc][idx].Item1 < 0 || dist < topoMap[strLoc][idx].Item1)
            {
                topoMap[strLoc][idx] = new Tuple<float, string>(dist, Utils.PtString(P));
            }
        }

        private void AddSectionalTriPt(in Polyline poly)
        {
            // tolerance for angle in the grid map
            double tol = 5; // considering the fact of the scaling, this should be adqueate

            // if triangle contains a 90deg corner, it is a side-triangle, ignore it.
            for (int i = 0; i < 3; i++)
            {
                var v0 = poly[1] - poly[0];
                var v1 = poly[2] - poly[1];
                var v2 = poly[0] - poly[2];

                double triTol = 1e-3;
                if (Math.Abs(Vector3d.Multiply(v0, v1)) < triTol ||
                    Math.Abs(Vector3d.Multiply(v1, v2)) < triTol ||
                    Math.Abs(Vector3d.Multiply(v2, v0)) < triTol)
                    return;
            }

            // use kdTree for duplication removal
            // use concurrentDict for neighbour storage 
            for (int i = 0; i < 3; i++)
            {
                var pt = poly[i];
                var kdKey = new[] { (float)pt.X, (float)pt.Y, (float)pt.Z };
                var strLoc = Utils.PtString(pt);
                if (kdMap.Add(kdKey, strLoc))
                {
                    ptMap.TryAdd(strLoc, pt);
                    topoMap.TryAdd(strLoc, new List<Tuple<float, string>> {
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, ""),
                    });
                }

                List<Point3d> surLst = new List<Point3d> { poly[(i + 1) % 3], poly[(i + 2) % 3] };
                foreach (var pNext in surLst)
                {
                    var vP = pNext - pt;
                    var ang = Utils.ToDegree(Vector3d.VectorAngle(mPln.XAxis, vP, mPln.ZAxis));

                    if (Math.Abs(ang - 60) < tol)
                        AddNeighbour(strLoc, 0, pt, pNext);
                    else if (Math.Abs(ang - 120) < tol)
                        AddNeighbour(strLoc, 1, pt, pNext);
                    else if (Math.Abs(ang - 180) < tol)
                        AddNeighbour(strLoc, 2, pt, pNext);
                    else if (Math.Abs(ang - 240) < tol)
                        AddNeighbour(strLoc, 3, pt, pNext);
                    else if (Math.Abs(ang - 300) < tol)
                        AddNeighbour(strLoc, 4, pt, pNext);
                    else if (Math.Abs(ang) < tol || Math.Abs(ang - 360) < tol)
                        AddNeighbour(strLoc, 5, pt, pNext);
                    else
                        throw new ArgumentException($"Error: point {strLoc} has no neighbour!");
                }
            }
        }

        public void AddGeo(in ConcurrentBag<Polyline> polyIn) { }

        public void AddGeo(in ConcurrentBag<Point3d> ptIn) { }

        public void BuildMap(in ConcurrentBag<Polyline> polyBag)
        {
            // for sectional version, we need to get neighbouring relations.
            // cannot use parallel, need sequential.
            if (this.mapMode == "sectional")
            {
                var polyLst = polyBag.ToList();
                foreach (var tri in polyLst)
                {
                    // 1. add all pts 
                    this.AddSectionalTriPt(in tri);
                    // 2. create topology mapping
                    //this.CreateSectionalTriTopoMap(in tri);
                }

                // check topoMap is successfully built
                foreach (var m in topoMap)
                {
                    var sumIdx = m.Value.Select(x => x.Item1).Sum();
                    if (sumIdx == -6)
                        throw new ArgumentException("Error: Topo map is not built successfully. Check if the plane is aligned with the triangles.");
                }


            }
            // for planar version, adding to the kdTree can be parallel.
            else if (this.mapMode == "planar")
            {
                var ptBag = new ConcurrentBag<Point3d>();
                Parallel.ForEach(polyBag, pl =>
                {
                    foreach (var p in pl)
                        ptBag.Add(p);
                });
                BuildMap(ptBag);
            }

            // ! compute unitLen
            polyBag.TryPeek(out Polyline tmp);
            unitLen = polyBag.Select(x => x.Length).Average() / (tmp.Count - 1);
        }

        public void BuildMap(in ConcurrentBag<Point3d> ptLst)
        {
            Parallel.ForEach(ptLst, pt =>
            {
                // for general cases, just build map and remove duplicated points
                var kdKey = new[] { (float)pt.X, (float)pt.Y, (float)pt.Z };
                var strLoc = Utils.PtString(pt);
                if (kdMap.Add(kdKey, strLoc))
                {
                    ptMap.TryAdd(strLoc, pt);
                }
                //var res = kdMap.RadialSearch(kdKey, (float)0.01, 1);

                //if (res.Length == 0)
                //{
                //    kdMap.Add(kdKey, strLoc);
                //    ptMap.TryAdd(strLoc, pt);
                //}
            });

            // average 10 random selected pt to its nearest point as unitLen
            var pt10 = ptLst.OrderBy(x => Guid.NewGuid()).Take(10).ToList();
            unitLen = pt10.Select(x =>
            {
                // find the 2 nearest point and measure distance (0 and a p-p dist).
                var res = kdMap.GetNearestNeighbours(new[] { (float)x.X, (float)x.Y, (float)x.Z }, 2);
                var nearest2Dist = res.Select(m => ptMap[m.Value].DistanceTo(x)).ToList();
                return nearest2Dist.Max();
            }).Average();
        }

        public void BuildBound()
        {
            List<double> uLst = new List<double>();
            List<double> vLst = new List<double>();
            foreach (var node in kdMap)
            {
                var pt3d = new Point3d(node.Point[0], node.Point[1], node.Point[2]);
                double u, v;
                if (mPln.ClosestParameter(pt3d, out u, out v))
                {
                    uLst.Add(u);
                    vLst.Add(v);
                }
            }
            mBndParam = new Tuple<double, double, double, double>(uLst.Min(), uLst.Max(), vLst.Min(), vLst.Max());
        }
        public bool IsOnBound(in Point3d pt)
        {
            double u, v;
            if (mPln.ClosestParameter(pt, out u, out v))
            {
                if ((mBndParam.Item1 - u) * (mBndParam.Item1 - u) < 1e-2
                    || (mBndParam.Item2 - u) * (mBndParam.Item2 - u) < 1e-2
                    || (mBndParam.Item3 - v) * (mBndParam.Item3 - v) < 1e-2
                    || (mBndParam.Item4 - v) * (mBndParam.Item4 - v) < 1e-2)
                    return true;
            }

            return false;
        }

        public Point3d GetNearestPoint(in Point3d pt)
        {
            var resNode = kdMap.GetNearestNeighbours(new[] { (float)pt.X, (float)pt.Y, (float)pt.Z }, 1)[0];

            return new Point3d(resNode.Point[0], resNode.Point[1], resNode.Point[2]);
        }

        public string GetNearestPointStr(in Point3d pt)
        {
            var resNode = kdMap.GetNearestNeighbours(new[] { (float)pt.X, (float)pt.Y, (float)pt.Z }, 1)[0];

            return resNode.Value;
        }

        public List<Point3d> GetNearestPoints(in Point3d pt, int N)
        {
            var resNode = kdMap.GetNearestNeighbours(new[] { (float)pt.X, (float)pt.Y, (float)pt.Z }, N);

            // error case
            if (resNode.Length == 0)
            {
                return new List<Point3d>();
            }

            var resL = resNode.Select(x => new Point3d(x.Point[0], x.Point[1], x.Point[2])).ToList();
            return resL;
        }

        public List<string> GetNearestPointsStr(in Point3d pt, int N)
        {
            var resNode = kdMap.GetNearestNeighbours(new[] { (float)pt.X, (float)pt.Y, (float)pt.Z }, N);

            // error case
            if (resNode.Length == 0)
            {
                return new List<string>();
            }

            var resL = resNode.Select(x => x.Value).ToList();
            return resL;
        }

        private int SampleIdx(int i0 = 2, int i1 = 5)
        {
            if (i0 > i1)
                return -1;

            // make sure fall into [2, 5] due to the hex arrangement and index
            var sampleIdx = (int)Math.Round(distNorm.Sample());
            while (sampleIdx < i0 || sampleIdx > i1)
                sampleIdx = (int)Math.Round(distNorm.Sample());

            return sampleIdx;
        }

        // sample the next point from idx range [i0, i1], using the current pt
        public (double, string) GetNextPointAndDistance(in string pt, int i0 = 2, int i1 = 5)
        {
            var idx = SampleIdx(i0, i1);

            var (dis, nextPt) = topoMap[pt][idx];
            while (nextPt == "")
            {
                idx = SampleIdx(i0, i1);
                (dis, nextPt) = topoMap[pt][idx];
            }

            return (dis, nextPt);
        }

        public Point3d GetPoint(string strKey)
        {
            return ptMap[strKey];
        }


        public Plane mPln;
        // boundary param based on the basePlane
        Tuple<double, double, double, double> mBndParam = new Tuple<double, double, double, double>(0, 0, 0, 0);

        public double unitLen = float.MaxValue;
        readonly KdTree<float, string> kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath());
        readonly ConcurrentDictionary<string, List<Tuple<float, string>>> topoMap;
        public ConcurrentDictionary<string, Point3d> ptMap;
        readonly Normal distNorm = new Normal();
        public string mapMode = "sectional";

    }

    class MapNode
    {
        public Point3d pos;
        public List<MapNode> nextNode = new List<MapNode>();
        public Vector3d dir = new Vector3d();

        public double distance = 0.0;
        public int steps = 0;

        public MapNode(in Point3d pt)
        {
            this.pos = pt;
        }

        public void addChildNode(in MapNode node)
        {
            // pos, distance, direction
            node.distance = distance + pos.DistanceTo(node.pos);
            node.steps = steps + 1;
            node.dir = node.pos - pos;

            nextNode.Add(node);
        }
    }

    class RootSectional
    {
        // for sectional root, we use a "TREE" structure, and BFS for constructing the "radius" of each branch
        public RootSectional()
        {

        }

        public RootSectional(in SoilMap map, in Point3d anchor,
            string rootType, in int steps = 1, in int density = 2, in int seed = -1,
            in bool envToggle = false, in double envRange = 0.0,
            in List<Curve> envAtt = null, in List<Curve> envRep = null)
        {
            mSoilMap = map;
            mAnchor = anchor;
            mRootType = rootType;
            mSeed = seed;
            mSteps = steps;
            mDensity = density;
            mRootNode = new MapNode(anchor);

            mRnd = mSeed >= 0 ? new Random(mSeed) : Utils.balRnd;
            mDownDir = -mSoilMap.mPln.YAxis;

            // env param
            this.envToggle = envToggle;
            this.envDist = envRange;
            this.envAtt = envAtt;
            this.envRep = envRep;
        }

        private bool RootDensityCheck(Point3d pt)
        {
            var k = Utils.PtString(pt);
            if (!mSoilEnv.ContainsKey(k))
                mSoilEnv[k] = 0;
            else if (mSoilEnv[k] > 20)
                return false;

            return true;
        }

        private void GrowSingleRoot(Vector3d nextDir, in MapNode curNode, ref List<MapNode> resLst)
        {

            // direction scale and extension
            nextDir.Unitize();
            if (Vector3d.Multiply(nextDir, mDownDir) < 1e-2)
            {
                nextDir += mDownDir * 0.3;
            }

            nextDir *= mSoilMap.unitLen * (mRnd.Next(100, 170) / 100.0);

            var endPt = BalCore.ExtendDirByAffector(
                curNode.pos, nextDir, mSoilMap,
                envToggle, envDist, envAtt, envRep);

            //tricks to add randomness
            var tmpPos = mSoilMap.GetNearestPoint(endPt);
            resLst.Add(new MapNode(tmpPos));
        }

        public List<MapNode> extendRoot(in MapNode curNode, in Vector3d dir, in string rType = "single")
        {
            var resLst = new List<MapNode>();
            double denParam = curNode.steps < mSteps * 0.2 ? 0.1 : 0.03;

            if (rType == "single")
            {
                // direction variation + small turbulation
                var nextDir = dir;
                nextDir.Unitize();
                if (curNode.steps < mSteps * 0.5) // gravity effect at the initial phases
                    nextDir += mDownDir * 0.7;
                else
                    nextDir += mDownDir * 0.3;

                nextDir.Unitize();
                var rnd = new Random((int)Math.Round(curNode.pos.DistanceToSquared(mSoilMap.mPln.Origin)));
                if (curNode.steps > 3) // turbulation
                {
                    nextDir += (rnd.Next(-50, 50) / 100.0) * mSoilMap.mPln.XAxis;
                }

                // direction scale and extension
                GrowSingleRoot(nextDir, curNode, ref resLst);

                //nextDir.Unitize();
                //nextDir *= mSoilMap.unitLen * (rnd.Next(100, 120) / 100.0);

                //var endPt = BalCore.ExtendDirByAffector(
                //    curNode.pos, nextDir, mSoilMap,
                //    envToggle, envDist, envAtt, envRep);

                //var tmpPos = mSoilMap.GetNearestPoint(endPt);
                //resLst.Add(new MapNode(tmpPos));
            }
            else if (rType == "multi")
            {
                // direction variation + small turbulation
                var initDir = dir;
                var turbDir = Vector3d.Zero;

                var nextDir = dir;
                var nextDir2 = dir;

                var rnd = new Random((int)Math.Round(curNode.pos.DistanceToSquared(mSoilMap.mPln.Origin)));

                nextDir.Unitize();
                if (curNode.steps < mSteps * 0.5) // gravity effect at the initial phases
                    initDir += mDownDir * 0.4;
                else
                    initDir += mDownDir * 0.2;

                initDir.Unitize();
                if (curNode.steps > 2) // turbulation
                {
                    turbDir = (rnd.Next(-50, 50) / 100.0) * mSoilMap.mPln.XAxis;
                    nextDir = initDir + turbDir;
                }

                // direction scale and extension
                GrowSingleRoot(nextDir, curNode, ref resLst);

                // ! density control: percentage control based on density param
                if (mRnd.NextDouble() < mDensity * denParam)
                {
                    nextDir2 = initDir - turbDir;

                    // direction scale and extension
                    GrowSingleRoot(nextDir2, curNode, ref resLst);
                }
            }

            return resLst;
        }

        public void Grow(int rSteps, int rDen = 2)
        {
            var anchorOnMap = mSoilMap.GetNearestPoint(mAnchor);
            if (anchorOnMap != null)
                mRootNode = new MapNode(anchorOnMap);

            //! prepare initial root and init BFS queue
            bfsQ.Clear();
            mSoilEnv.Clear();

            mRootNode.dir = -mSoilMap.mPln.YAxis * mSoilMap.unitLen * 2;

            //! get a bunch of closest points and sort it based on the angle with "down vector"
            var pts = mSoilMap.GetNearestPoints(mRootNode.pos, rDen + 10).ToArray();

            var ptLoc = new List<Point3d>();
            var ptAng = new List<double>();
            foreach (var p in pts)
            {
                var vec = p - mRootNode.pos;
                vec.Unitize();

                var sign = Vector3d.CrossProduct(vec, mDownDir).Z > 0 ? 1 : -1;
                var prod = Vector3d.Multiply(vec, mDownDir) * sign;

                if (prod != 0)
                {
                    ptLoc.Add(p);
                    ptAng.Add(Math.Round(prod, 3));
                }
            }

            // sort the points based on angle
            var arrLoc = ptLoc.ToArray();
            var arrAng = ptAng.ToArray();
            Array.Sort(arrAng, arrLoc);

            var distinctAngle = arrAng.Distinct().ToArray();
            var distinctPt = distinctAngle.Select(x => arrLoc[arrAng.ToList().IndexOf(x)]).ToList();

            // pick pts from the two sides
            var pickedPt = new List<Point3d>();
            for (int i = 0; i < rDen; i++)
            {
                if (i % 2 == 0)
                    pickedPt.Add(distinctPt[i / 2]);
                else
                    pickedPt.Add(distinctPt[distinctPt.Count - (i / 2 + 1)]);
            }

            // add the first rDen points 
            for (int i = 0; i < rDen; i++)
            {
                var x = new MapNode(pickedPt[i]);
                mRootNode.addChildNode(x);
                bfsQ.Enqueue(x);

                //  collecting initial crv
                rootCrv.Add(new Line(mRootNode.pos, x.pos));
            }

            // ! BFS starts
            while (bfsQ.Count > 0)
            {
                var curNode = bfsQ.Dequeue();

                //  ! stopping criteria
                if (curNode.steps >= rSteps || mSoilMap.IsOnBound(curNode.pos))
                {
                    continue; // skip this node, start new item in the queue
                }

                var nextDir = curNode.dir;

                // extend roots
                var nodes = extendRoot(curNode, nextDir, mRootType);
                nodes.ForEach(x =>
                {
                    if (curNode.pos.DistanceToSquared(x.pos) >= 0.01 && RootDensityCheck(x.pos))
                    {

                        curNode.addChildNode(x);
                        bfsQ.Enqueue(x);

                        //  collecting crv
                        mSoilEnv[Utils.PtString(x.pos)] += 1; // record # the location is used
                        rootCrv.Add(new Line(curNode.pos, x.pos));
                    }
                });
            }
        }

        // rootTyle: 0 - single, 1 - multi(branching)
        // deprecated: archived function, only for record purpose
        public void GrowRoot(double radius, int rDen = 2)
        {
            // init starting ptKey
            var anchorOnMap = mSoilMap.GetNearestPointsStr(mAnchor, 1)[0];
            if (anchorOnMap != null)
                frontKey.Add(anchorOnMap);

            // build a distance map from anchor point
            // using euclidian distance, not grid distance for ease
            disMap.Clear();
            foreach (var pt in mSoilMap.ptMap)
            {
                disMap[pt.Key] = pt.Value.DistanceTo(mAnchor);
            }

            // grow root until given radius is reached
            double curR = 0;
            double aveR = 0;

            int branchNum;
            switch (mRootType)
            {
                case "single":
                    branchNum = 1;
                    break;
                case "multi":
                    branchNum = 2;
                    break;
                default:
                    branchNum = 1;
                    break;
            }

            // TODO: change to "while"?
            for (int i = 0; i < 5000; i++)
            {
                if (frontKey.Count == 0 || curR >= radius)
                    break;

                // pop the first element
                var rndIdx = Utils.balRnd.Next(0, frontKey.Count()) % frontKey.Count;
                var startPt = frontKey.ElementAt(rndIdx);
                frontKey.Remove(startPt);
                nextFrontKey.Clear();

                // use this element as starting point, grow roots
                int branchCnt = 0;
                for (int j = 0; j < 20; j++)
                {
                    if (branchCnt >= branchNum)
                        break;

                    // the GetNextPointAndDistance guarantee grow downwards
                    var (dis, nextPt) = mSoilMap.GetNextPointAndDistance(in startPt);
                    if (nextFrontKey.Add(nextPt))
                    {
                        rootCrv.Add(new Line(mSoilMap.GetPoint(startPt), mSoilMap.GetPoint(nextPt)));
                        curR = disMap[nextPt] > curR ? disMap[nextPt] : curR;

                        branchCnt += 1;
                    }
                }

                frontKey.UnionWith(nextFrontKey);
                var disLst = frontKey.Select(x => disMap[x]).ToList();
                disLst.Sort();
                aveR = disLst[(disLst.Count() - 1) / 2];
            }
        }

        // public variables
        public List<Line> rootCrv = new List<Line>();

        // internal variables
        HashSet<string> frontKey = new HashSet<string>();
        HashSet<string> nextFrontKey = new HashSet<string>();
        ConcurrentDictionary<string, double> disMap = new ConcurrentDictionary<string, double>();
        Point3d mAnchor = new Point3d();
        MapNode mRootNode = null;
        SoilMap mSoilMap = new SoilMap();
        Queue<MapNode> bfsQ = new Queue<MapNode>();
        Dictionary<string, int> mSoilEnv = new Dictionary<string, int>();

        int mSeed = -1;
        int mSteps = 0;
        int mDensity = 1;
        Random mRnd;
        Vector3d mDownDir;

        string mRootType = "single";
        bool envToggle = false;
        double envDist = 0.0;
        List<Curve> envAtt = null;
        List<Curve> envRep = null;

    }

    class RootPlanar
    {
        public RootPlanar() { }

        public RootPlanar(in SoilMap soilmap, in Point3d anchor, double scale, int phase, int divN,
            in List<Curve> envA = null, in List<Curve> envR = null, double envRange = 0.0, bool envToggle = false)
        {
            this.sMap = soilmap;
            this.anchor = anchor;
            this.scale = scale;
            this.phase = phase;
            this.divN = divN;

            this.envA = envA;
            this.envR = envR;
            this.envDetectingDist = envRange * sMap.unitLen;
            this.envT = envToggle;

            this.rCrv.Clear();
            this.rAbs.Clear();

            for (int i = 0; i < 6; i++)
            {
                rCrv.Add(new List<Line>());
                frontId.Add(new List<string>());
                frontDir.Add(new List<Vector3d>());
            }
        }

        public (List<List<Line>>, List<Line>) GrowRoot()
        {
            for (int i = 1; i < phase + 1; i++)
            {
                switch (i)
                {
                    case 1:
                        DrawPhaseCentre(0);
                        break;
                    case 2:
                        DrawPhaseBranch(1);
                        break;
                    case 3:
                        DrawPhaseBranch(2);
                        break;
                    case 4:
                        DrawPhaseBranch(3);
                        break;
                    case 5:
                        DrawPhaseExtend(4);
                        break;
                    default:
                        break;
                }
            }

            foreach (var rLst in rCrv)
            {
                CreateAbsorbent(rLst);
            }

            return (rCrv, rAbs);
        }

        public void CreateAbsorbent(in List<Line> roots, int N = 5)
        {
            var rotAng = 40;

            var rtDir = roots.Select(x => x.Direction).ToList();

            foreach (var (ln, i) in roots.Select((ln, i) => (ln, i)))
            {
                if (ln.Length == 0)
                    continue;

                var segL = ln.Length * 0.2;
                ln.ToNurbsCurve().DivideByCount(N, false, out Point3d[] basePt);

                var dir0 = rtDir[i];
                var dir1 = rtDir[i];

                dir0.Unitize();
                dir1.Unitize();

                dir0.Rotate(Utils.ToRadian(rotAng), sMap.mPln.Normal);
                dir1.Rotate(Utils.ToRadian(-rotAng), sMap.mPln.Normal);

                foreach (var p in basePt)
                {
                    rAbs.Add(new Line(p, p + dir0 * segL));
                    rAbs.Add(new Line(p, p + dir1 * segL));
                }
            }
        }

        protected void DrawPhaseCentre(int phaseId)
        {
            var ang = Math.PI * 2 / divN;
            var curLen = sMap.unitLen * scale * scaleFactor[0];

            for (int i = 0; i < divN; i++)
            {
                var dir = sMap.mPln.PointAt(Math.Cos(ang * i), Math.Sin(ang * i), 0) - sMap.mPln.Origin;
                BranchExtend(phaseId, anchor, dir, curLen);
            }
        }

        protected void DrawPhaseBranch(int phaseId)
        {
            var preId = phaseId - 1;
            var curLen = sMap.unitLen * scale * scaleFactor[phaseId];

            // for each node, divide two branches
            foreach (var (pid, i) in frontId[preId].Select((pid, i) => (pid, i)))
            {
                var curVec = frontDir[preId][i];
                var curPt = sMap.ptMap[pid];

                // v0, v1 are utilized
                var v0 = curVec;
                var v1 = curVec;
                v0.Rotate(Utils.ToRadian(30), sMap.mPln.Normal);
                v1.Rotate(Utils.ToRadian(-30), sMap.mPln.Normal);

                BranchExtend(phaseId, curPt, v0, curLen);
                BranchExtend(phaseId, curPt, v1, curLen);
            }
        }

        protected void DrawPhaseExtend(int phaseId)
        {
            var preId = phaseId - 1;

            foreach (var (pid, i) in frontId[preId].Select((pid, i) => (pid, i)))
            {
                // no branching, just extending
                var preVec = frontDir[preId - 1][(int)(i / 2)];
                var curVec = frontDir[preId][i];
                var curLen = sMap.unitLen * scale * scaleFactor[phaseId];
                var curPt = sMap.ptMap[pid];

                // v0, v1 are unitized
                var tmpVec = Vector3d.CrossProduct(curVec, preVec);
                var sign = tmpVec * sMap.mPln.Normal;
                var ang = (sign >= 0 ? 15 : -15);

                curVec.Rotate(Utils.ToRadian(ang), sMap.mPln.Normal);
                BranchExtend(phaseId, curPt, curVec, curLen);
            }
        }

        protected void BranchExtend(int lvId, in Point3d startP, in Vector3d dir, double L)
        {
            var endPtOffGrid = GrowPointWithEnvEffect(startP, dir * L);

            // record
            var ptKey2 = sMap.GetNearestPointsStr(endPtOffGrid, 2);
            var endPkey = Utils.PtString(endPtOffGrid) == ptKey2[0] ? ptKey2[1] : ptKey2[0];
            var endP = sMap.ptMap[endPkey];

            var branchLn = new Line(startP, endP);
            var unitDir = branchLn.Direction;
            unitDir.Unitize();

            // draw
            rCrv[lvId].Add(branchLn);
            frontId[lvId].Add(endPkey);
            frontDir[lvId].Add(unitDir);
        }

        /// <summary>
        /// Use environment to affect the EndPoint.
        /// If the startPt is inside any attractor / repeller area, that area will dominant the effect;
        /// Otherwise, we accumulate weighted (dist-based) effect of all the attractor/repeller area.
        /// </summary>
        protected Point3d GrowPointWithEnvEffect(in Point3d pt, in Vector3d scaledDir)
        {
            return BalCore.ExtendDirByAffector(pt, scaledDir, sMap, envT, envDetectingDist, envA, envR);
        }

        protected SoilMap sMap = new SoilMap();
        protected Point3d anchor = new Point3d();
        readonly double scale = 1.0;
        readonly int phase = 0;
        readonly int divN = 4;

        List<Curve> envA = null;
        List<Curve> envR = null;
        double envDetectingDist = 0;
        bool envT = false;

        readonly private List<double> scaleFactor = new List<double> { 1, 1.2, 1.5, 2, 2.5 };

        List<List<Line>> rCrv = new List<List<Line>>();
        List<Line> rAbs = new List<Line>();
        List<List<string>> frontId = new List<List<string>>();
        List<List<Vector3d>> frontDir = new List<List<Vector3d>>();
    }

    class Tree
    {
        public Tree() { }
        public Tree(Plane pln, double height, bool unitary = false)
        {
            mPln = pln;
            mHeight = height;
            mUnitary = unitary;

            maxStdR = height * 0.5;
            minStdR = height * treeSepParam * 0.5;
            stepR = (maxStdR - minStdR) / (numLayer - 1);
        }

        // draw the trees
        public (bool, string) Draw(int phase)
        {
            // record phase
            mCurPhase = phase;

            // ! draw tree trunks
            if (mHeight <= 0)
                return (false, "The height of the tree needs to be > 0.");
            if (phase > 12 || phase <= 0)
                return (false, "Phase out of range ([1, 12] for non-unitary tree).");

            var treeTrunk = new Line(mPln.Origin, mPln.Origin + mHeight * mPln.YAxis).ToNurbsCurve();
            treeTrunk.Domain = new Interval(0.0, 1.0);

            var treeBot = treeTrunk.PointAtStart;
            var treeTop = treeTrunk.PointAtEnd;

            var startRatio = 0.1;
            var seq = Utils.Range(startRatio, 1, numLayer - 1).ToList();

            List<double> remapT;
            if (mUnitary)
                remapT = seq.Select(x => Math.Sqrt(startRatio) + x / (1 - startRatio) * (1 - Math.Sqrt(startRatio))).ToList();
            else
                remapT = seq.Select(x => Math.Sqrt(x)).ToList();

            List<Curve> trunkCol = remapT.Select(x => new Line(treeBot, mPln.Origin + mHeight * x * mPln.YAxis).ToNurbsCurve() as Curve).ToList();

            mDebug = trunkCol;

            // ! draw elliptical boundary
            var vecCol = new List<Vector3d>();
            Utils.Range(48, 93, numLayer - 1).ToList().ForEach(x =>
            {
                var vec = mPln.YAxis;
                vec.Rotate(Utils.ToRadian(x), mPln.ZAxis);
                vecCol.Add(vec);
            });

            foreach (var (t, i) in trunkCol.Select((t, i) => (t, i)))
            {
                // arc as half canopy 
                var lBnd = new Arc(treeBot, vecCol[i], t.PointAtEnd).ToNurbsCurve();

                var tmpV = new Vector3d(-vecCol[i].X, vecCol[i].Y, vecCol[i].Z);
                var rBnd = new Arc(treeBot, tmpV, t.PointAtEnd).ToNurbsCurve();

                mCircCol_l.Add(lBnd);
                mCircCol_r.Add(rBnd);

                var fullBnd = Curve.JoinCurves(new List<Curve> { lBnd, rBnd }, 0.02)[0];
                mCircCol.Add(fullBnd);
            }

            // ! draw collections of branches
            // branchPts
            var branchingPt = new List<Point3d>();
            var tCol = new List<double>();

            foreach (var c in mCircCol)
            {
                var events = Intersection.CurveCurve(treeTrunk.ToNurbsCurve(), c, 0.01, 0.01);

                if (events.Count > 1)
                {
                    if (mPln.Origin.DistanceTo(events[1].PointA) > 1e-3)
                    {
                        branchingPt.Add(events[1].PointA);
                        tCol.Add(events[1].ParameterA);
                    }
                    else
                    {
                        branchingPt.Add(events[0].PointB);
                        tCol.Add(events[0].ParameterB);
                    }
                }
            }

            #region phase < mMatureIdx
            // ! idx determination
            var curIdx = (phase - 1) * 2;
            var trimN = mUnitary ? phase : Math.Min(phase, mMatureIdx - 1);
            var trimIdx = (trimN - 1) * 2;


            // ! canopy
            var canopyIdx = phase < mDyingIdx ? curIdx : (mDyingIdx - 2) * 2;
            mCurCanopy = mCircCol[canopyIdx];
            mCurCanopy.Domain = new Interval(0.0, 1.0);
            mCurCanopy = mCurCanopy.Trim(0.1, 0.9);

            // ! branches
            var lBranchCol = new List<Curve>();
            var rBranchCol = new List<Curve>();
            var lBranchVec = new Vector3d(mPln.YAxis);
            var rBranchVec = new Vector3d(mPln.YAxis);

            lBranchVec.Rotate(Utils.ToRadian(mOpenAngle), mPln.ZAxis);
            rBranchVec.Rotate(Utils.ToRadian(-mOpenAngle), mPln.ZAxis);

            if (mUnitary)
                mAngleStep *= 0.35;

            foreach (var (p, i) in branchingPt.Select((p, i) => (p, i)))
            {
                lBranchVec.Rotate(Utils.ToRadian(-mAngleStep * i), mPln.ZAxis);
                rBranchVec.Rotate(Utils.ToRadian(mAngleStep * i), mPln.ZAxis);

                lBranchCol.Add(new Line(p, p + 1000 * lBranchVec).ToNurbsCurve());
                rBranchCol.Add(new Line(p, p + 1000 * rBranchVec).ToNurbsCurve());
            }

            // phase out of range
            if (trimIdx >= trunkCol.Count)
                return (false, "Phase out of range ([1, 9] for unitary tree).");

            // side branches: generate the left and right separately based on scaled canopy
            var subL = lBranchCol.GetRange(0, trimIdx);
            subL.ForEach(x => x.Domain = new Interval(0.0, 1.0));
            subL = subL.Select(x => TrimCrv(x, mCurCanopy)).ToList();

            var subR = rBranchCol.GetRange(0, trimIdx);
            subR.ForEach(x => x.Domain = new Interval(0.0, 1.0));
            subR = subR.Select(x => TrimCrv(x, mCurCanopy)).ToList();

            mSideBranch_l.AddRange(subL);
            mSideBranch_r.AddRange(subR);

            mSideBranch = mSideBranch_l.Concat(mSideBranch_r).ToList();

            // trunk
            mCurTrunk = trunkCol[trimIdx];
            mCurTrunk.Domain = new Interval(0.0, 1.0);

            // trimming mCurCanopy to adapt to the current phase 
            mCurCanopy.Domain = new Interval(0.0, 1.0);
            var param = 0.1 + phase * 0.03;
            mCurCanopy = mCurCanopy.Trim(param, 1 - param);

            // ! split to left/right part for global scaling
            var canRes = mCurCanopy.Split(0.5);
            mCurCanopy_l = canRes[0];
            mCurCanopy_r = canRes[1];

            #endregion

            // branch removal at bottom part
            //if (phase > 6)
            //    mSideBranch = mSideBranch.GetRange(2);

            #region phase >= matureIdx && phase < mDyingIdx

            // top branches: if unitary, we can stop here.
            if (mUnitary)
                return (true, "");

            // - if not unitary tree, then do top branching
            if (phase >= mMatureIdx && phase < mDyingIdx)
            {
                var cPln = mPln.Clone();
                cPln.Translate(new Vector3d(mCurTrunk.PointAtEnd - mPln.Origin));
                mSubBranch_l.Clear();
                mSubBranch_r.Clear();
                mSubBranch.Clear();

                var topB = BiBranching(cPln, phase - mMatureIdx + 1);

                // do the scale transformation
                //var lSca = Transform.Scale(cPln, mScale.Item1, 1, 1);
                //lB.ForEach(x => x.Item1.Transform(lSca));

                //var rSca = Transform.Scale(cPln, mScale.Item2, 1, 1);
                //rB.ForEach(x => x.Item1.Transform(rSca));
                var lB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'l').ToList();
                var rB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'r').ToList();

                mSubBranch_l.AddRange(lB.Select(x => x.Item1));
                mSubBranch_r.AddRange(rB.Select(x => x.Item1));

                mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();
                //mSubBranch.AddRange(lB.Select(x => x.Item1));
                //mSubBranch.AddRange(rB.Select(x => x.Item1));
            }
            #endregion
            #region phase >= dyIngidx
            else if (phase >= mDyingIdx)
            {
                mSideBranch.ForEach(x => x.Domain = new Interval(0.0, 1.0));
                var cPln = mPln.Clone();

                // ! Top branching, corner case
                if (phase == mDyingIdx)
                {
                    // keep top branching (Dec.2022)
                    cPln.Translate(new Vector3d(mCurTrunk.PointAtEnd - mPln.Origin));
                    mSubBranch.Clear();

                    var topB = BiBranching(cPln, mDyingIdx - mMatureIdx);

                    var lB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'l').ToList();
                    var rB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'r').ToList();

                    mSubBranch_l.AddRange(lB.Select(x => x.Item1));
                    mSubBranch_r.AddRange(rB.Select(x => x.Item1));

                    mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();
                }
                else if (phase == mDyingIdx + 1)
                {
                    // for phase 11, keep only the right side of the top branch
                    cPln.Translate(new Vector3d(mCurTrunk.PointAtEnd - mPln.Origin));
                    mSubBranch.Clear();

                    var topB = BiBranching(cPln, mDyingIdx - mMatureIdx);

                    var rB = topB.Where(x => x.Item2 != null && x.Item2.ElementAt(1) == 'r').ToList();
                    mSubBranch_r.AddRange(rB.Select(x => x.Item1));
                    mSubBranch.AddRange(rB.Select(x => x.Item1));
                }
                else
                {
                    mSubBranch.Clear();
                }

                // ! Side new born branch and branched trunk
                if (phase == mDyingIdx)
                {
                    mNewBornBranch = CrvSelection(mSideBranch, 0, 18, 3);
                    mNewBornBranch = mNewBornBranch.Select(x => x.Trim(0.0, 0.3)).ToList();

                    var babyTreeCol = new List<Curve>();
                    foreach (var b in mNewBornBranch)
                    {
                        b.Domain = new Interval(0, 1);
                        List<Point3d> ptMidEnd = new List<Point3d> { b.PointAtEnd };
                        //List<Point3d> ptMidEnd = new List<Point3d> { b.PointAtEnd, b.PointAt(0.5) };

                        foreach (var p in ptMidEnd)
                        {
                            cPln = mPln.Clone();
                            cPln.Translate(new Vector3d(p - mPln.Origin));

                            var cTree = new Tree(cPln, mHeight / 3.0);
                            cTree.Draw(1);

                            babyTreeCol.Add(cTree.mCurCanopy);
                            babyTreeCol.Add(cTree.mCurTrunk);
                        }
                    }
                    mNewBornBranch.AddRange(babyTreeCol);
                }
                else if (phase > mDyingIdx)
                {
                    // base branch
                    mNewBornBranch = CrvSelection(mSideBranch, 0, 16, 5);
                    mNewBornBranch = mNewBornBranch.Select(x => x.Trim(0.0, 0.3)).ToList();

                    // top two branches use polylinecurve
                    var top2 = new List<Curve>();
                    for (int i = 0; i < 4; i += 2)
                    {
                        var c = mNewBornBranch[i];
                        c.Trim(0.0, 0.35);
                        top2.Add(c);
                    }

                    // bottom two branches use curve
                    var bot2 = new List<Curve>();
                    for (int i = 1; i < 4; i += 2)
                    {
                        var c = mNewBornBranch[i].Trim(0.0, 0.2);
                        var pt2 = c.PointAtEnd + mPln.YAxis * c.GetLength() / 2;
                        var newC = NurbsCurve.Create(false, 2, new List<Point3d> { c.PointAtStart, c.PointAtEnd, pt2 });
                        bot2.Add(newC);
                    }

                    // collect new born branches, remove canopy and side branches
                    mNewBornBranch = top2.Concat(bot2).ToList();
                    mCurCanopy_l = null;
                    mCurCanopy_r = null;
                    mCurCanopy = null;

                    // create babyTree
                    var babyCol = new List<Curve>();
                    foreach (var b in mNewBornBranch)
                    {
                        cPln = mPln.Clone();
                        cPln.Translate(new Vector3d(b.PointAtEnd - mPln.Origin));
                        var cTree = new Tree(cPln, mHeight / 3.0);
                        cTree.Draw(phase - mDyingIdx + 1);

                        babyCol.Add(cTree.mCurCanopy);
                        babyCol.Add(cTree.mCurTrunk);
                        babyCol.AddRange(cTree.mSideBranch);

                        // for debugging
                        mDebug.AddRange(cTree.mCircCol);
                    }

                    // attach the babyTree and split the trunk
                    mNewBornBranch.AddRange(babyCol);

                    // process Trunk spliting.
                    var leftTrunk = mCurTrunk.Duplicate() as Curve;
                    leftTrunk.Translate(-mPln.XAxis * mHeight / 150);
                    var rightTrunk = mCurTrunk.Duplicate() as Curve;
                    rightTrunk.Translate(mPln.XAxis * mHeight / 150);
                    mCurTrunk = new PolylineCurve(new List<Point3d> {
                        leftTrunk.PointAtStart, leftTrunk.PointAtEnd,
                        rightTrunk.PointAtEnd, rightTrunk.PointAtStart });
                }

                mCurCanopy_l = null;
                mCurCanopy_r = null;
                mCurCanopy = null;
                mSideBranch_l.Clear();
                mSideBranch_r.Clear();
                mSideBranch.Clear();
            }
            #endregion

            //mDebug = sideBranch;

            return (true, "");
        }

        // scale the trees so that they don't overlap
        public double CalWidth()
        {
            var bbx = mSideBranch.Select(x => x.GetBoundingBox(false)).ToList();
            var finalBbx = mCurTrunk.GetBoundingBox(false);
            bbx.ForEach(x => finalBbx.Union(x));

            // using diag to check which plane the trees are
            Vector3d diag = finalBbx.Max - finalBbx.Min;
            double wid = Vector3d.Multiply(diag, mPln.XAxis);

            return wid;
        }

        public void Scale(in Tuple<double, double> scal)
        {
            var lScal = Transform.Scale(mPln, scal.Item1, 1, 1);
            var rScal = Transform.Scale(mPln, scal.Item2, 1, 1);

            // circumBound
            mCircCol.Clear();
            for (int i = 0; i < mCircCol_l.Count; i++)
            {
                mCircCol_l[i].Transform(lScal);
                mCircCol_r[i].Transform(rScal);
                var fullBnd = Curve.JoinCurves(new List<Curve> { mCircCol_l[i], mCircCol_r[i] }, 0.01)[0];
                mCircCol.Add(fullBnd);
            }

            // side branches
            for (int i = 0; i < mSideBranch_l.Count; i++)
            {
                mSideBranch_l[i].Transform(lScal);
                mSideBranch_r[i].Transform(rScal);
            }
            mSideBranch = mSideBranch_l.Concat(mSideBranch_r).ToList();

            // subbranches
            for (int i = 0; i < mSubBranch_l.Count; i++)
            {
                mSubBranch_l[i].Transform(lScal);
                mSubBranch_r[i].Transform(rScal);
            }
            mSubBranch = mSubBranch_l.Concat(mSubBranch_r).ToList();

            if (mCurCanopy != null)
            {
                mCurCanopy_l.Transform(lScal);
                mCurCanopy_r.Transform(rScal);
                mCurCanopy = Curve.JoinCurves(new List<Curve> { mCurCanopy_l, mCurCanopy_r }, 0.02)[0];
            }

        }

        private List<Curve> CrvSelection(in List<Curve> lst, int startId, int endId, int step)
        {
            List<Curve> res = new List<Curve>();
            for (int i = startId; i < endId; i += step)
            {
                res.Add(lst[i]);
                //res[res.Count - 1].Domain = new Interval(0, 1);
            }

            return res;
        }

        private Curve TrimCrv(in Curve C0, in Curve C1)
        {
            var events = Intersection.CurveCurve(C0, C1, 0.01, 0.01);
            if (events.Count != 0)
                return C0.Trim(0.0, events[0].ParameterA);
            else
                return C0;
        }

        private List<Tuple<Curve, string>> BiBranching(in Plane pln, int step)
        {
            // use a string "i-j-l/r" to record the position of the branch -- end pt represent the branch
            var subPt = new List<Tuple<Point3d, string>> { Tuple.Create(pln.Origin, "n") };
            var subLn = new List<Tuple<Curve, string>> { Tuple.Create(
                new Line(pln.Origin, pln.Origin + pln.YAxis).ToNurbsCurve() as Curve, "") };
            var resCol = new List<Tuple<Curve, string>>();

            for (int i = 0; i < step; i++)
            {
                var ptCol = new List<Tuple<Point3d, string>>();
                var lnCol = new List<Tuple<Curve, string>>();

                var scalingParam = Math.Pow(0.85, (mMatureIdx - mMatureIdx + 1));
                var vecLen = mCurTrunk.GetLength() * 0.1 * scalingParam;
                var angleX = (mOpenAngle - (mMatureIdx + step) * mAngleStep * 3) * scalingParam;

                // for each phase, do bi-branching: 1 node -> 2 branches
                foreach (var (pt, j) in subPt.Select((pt, j) => (pt, j)))
                {
                    var curVec = new Vector3d(subLn[j].Item1.PointAtEnd - subLn[j].Item1.PointAtStart);
                    curVec.Unitize();

                    var vecA = new Vector3d(curVec);
                    var vecB = new Vector3d(curVec);
                    vecA.Rotate(Utils.ToRadian(angleX), mPln.ZAxis);
                    vecB.Rotate(-Utils.ToRadian(angleX), mPln.ZAxis);

                    var endA = subPt[j].Item1 + vecA * vecLen;
                    var endB = subPt[j].Item1 + vecB * vecLen;

                    // trace string: trace of the trace_prevPt + trace_curPt
                    ptCol.Add(Tuple.Create(endA, subPt[j].Item2 + "l"));
                    ptCol.Add(Tuple.Create(endB, subPt[j].Item2 + "r"));

                    lnCol.Add(Tuple.Create(new Line(pt.Item1, endA).ToNurbsCurve() as Curve, pt.Item2 + "l"));
                    lnCol.Add(Tuple.Create(new Line(pt.Item1, endB).ToNurbsCurve() as Curve, pt.Item2 + "r"));
                }

                subPt = ptCol;
                subLn = lnCol;

                resCol.AddRange(subLn);
            }

            return resCol;
        }


        // tree core param
        public Plane mPln;
        public double mHeight;
        public int mCurPhase;
        bool mUnitary = false;

        double mAngleStep = 1.2;
        readonly int numLayer = 18;
        readonly double mOpenAngle = 57;

        // mature and dying range idx
        readonly int mMatureIdx = 6;
        readonly int mDyingIdx = 10;

        // other parameter
        double treeSepParam = 0.2;
        readonly double maxStdR, minStdR, stepR;

        // curve collection
        public Curve mCurCanopy_l;
        public Curve mCurCanopy_r;
        public Curve mCurCanopy;

        public Curve mCurTrunk;

        public List<Curve> mCircCol_l = new List<Curve>();
        public List<Curve> mCircCol_r = new List<Curve>();
        public List<Curve> mCircCol = new List<Curve>();

        public List<Curve> mSideBranch_l = new List<Curve>();
        public List<Curve> mSideBranch_r = new List<Curve>();
        public List<Curve> mSideBranch = new List<Curve>();

        public List<Curve> mSubBranch_l = new List<Curve>();
        public List<Curve> mSubBranch_r = new List<Curve>();
        public List<Curve> mSubBranch = new List<Curve>();

        public List<Curve> mNewBornBranch = new List<Curve>();

        public List<Curve> mDebug = new List<Curve>();

    }

    class TreeRoot
    {
        public TreeRoot() { }
        public TreeRoot(Plane pln, double height, ref SoilMap sMap)
        {
            mPln = pln;
            mAnchor = pln.Origin;
            mSoilMap = sMap;
        }


        // internal variables
        public Point3d mAnchor;
        private Plane mPln;
        private SoilMap mSoilMap;
    }

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
