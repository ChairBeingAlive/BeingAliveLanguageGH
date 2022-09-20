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

namespace BeingAliveLanguage
{

    /// <summary>
    /// The base information of initialized soil, used for soil/root computing.
    /// </summary>
    public struct SoilBase
    {
        public List<Polyline> soilT;
        public double unitL;
        public Plane pln;
        public Rectangle3d bnd;

        public SoilBase(Rectangle3d bound, Plane plane, List<Polyline> poly, double uL)
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

        public static Random balRnd = new Random(Guid.NewGuid().GetHashCode());

        public static double remap(double val, double originMin, double originMax, double targetMin, double targetMax)
        {
            // of original range is 0 length, return 0
            if (originMax - originMin < 1e-5)
            { return 0; }

            return targetMin + val / (originMax - originMin) * (targetMax - targetMin);
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
        private static readonly Func<Polyline, double> triArea = poly =>
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
        static Func<List<Polyline>, List<Polyline>> subDivTriLst = triLst =>
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

        /// <summary>
        /// Main Func: divide triMap into subdivisions based on the urban ratio
        /// </summary>
        public static (List<Polyline>, List<Polyline>, List<Polyline>, List<Polyline>)
            DivUrbanSoilMap(in SoilBase sBase, in double[] ratio, in double relStoneSZ)
        {
            // ratio array order:{ rSand, rClay, rBiochar, rStone}; 

            // get area
            double totalArea = sBase.soilT.Sum(x => triArea(x));

            // ! sand
            List<Polyline> sandT = new List<Polyline>();
            var postSandT = sBase.soilT;
            var totalASand = totalArea * ratio[0];
            if (totalASand > 0)
            {
                var rSand = ratio[0];
                var numSand = (int)(Math.Round(postSandT.Count * rSand));

                sandT = postSandT.OrderBy(x => Guid.NewGuid()).Take(numSand).ToList();
                postSandT = sBase.soilT.Except(sandT).ToList();
            }

            var lv3T = subDivTriLst(subDivTriLst(postSandT));

            // ! stone
            // at this stage, there are a collection of small-level triangles to be grouped into stones.
            var preStoneT = lv3T;
            var rStone = ratio[3];
            double stoneArea = totalArea * rStone;
            double stoneR = 0.5 * Utils.remap(relStoneSZ, 1, 10, sBase.unitL, Math.Min(sBase.bnd.Height, sBase.bnd.Width));

            var (stoneT, postStoneT) = PickAndCluster(sBase, preStoneT, stoneR, stoneArea);

            // ! clay, biochar 
            List<Polyline> clayT = new List<Polyline>();
            var totalAclay = totalArea * ratio[1];
            var postClayT = postStoneT;
            if (totalAclay > 0)
            {
                var rClay = ratio[1];
                var numClay = (int)(Math.Round(lv3T.Count * rClay));
                clayT = postStoneT.OrderBy(x => Guid.NewGuid()).Take(numClay).ToList();
                postClayT = postStoneT.Except(clayT).ToList();
            }

            List<Polyline> biocharT = new List<Polyline>();
            var totalABiochar = totalArea * ratio[2];
            var postBiocharT = postClayT;
            if (totalABiochar > 0)
            {
                var rBiochar = ratio[2];
                var numBiochar = (int)(Math.Round(lv3T.Count * rBiochar));
                biocharT = postClayT.OrderBy(x => Guid.NewGuid()).Take(numBiochar).ToList();
                postBiocharT = postClayT.Except(biocharT).ToList();
            }

            // if there're small triangles left, give it to the bigger 
            if (postBiocharT.Count > 0)
            {
                if (clayT.Count > biocharT.Count)
                    clayT = clayT.Concat(postBiocharT).ToList();
                else
                    biocharT = biocharT.Concat(postBiocharT).ToList();
            }

            // ! offset
            var cPln = sBase.pln;
            // ! calculate the offset distance. map range [1, 10] to [0.9, 0.6]
            var rOffset = Utils.remap(relStoneSZ, 1, 10, 1, 0.8);
            //var rOffset = 2.5;

            var offsetClayT = clayT.Select(x => ClipperUtils.OffsetPolygon(cPln, x, rOffset)).ToList();
            var offsetStoneT = stoneT.Select(x => ClipperUtils.OffsetPolygon(cPln, x, rOffset)).ToList();

            return (offsetClayT, clayT, offsetStoneT, stoneT);
        }

        static public (List<Polyline>, List<Polyline>) PickAndCluster(in SoilBase sBase, in List<Polyline> polyIn, double approxR, double targetArea)
        {
            var stonePoly = new List<Polyline>();
            var restPoly = new List<Polyline>();

            var cenCollection = polyIn.Select(x => ((x[0] + x[1] + x[2]) / 3)).ToList();
            var vertCollection = polyIn.Aggregate(new List<Point3d>(), (x, y) => x.ToList().Concat(y.ToList()).ToList());

            // build a kd-map for polygon centre
            var kdMap = new KdTree<float, Polyline>(3, new KdTree.Math.FloatMath(), AddDuplicateBehavior.Skip);
            Parallel.ForEach(polyIn, pl =>
            {
                var cen = (pl[0] + pl[1] + pl[2]) / 3;
                kdMap.Add(new[] { (float)cen.X, (float)cen.Y, (float)cen.Z }, pl);
            });


            var curPln = sBase.pln;
            var pt2d = FastPoisson.GenerateSamples((float)(sBase.bnd.Width), (float)(sBase.bnd.Height), (float)approxR);
            var stoneCen = pt2d.Select(x => curPln.Origin + curPln.XAxis * x.Y + curPln.YAxis * x.X).ToList();


            // start to boolean stone curves
            double curArea = 0;
            var polyCluster = new List<Curve>(stoneCen.Count);
            for (int i = 0; i < stoneCen.Count; i++)
            {
                var kdRes = kdMap.GetNearestNeighbours(new[] { (float)stoneCen[i].X, (float)stoneCen[i].Y, (float)stoneCen[i].Z }, 1);

                polyCluster.Add(kdRes[0].Value.ToPolylineCurve());
                kdMap.RemoveAt(kdRes[0].Point);
            }


            // grow each stone area until the total area is reached
            bool areaReached = false;
            while (!areaReached)
            {
                // the zeroCnt is used to guarantee that when curArea cannot expand to targetArea, we also stop safely.
                int zeroCnt = 0;
                for (int i = 0; i < stoneCen.Count; i++)
                {
                    var kdRes = kdMap.GetNearestNeighbours(new[] { (float)stoneCen[i].X, (float)stoneCen[i].Y, (float)stoneCen[i].Z }, 1);

                    if (kdRes.Length == 0)
                    {
                        zeroCnt += 1;
                    }
                    else
                    {
                        var tmpCollection = new List<Curve> { polyCluster[i], kdRes[0].Value.ToPolylineCurve() };
                        var booleanRes = Curve.CreateBooleanUnion(tmpCollection, 0.1);

                        if (booleanRes.Length == 1)
                        {
                            curArea += triArea(kdRes[0].Value);
                            kdMap.RemoveAt(kdRes[0].Point);
                            polyCluster[i] = booleanRes[0];
                        }

                        if (curArea >= targetArea)
                        {
                            areaReached = true;
                            break;
                        }
                    }
                }

                // stone cannot expand anymore
                if (zeroCnt == stoneCen.Count)
                    break;
            }

            polyCluster.ForEach(pl =>
            {
                if (pl.TryGetPolyline(out Polyline resPoly))
                {
                    resPoly.MergeColinearSegments(0.1, true);
                    stonePoly.Add(resPoly);
                }
            });

            // find the rest polyline and store
            foreach (var ptKey in kdMap)
            {
                if (kdMap.TryFindValueAt(ptKey.Point, out Polyline pl))
                {
                    restPoly.Add(pl);
                }
            }

            return (stonePoly, restPoly);
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

            var curWaterLn = triPoly.Select(x => OffsetTri(x.Duplicate(), rWater)).ToList();

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

                var tmpL2 = new List<Polyline>();
                for (int j = 1; j < denAvailWater + 1; j++)
                {
                    double ratio = (double)j / (denAvailWater + 1);
                    tmpL2.Add(new Polyline(triWP[i].Zip(curWaterLn[i], (x, y) => x * ratio + y * (1 - ratio))));
                }
                hatchPAW.Add(tmpL2);
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
        private static readonly Func<Polyline, Polyline, int, List<Line>> createOM = (polyout, polyin, divN) =>
        {
            if (divN <= 0)
                return new List<Line>();

            var param = polyin.ToPolylineCurve().DivideByCount(divN, true, out Point3d[] startPt);
            var endPt = param.Select(x => polyout.ToPolylineCurve().PointAt(x)).ToArray();

            var curLn = startPt.Zip(endPt, (s, e) => new Line(s, e)).ToList();

            return curLn;
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

    static class Menu
    {
        public static void SelectMode(GH_Component _this, object sender, EventArgs e, ref string _mode, string _setTo)
        {
            _mode = _setTo;
            _this.Message = _mode.ToUpper();
            _this.ExpireSolution(true);
        }
    }

    static class FastPoisson
    {
        private static int _k = 30; // recommended value from the paper TODO provide a means for configuring this value

        //public struct Vector2
        //{
        //    public float X;
        //    public float Y;

        //    public Vector2(float width, float height)
        //    {
        //        X = width;
        //        Y = height;
        //    }

        //    public DistanceSquared()
        //}

        /// <summary>
        ///     Generates a Poisson distribution of <see cref="Vector2"/> within some rectangular shape defined by <paramref name="height"/> * <paramref name="width"/>.
        /// </summary>
        /// <param name="width">The width of the plane.</param>
        /// <param name="height">The height of the plane.</param>
        /// <param name="radius">The minimum distance between any two points.</param>
        /// <returns>Enumeration of <see cref="Vector2"/> elements where no element is within <paramref name="radius"/> distance to any other element.</returns>
        public static IEnumerable<System.Numerics.Vector2> GenerateSamples(float width, float height, float radius)
        {
            List<System.Numerics.Vector2> samples = new List<System.Numerics.Vector2>();
            Random random = new Random(); // TODO evaluate whether this Random can generate uniformly random numbers

            // cell size to guarantee that each cell within the accelerator grid can have at most one sample
            float cellSize = radius / (float)Math.Sqrt(radius);

            // dimensions of our accelerator grid
            int acceleratorWidth = (int)Math.Ceiling(width / cellSize);
            int acceleratorHeight = (int)Math.Ceiling(height / cellSize);

            // backing accelerator grid to speed up rejection of generated samples
            int[,] accelerator = new int[acceleratorHeight, acceleratorWidth];

            // initializer point right at the center
            System.Numerics.Vector2 initializer = new System.Numerics.Vector2(width / 2, height / 2);

            // keep track of our active samples
            List<System.Numerics.Vector2> activeSamples = new List<System.Numerics.Vector2>();

            activeSamples.Add(initializer);

            // begin sample generation
            while (activeSamples.Count != 0)
            {
                // pop off the most recently added samples and begin generating addtional samples around it
                int index = random.Next(0, activeSamples.Count);
                System.Numerics.Vector2 currentOrigin = activeSamples[index];
                bool isValid = false; // need to keep track whether or not the sample we have meets our criteria

                // attempt to randomly place a point near our current origin up to _k rejections
                for (int i = 0; i < _k; i++)
                {
                    // create a random direction to place a new sample at
                    float angle = (float)(random.NextDouble() * Math.PI * 2);
                    System.Numerics.Vector2 direction;
                    direction.X = (float)Math.Sin(angle);
                    direction.Y = (float)Math.Cos(angle);

                    // create a random distance between r and 2r away for that direction
                    float distance = random.Next((int)(radius * 100), (int)(2 * radius * 100)) / (float)100.0;
                    direction.X *= distance;
                    direction.Y *= distance;

                    // create our generated sample from our currentOrigin plus our new direction vector
                    System.Numerics.Vector2 generatedSample;
                    generatedSample.X = currentOrigin.X + direction.X;
                    generatedSample.Y = currentOrigin.Y + direction.Y;

                    isValid = IsGeneratedSampleValid(generatedSample, width, height, radius, cellSize, samples, accelerator);

                    if (isValid)
                    {
                        activeSamples.Add(generatedSample); // we may be able to add more samples around this valid generated sample later
                        samples.Add(generatedSample);

                        // mark the generated sample as "taken" on our accelerator
                        accelerator[(int)(generatedSample.X / cellSize), (int)(generatedSample.Y / cellSize)] = samples.Count;

                        break; // restart since we successfully generated a point
                    }
                }

                if (!isValid)
                {
                    activeSamples.RemoveAt(index);
                }
            }
            return samples;
        }

        private static bool IsGeneratedSampleValid(System.Numerics.Vector2 generatedSample, float width, float height, float radius, float cellSize, List<System.Numerics.Vector2> samples, int[,] accelerator)
        {
            // is our generated sample within our boundaries?
            if (generatedSample.X < 0 || generatedSample.X >= height || generatedSample.Y < 0 || generatedSample.Y >= width)
            {
                return false; // out of bounds
            }

            int acceleratorX = (int)(generatedSample.X / cellSize);
            int acceleratorY = (int)(generatedSample.Y / cellSize);

            // TODO - for some reason my math for initially have +/- 2 for the area bounds causes some points to just slip
            //        through with a distance just below the radis - bumping this up to +/- 3 solves it at the cost of additional compute
            // create our search area bounds
            int startX = Math.Max(0, acceleratorX - 3);
            int endX = Math.Min(acceleratorX + 3, accelerator.GetLength(0) - 1);

            int startY = Math.Max(0, acceleratorY - 3);
            int endY = Math.Min(acceleratorY + 3, accelerator.GetLength(1) - 1);

            // search within our boundaries for another sample
            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    int index = accelerator[x, y] - 1; // index of sample at this point (if there is one)

                    if (index >= 0) // in each point for the accelerator where we have a sample we put the current size of the number of samples
                    {
                        // compute Euclidean distance squared (more performant as there is no square root)
                        float distance = System.Numerics.Vector2.DistanceSquared(generatedSample, samples[index]);
                        if (distance < radius * radius)
                        {
                            return false; // too close to another point
                        }
                    }
                }
            }
            return true; // this is a valid generated sample as there are no other samples too close to it
        }
    }
}
