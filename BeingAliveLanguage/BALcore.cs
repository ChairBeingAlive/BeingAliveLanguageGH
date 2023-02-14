using System;
using System.Linq;
using System.Collections.Generic;
using Rhino.Geometry;
using KdTree;
using System.Collections.Concurrent;
using MathNet.Numerics.Distributions;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Clipper2Lib;
using System.Security.Policy;
using System.Data;
using System.Runtime.Remoting.Messaging;
using MathNet.Numerics;
using Rhino.Collections;
using System.Diagnostics;
using System.CodeDom;
using Eto.Forms;
using System.Windows.Forms.VisualStyles;
using System.Reflection.Emit;
using Eto.Drawing;
using System.Windows.Forms;
using Rhino.Geometry.Intersect;
using MathNet.Numerics.LinearAlgebra;
using Rhino.Geometry.Collections;
using System.Web;
//using Grasshopper.Kernel.Geometry;
//using Grasshopper.Kernel.Geometry;

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
            bnd = bound;
            pln = plane;
            soilT = poly;
            unitL = uL;
        }
    }

    /// <summary>
    /// a basic soil info container.
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

            return targetMin + val / (originMax - originMin) * (targetMax - targetMin);
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

    class balCore
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

        /// <summary>
        /// MainFunc: make a triMap from given rectangle boundary.
        /// </summary>
        public static (double, List<List<PolylineCurve>>) MakeTriMap(ref Rectangle3d rec, int resolution)
        {
            // basic param
            var pln = rec.Plane;
            var hTri = rec.Height / resolution; // height of base triangle
            var sTri = hTri * 2 * Math.Sqrt(3.0) / 3; // side length of base triangle

            var nHorizontal = (int)(rec.Width / sTri * 2);
            var nVertical = resolution;

            // up-triangle's three position vector from bottom left corner
            var vTop = createVec(pln, sTri / 2, hTri);
            var vForward = createVec(pln, sTri, 0);
            List<Vector3d> triUp = new List<Vector3d> { createVec(pln, 0, 0), vForward, vTop };

            // down-triangle's three position vector from top left corner
            var vTopLeft = createVec(pln, 0, hTri);
            var vTopRight = createVec(pln, sTri, hTri);
            List<Vector3d> triDown = new List<Vector3d> { vTopLeft, vForward / 2, vTopRight };

            // collection of the two types
            List<List<Vector3d>> triType = new List<List<Vector3d>> { triUp, triDown };

            // start making triGrid
            var refPt = rec.Corner(0);
            List<List<PolylineCurve>> gridMap = new List<List<PolylineCurve>>();
            for (int i = 0; i < nVertical; i++)
            {
                var pt = Point3d.Add(refPt, vTopLeft * i);
                pt = Point3d.Add(pt, -0.5 * sTri * pln.XAxis); // compensate for the alignment
                gridMap.Add(CreateTriLst(in pt, in pln, vForward, nHorizontal + 1, i % 2, in triType));
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
        private static readonly Func<double, double, double, SoilProperty> soilType = (rSand, rSilt, rClay) =>
        {
            bool isSand = (rClay <= 0.1 && rSand > 0.5 * rClay + 0.85);
            // for loamy sand, use the upper inclined line of loamy sand and exclude the sand part
            bool isLoamySand = (rClay <= 0.15 && rSand > rClay + 0.7) && (!isSand);

            if (rClay > 0.4 && rSand <= 0.45 && rSilt <= 0.4)
                return new SoilProperty("clay", 0.42, 0.30, 0.5);

            else if (rClay > 0.35 && rSand > 0.45)
                return new SoilProperty("sandy clay", 0.36, 0.25, 0.44);

            else if (rClay > 0.4 && rSilt > 0.4)
                return new SoilProperty("silty clay", 0.41, 0.27, 0.52);

            else if (rClay > 0.27 && rClay <= 0.4 && rSand > 0.2 && rSand <= 0.45)
                return new SoilProperty("clay loam", 0.36, 0.22, 0.48);

            else if (rClay > 0.27 && rClay <= 0.4 && rSand <= 0.2)
                return new SoilProperty("silty clay loam", 0.38, 0.22, 0.51);

            else if (rClay > 0.2 && rClay <= 0.35 && rSand > 0.45 && rSilt < 0.27)
                return new SoilProperty("sandy clay loam", 0.27, 0.17, 0.43);

            else if (rClay > 0.07 && rClay <= 0.27 && rSand <= 0.53 && rSilt > 0.28 && rSilt <= 0.5)
                return new SoilProperty("loam", 0.28, 0.14, 0.46);

            else if (rClay <= 0.27 && ((rSilt > 0.5 && rSilt <= 0.8) || (rSilt > 0.8 && rClay > 0.14)))
                return new SoilProperty("silt loam", 0.31, 0.11, 0.48);

            else if (rClay <= 0.14 && rSilt > 0.8)
                return new SoilProperty("silt", 0.3, 0.06, 0.48);

            // three special cases for conditioning
            else if (isSand)
                return new SoilProperty("sand", 0.1, 0.05, 0.46);

            else if (isLoamySand)
                return new SoilProperty("loamy sand", 0.18, 0.08, 0.45);

            else if (((!isLoamySand) && rClay <= 0.2 && rSand > 0.53) || (rClay <= 0.07 && rSand > 0.53 && rSilt <= 0.5))
                return new SoilProperty("sandy loam", 0.18, 0.08, 0.45);


            // default check if no above condition is used
            return new SoilProperty("errorSoil", 0, 0, 0);
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


        /// <summary>
        /// Main Func: divide triMap into subdivisions based on the general soil ratio
        /// </summary>
        public static (List<Polyline>, List<Polyline>, List<Polyline>, SoilProperty)
            DivGeneralSoilMap(in List<Polyline> triL, in double[] ratio, in List<Curve> rock)
        {
            // ratio array order: sand, silt, clay
            var soilData = soilType(ratio[0], ratio[1], ratio[2]);

            // get area
            double totalArea = triL.Sum(x => triArea(x));

            var totalASand = totalArea * ratio[0];
            var totalASilt = totalArea * ratio[1];
            var totalAClay = totalArea * ratio[2];

            // sand
            var numSand = (int)(Math.Round(triL.Count * ratio[0]));
            var sandT = triL.OrderBy(x => Guid.NewGuid()).Take(numSand).ToList();

            // silt
            var preSiltT = triL.Except(sandT).ToList();
            var preSiltTDiv = subDivTriLst(preSiltT);

            double avgPreSiltTArea = preSiltTDiv.Sum(x => triArea(x)) / preSiltTDiv.Count;

            var numSilt = (int)Math.Round(totalASilt / avgPreSiltTArea);
            var siltT = preSiltTDiv.OrderBy(x => Guid.NewGuid()).Take(numSilt).ToList();

            // clay
            var preClayT = preSiltTDiv.Except(siltT).ToList();
            var clayT = subDivTriLst(preClayT);


            // if rock exists, avoid it 
            if (rock.Any() && rock[0] != null)
            {
                var rockLocal = rock;
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
                clayT = clayT.Where(x => !hitRock(x)).ToList();
                siltT = siltT.Where(x => !hitRock(x)).ToList();
                sandT = sandT.Where(x => !hitRock(x)).ToList();
            }

            // return
            return (sandT, siltT, clayT, soilData);
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


        /// <summary>
        /// Get string-based soil info
        /// </summary>
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


        /// <summary>
        /// generate organic matter for soil inner
        /// </summary>
        public static (List<List<Line>>, OrganicMatterProperty) GenOrganicMatterInner(in Rectangle3d bnd, in SoilProperty sInfo, in List<Curve> tri, double dOM)
        {
            var coreRatio = 1 - sInfo.saturation;
            var triPoly = tri.Select(x => Utils.CvtCrvToPoly(x)).ToList();
            var triCore = triPoly.Select(x => OffsetTri(x.Duplicate(), coreRatio)).ToList();

            // compute density based on distance to the soil surface
            List<double> denLst = new List<double>();
            foreach (var t in triPoly)
            {
                bnd.Plane.ClosestParameter(t.CenterPoint(), out double x, out double y);
                denLst.Add(bnd.Height - y);
            }

            // remap density
            var dMin = denLst.Min();
            var dMax = denLst.Max();

            var distDen = denLst.Select(x => CustomExpFit((x - dMin) / (dMax - dMin))).ToList();
            var triLen = tri.Select(x => x.GetLength()).ToList();

            // generate lines
            List<List<Line>> res = new List<List<Line>>();
            for (int i = 0; i < triPoly.Count; i++)
            {
                // for each triangle, divide pts based on the density param, and create OM lines
                int divN = (int)Math.Round(triPoly[i].Length / distDen[i] * dOM / 10) * 3;
                if (divN == 0)
                    continue;

                var omLn = createOM(triCore[i], triPoly[i], divN);
                res.Add(omLn);
            }

            return (res, new OrganicMatterProperty(bnd, distDen.Min(), dOM, triLen.Max() / 3));

        }

        /// <summary>
        /// Main Func: (overload) Generate the top layer organic matter, using params from inner OM
        /// </summary>
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
            int divN = (int)Math.Round(flattenTri[1].ToPolyline().Length / omP.distDen * omP.omDen / 10) * 3;
            var res = coreTri.Zip(flattenTri, (i, o) => createOM(i, o.ToPolyline(), divN)).ToList();

            return res;
        }

        /// <summary>
        /// Main Func: Generate the top layer organic matter
        /// </summary>
        public static List<List<Line>> GenOrganicMatterTop(in Rectangle3d bnd, double uL, int type, double dOM, int layer)
        {
            var omP = new OrganicMatterProperty(bnd, dOM / 10, dOM, uL);
            return GenOrganicMatterTop(omP, type, layer);
        }

        /// <summary>
        /// Main Func: Generate the urban soil organic matter
        /// </summary>
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

            this.totalArea = sBase.soilT.Sum(x => balCore.triArea(x));

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
                var numSand = (int)(Math.Round(postSandT.Count * rSand));

                var ptCen = samplingUtils.uniformSampling(ref this.sBase, (int)(numSand * 1.2));
                tmpPt = ptCen;

                balCore.CreateCentreMap(postSandT, out cenMap);

                // build a kd-map for polygon centre. We need to transform into 2d, otherwise, collision box will overlap
                var kdMap = new KdTree<double, Polyline>(2, new KdTree.Math.DoubleMath(), AddDuplicateBehavior.Skip);
                foreach (var pl in postSandT)
                {
                    var cen = (pl[0] + pl[1] + pl[2]) / 3;
                    var originalCen = cen;
                    cen.Transform(toLocal);
                    kdMap.Add(new[] { cen.X, cen.Y }, pl);
                }

                HashSet<Polyline> sandTPrepare = new HashSet<Polyline>();
                foreach (var pt in ptCen)
                {
                    var tmpP = pt;
                    tmpP.Transform(toLocal);
                    var kdRes = kdMap.GetNearestNeighbours(new[] { tmpP.X, tmpP.Y }, 1);

                    sandTPrepare.Add(kdRes[0].Value);
                }

                sandT = sandTPrepare.OrderBy(x => Guid.NewGuid()).Take(numSand).ToList();
                //sandT = postSandT.OrderBy(x => Guid.NewGuid()).Take(numSand).ToList();
                postSandT = sBase.soilT.Except(sandT).ToList();
            }

            var lv3T = balCore.subDivTriLst(balCore.subDivTriLst(postSandT));
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
            //Transform toLocal = Transform.ChangeBasis(Plane.WorldXY, curPln);
            //Transform toWorld = Transform.ChangeBasis(curPln, Plane.WorldXY);

            // nbMap: mapping of each triangle to the neighbouring triangles
            balCore.CreateNeighbourMap(polyIn, out nbMap);
            // cenMap: mapping of centre to the triangle centre Point3D and triangle polyline
            balCore.CreateCentreMap(polyIn, out cenMap);

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

            // convert relative stone radii for generating distributed points 
            List<double> stoneR = new List<double>();
            foreach (var sz in szLst)
            {
                // sz range in [1, 5] mapped to the corresponding range 
                stoneR.Add(Utils.remap(sz, 1, 5, sBase.unitL, Math.Min(sBase.bnd.Height, sBase.bnd.Width) / 3));
            }

            #region Poisson Disc sampling for stone centres
            // ! generate poisson disc distribution point, scale the pt2d down a bit to avoid point generated near the border
            var weightedR = stoneR.Zip(ratioLst, (x, y) => x * y).Sum() / ratioLst.Sum();
            var pt2d = FastPoisson.GenerateSamples((float)(sBase.bnd.Width), (float)(sBase.bnd.Height), (float)weightedR).ToList();
            var fastCen = pt2d.Aggregate(new System.Numerics.Vector2(), (x, y) => x + y) / pt2d.Count;
            pt2d = pt2d.Select(x => fastCen + (x - fastCen) * (float)0.93).ToList();

            // Notice: stoneCen is not aligned with polyTri cen.
            stoneCen = pt2d.Select(x => curPln.Origin + curPln.XAxis * x.Y + curPln.YAxis * x.X).ToList();
            #endregion

            // ! separate the stoneCen into several clusters according to the number of stone types, and collect the initial triangle
            // we use a new struct "StoneCluster" to store info related to the final stones
            var stoneCol = new List<StoneCluster>(stoneCen.Count);


            #region Stone Count
            // ! Some explanation here: 
            /// to decide the number of stones for each ratio, we actually need to solve a linear programming question of:
            ///
            /// Sum(N_i) = N
            /// Sum(N_i * f_area(stone_i)) = A_i
            /// N_i * f_area(stone_i) = A_i
            ///
            /// To get multiple solutions.
            /// To get a usable solution, we need more assumptions, for instance, N_i ~ ratio_i, etc.
            ///
            /// However, for the two stone type case, we can direct solve the only solution without the linear programming issue:
            /// N_1 * Area(stone_1) = A_1
            /// N_2 * Area(stone_2) = A_2
            /// N_1 + N_2 = N
            /// Area(stone_1) : Area(stone_2) = sz_1 : sz_2

            var unitStoneA = totalArea / pt2d.Count;
            var stoneR_convert = stoneR.Select(x => Math.Sqrt(Math.Pow(x / stoneR.Min(), 2) * unitStoneA)).ToList();
            var unitAreaConvert = szLst.Select(x => (x / szLst.Min() * unitStoneA)).ToList();
            var cntLst = ratioLst.Zip(unitAreaConvert, (r, a) => (int)Math.Round(r * totalArea / a)).ToList();
            // scale count to stone num
            var tmpCnt = cntLst.Sum();
            cntLst = cntLst.Select(x => (int)Math.Round((double)stoneCen.Count / tmpCnt * x)).ToList();
            Debug.Assert(cntLst.Sum() == stoneCen.Count);
            //var cntLst = new List<int>();
            //for (int i = 0; i < ratioLst.Count; i++)
            //{
            //    cntLst.Add((int)Math.Round(ratioLst[i] * totalArea / unitAreaConvert[i]));
            //}
            #endregion

            #region Initialize Stone Collection
            int idxCnt = 0;
            var tmpStoneCen = stoneCen;

            for (int i = 0; i < ratioLst.Count; i++)
            {
                //var cnt = (int)Math.Round(totalArea * ratioLst[i] / (Math.Sqrt(3) / 4 * stoneR[i] * stoneR[i]));
                var curLst = tmpStoneCen.OrderBy(_ => Utils.balRnd.Next()).Take(cntLst[i]).ToList();

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
                stoneTypeArea[st.typeId] += balCore.triArea(cenMap[Utils.PtString(st.cen)].Item2);
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
                    if (stoneTypeArea[curStoneType] < areaLst[curStoneType] && stoneCol[i].GetAveRadius() < stoneR[curStoneType])
                    {
                        stoneCol[i].strIdInside.Add(nearestT); // add to the collection
                        stoneCol[i].strIdNeigh.Remove(nearestT);

                        pickedTriCenStr.Add(nearestT);
                        stoneTypeArea[curStoneType] += balCore.triArea(cenMap[nearestT].Item2); // add up area
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
            this.pln = Plane.WorldXY;
            this.kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath(), AddDuplicateBehavior.Skip);
            this.topoMap = new ConcurrentDictionary<string, List<Tuple<float, string>>>();
            this.ptMap = new ConcurrentDictionary<string, Point3d>();
            this.distNorm = new Normal(3.5, 0.5);

        }

        public SoilMap(in Plane pl, in string mapMode)
        {
            this.pln = pl;
            this.kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath(), AddDuplicateBehavior.Skip);
            this.topoMap = new ConcurrentDictionary<string, List<Tuple<float, string>>>();
            this.ptMap = new ConcurrentDictionary<string, Point3d>();
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
            // if triangle contains a 90deg corner, it is a side-triangle, ignore it.
            for (int i = 0; i < 3; i++)
            {
                var v0 = poly[1] - poly[0];
                var v1 = poly[2] - poly[1];
                var v2 = poly[0] - poly[2];

                double tol = 1e-3;
                if (Math.Abs(Vector3d.Multiply(v0, v1)) < tol ||
                    Math.Abs(Vector3d.Multiply(v1, v2)) < tol ||
                    Math.Abs(Vector3d.Multiply(v2, v0)) < tol)
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
                    //var res = kdMap.RadialSearch(kdKey, (float)0.01, 1);
                    //var strLoc = Utils.PtString(pt);

                    //if (res.Length == 0)
                    //{
                    //kdMap.Add(kdKey, strLoc);
                    ptMap.TryAdd(strLoc, pt);
                    topoMap.TryAdd(strLoc, new List<Tuple<float, string>> {
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, ""),
                        //new Tuple<float, string>(-1, ""),
                        //new Tuple<float, string>(-1, "")
                    });
                }
                //    }
                //}

                //// to have a stable and thorough topoMapping of all pts, we need to separate this step from the above func.
                //private void CreateSectionalTriTopoMap(in Polyline poly)
                //{
                //    for (int i = 0; i < 3; i++)
                //    {
                //        var pt = poly[i];
                //        var strLoc = Utils.PtString(pt);

                List<Point3d> surLst = new List<Point3d> { poly[(i + 1) % 3], poly[(i + 2) % 3] };
                foreach (var pNext in surLst)
                {
                    var vP = pNext - pt;
                    var ang = Utils.ToDegree(Vector3d.VectorAngle(pln.XAxis, vP, pln.ZAxis));

                    if (Math.Abs(ang - 60) < 1e-3)
                        AddNeighbour(strLoc, 0, pt, pNext);
                    //else if (Math.Abs(ang - 90) < 1e-3)
                    //    AddNeighbour(strLoc, 1, pt, pNext);
                    else if (Math.Abs(ang - 120) < 1e-3)
                        AddNeighbour(strLoc, 1, pt, pNext);
                    else if (Math.Abs(ang - 180) < 1e-3)
                        AddNeighbour(strLoc, 2, pt, pNext);
                    else if (Math.Abs(ang - 240) < 1e-3)
                        AddNeighbour(strLoc, 3, pt, pNext);
                    //else if (Math.Abs(ang - 270) < 1e-3)
                    //    AddNeighbour(strLoc, 5, pt, pNext);
                    else if (Math.Abs(ang - 300) < 1e-3)
                        AddNeighbour(strLoc, 4, pt, pNext);
                    else if (Math.Abs(ang) < 1e-3 || Math.Abs(ang - 360) < 1e-3)
                        AddNeighbour(strLoc, 5, pt, pNext);
                    else
                        throw new ArgumentException($"Error: point {strLoc} has no neighbour!");
                }
            }
        }

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
                //var ptLst = polyBag.Aggregate(new List<Point3d>(), (x, y) => (x.ToList().Concat(y.ToList()).ToList()));
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

        public List<string> GetNearestPoint(in Point3d pt, int N)
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

        private int SampleIdx()
        {
            // make sure fall into [2, 5] due to the hex arrangement and index
            var sampleIdx = (int)Math.Round(distNorm.Sample());
            while (sampleIdx < 2 || sampleIdx > 5)
                sampleIdx = (int)Math.Round(distNorm.Sample());

            return sampleIdx;
        }

        public (double, string) GetNextPointAndDistance(in string pt)
        {
            var idx = SampleIdx();

            var (dis, nextPt) = topoMap[pt][idx];
            while (nextPt == "")
            {
                idx = SampleIdx();
                (dis, nextPt) = topoMap[pt][idx];
            }

            return (dis, nextPt);
        }

        public Point3d GetPoint(string strKey)
        {
            return ptMap[strKey];
        }


        public Plane pln;
        public double unitLen = float.MaxValue;
        readonly KdTree<float, string> kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath());
        readonly ConcurrentDictionary<string, List<Tuple<float, string>>> topoMap;
        public ConcurrentDictionary<string, Point3d> ptMap;
        readonly Normal distNorm = new Normal();
        public string mapMode = "sectional";

    }

    class RootSec
    {
        public RootSec()
        {

        }

        public RootSec(in SoilMap map, in Point3d anchor, string rootType = "single")
        {
            sMap = map;
            anc = anchor;
            rType = rootType;
        }

        // rootTyle: 0 - single, 1 - multi(branching)
        public void GrowRoot(double radius)
        {
            // init starting ptKey
            var anchorOnMap = sMap.GetNearestPoint(anc, 1)[0];
            if (anchorOnMap != null)
                frontKey.Add(anchorOnMap);

            // build a distance map from anchor point
            // using euclidian distance, not grid distance for ease
            disMap.Clear();
            foreach (var pt in sMap.ptMap)
            {
                disMap[pt.Key] = pt.Value.DistanceTo(anc);
            }


            // grow root until given radius is reached
            double curR = 0;
            double aveR = 0;

            int branchNum;
            switch (rType)
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

            // 1000 is the limits, in case infinite loop
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
                    var (dis, nextPt) = sMap.GetNextPointAndDistance(in startPt);
                    if (nextFrontKey.Add(nextPt))
                    {
                        crv.Add(new Line(sMap.GetPoint(startPt), sMap.GetPoint(nextPt)));
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
        public List<Line> crv = new List<Line>();

        // internal variables
        HashSet<string> frontKey = new HashSet<string>();
        HashSet<string> nextFrontKey = new HashSet<string>();
        ConcurrentDictionary<string, double> disMap = new ConcurrentDictionary<string, double>();
        Point3d anc = new Point3d();
        SoilMap sMap = new SoilMap();
        string rType = "s";

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

                dir0.Rotate(Utils.ToRadian(rotAng), sMap.pln.Normal);
                dir1.Rotate(Utils.ToRadian(-rotAng), sMap.pln.Normal);

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
                var dir = sMap.pln.PointAt(Math.Cos(ang * i), Math.Sin(ang * i), 0) - sMap.pln.Origin;
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
                v0.Rotate(Utils.ToRadian(30), sMap.pln.Normal);
                v1.Rotate(Utils.ToRadian(-30), sMap.pln.Normal);

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
                var sign = tmpVec * sMap.pln.Normal;
                var ang = (sign >= 0 ? 15 : -15);

                curVec.Rotate(Utils.ToRadian(ang), sMap.pln.Normal);
                BranchExtend(phaseId, curPt, curVec, curLen);
            }
        }


        protected void BranchExtend(int lvId, in Point3d startP, in Vector3d dir, double L)
        {
            var endPtOffGrid = envT ? GrowPointWithEnvEffect(startP, dir * L) : Point3d.Add(startP, dir * L);

            // record
            var ptKey2 = sMap.GetNearestPoint(endPtOffGrid, 2);
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
            var sortingDict = new SortedDictionary<double, Tuple<Curve, char>>();

            // attractor
            foreach (var crv in envA)
            {
                var contain = crv.Contains(pt, sMap.pln, 0.01);
                if (contain == PointContainment.Inside || contain == PointContainment.Coincident)
                    return Point3d.Add(pt, scaledDir * forceInAttactor); // grow faster
                else
                {
                    double dist;
                    if (crv.ClosestPoint(pt, out double t) && (dist = crv.PointAt(t).DistanceTo(pt)) < envDetectingDist)
                        sortingDict[dist] = new Tuple<Curve, char>(crv, 'a');
                }
            }

            //repeller
            foreach (var crv in envR)
            {
                var contain = crv.Contains(pt, sMap.pln, 0.01);
                if (contain == PointContainment.Inside || contain == PointContainment.Coincident)
                    return Point3d.Add(pt, scaledDir * forceInRepeller); // grow slower
                else
                {
                    double dist;
                    if (crv.ClosestPoint(pt, out double t) && (dist = crv.PointAt(t).DistanceTo(pt)) < envDetectingDist)
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
                var (v0, v1) = Utils.GetPtCrvFacingVector(pt, sMap.pln, pair.Value.Item1);

                // enlarge the ray range by 15-deg
                var v0_enlarge = v0;
                var v1_enlarge = v1;
                v0_enlarge.Rotate(Utils.ToRadian(-15), sMap.pln.Normal);
                v1_enlarge.Rotate(Utils.ToRadian(15), sMap.pln.Normal);

                // calcuate angles between dir and the 4 vec
                var ang0 = Utils.SignedVecAngle(scaledDir, v0, sMap.pln.Normal);
                var ang0_rot = Utils.SignedVecAngle(scaledDir, v0_enlarge, sMap.pln.Normal);
                var ang1 = Utils.SignedVecAngle(scaledDir, v1, sMap.pln.Normal);
                var ang1_rot = Utils.SignedVecAngle(scaledDir, v1_enlarge, sMap.pln.Normal);

                // clamp force
                var K = envDetectingDist * envDetectingDist;
                var forceAtt = Math.Min(K / (pair.Key * pair.Key), forceOutAttractor);
                var forceRep = Math.Min(K / (pair.Key * pair.Key), forceOutRepeller);
                var newDir = scaledDir;

                // conditional decision:
                // dir in [vec0_enlarge, vec0] => rotate CCW
                if (ang0 * ang0_rot < 0 && Math.Abs(ang0) < 90 && Math.Abs(ang0_rot) < 90)
                {
                    var rotA = pair.Value.Item2 == 'a' ? -ang0_rot : ang0_rot;
                    newDir.Rotate(Utils.ToRadian(rotA), sMap.pln.Normal);

                    newDir *= (pair.Value.Item2 == 'a' ? forceAtt : forceRep);
                }
                // dir in [vec1, vec1_enlarge] => rotate CW
                else if (ang1 * ang1_rot < 0 && Math.Abs(ang1) < 90 && Math.Abs(ang1_rot) < 90)
                {
                    var rotA = pair.Value.Item2 == 'a' ? -ang1_rot : ang1_rot;
                    newDir.Rotate(Utils.ToRadian(rotA), sMap.pln.Normal);

                    newDir *= (pair.Value.Item2 == 'a' ? forceAtt : forceRep);
                }
                // dir in [vec0, vec1] => grow with force
                else if (ang0 * ang1 < 0 && Math.Abs(ang0) < 90 && Math.Abs(ang1) < 90)
                    newDir *= (pair.Value.Item2 == 'a' ? forceAtt : forceRep);


                ptCol.Add(pt + newDir);
            }

            return ptCol.Aggregate(new Point3d(0, 0, 0), (s, v) => s + v) / ptCol.Count;
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

        double forceInAttactor = 2;
        double forceOutAttractor = 1.5;
        double forceInRepeller = 0.3;
        double forceOutRepeller = 0.5;

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
        Plane mPln;
        double mHeight;
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
