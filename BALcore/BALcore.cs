using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Rhino.Geometry;
using BALcontract;

namespace BALcore
{
    [Export(typeof(IPlugin))]
    public class BALcompute : IPlugin
    {
        //  create a position vector from given 2D coordinates in a plane.
        private readonly Func<Plane, double, double, Vector3d> createVec = (pln, x, y) =>
            pln.XAxis * x + pln.YAxis * y;

        // create a triangle polyline from a set of position vectors.
        private readonly Func<Point3d, List<Vector3d>, Polyline> createTri = (cen, vecs) =>
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
        private void AlignTri(ref Polyline tri, in Plane pln, int type = 0)
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
        private List<PolylineCurve> CreateTriLst(in Point3d pt, in Plane pln, in Vector3d dirVec, int num, int type, in List<List<Vector3d>> triType)
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
        public (double, List<List<PolylineCurve>>) MakeTriMap(ref Rectangle3d rec, int resolution)
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
        private readonly Func<Polyline, double> triArea = poly =>
        {
            var dA = poly[1].DistanceTo(poly[2]);
            var dB = poly[2].DistanceTo(poly[0]);
            var dC = poly[0].DistanceTo(poly[1]);
            var p = (dA + dB + dC) * 0.5;
            return Math.Sqrt(p * (p - dA) * (p - dB) * (p - dC));
        };

        // compute the soil type and water ratio
        private readonly Func<double, double, double, SoilProperty> soilType = (rSand, rSilt, rClay) =>
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
        Func<List<Polyline>, List<Polyline>> subDivTriLst = triLst =>
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
        /// Main Func: divide triMap into subdivisions based on the soil ratio
        /// </summary>
        public (List<Polyline>, List<Polyline>, List<Polyline>, SoilProperty)
            DivBaseMap(in List<Polyline> triL, in double[] ratio, in List<Curve> rock)
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
        private readonly Func<Polyline, double, Polyline> offsetTri = (tri, ratio) =>
        {
            var cen = (tri[0] + tri[1] + tri[2]) / 3;
            var trans = Transform.Scale(cen, ratio);

            tri.Transform(trans);
            return tri;
        };

        /// <summary>
        /// Main Func: offset triangles for soil water data: wilting point, field capacity, etc.
        /// </summary>
        public (List<Polyline>, List<Polyline>, List<Polyline>, List<Polyline>, List<List<Polyline>>, List<List<Polyline>>)
            OffsetWater(in List<Curve> tri, SoilProperty sInfo, double rWater, int denEmbedWater, int denAvailWater)
        {
            // convert to polyline 
            var triPoly = tri.Select(x => Utils.CvtCrvToTriangle(x)).ToList();

            // Datta, Sumon, Saleh Taghvaeian, and Jacob Stivers. Understanding Soil Water Content and Thresholds For Irrigation Management, 2017. https://doi.org/10.13140/RG.2.2.35535.89765.
            var coreRatio = 1 - sInfo.saturation;
            var wpRatio = coreRatio + sInfo.wiltingPoint;
            var fcRatio = coreRatio + sInfo.fieldCapacity;

            // offset the triangles for the 3 specific ratio
            var triCore = triPoly.Select(x => offsetTri(x.Duplicate(), coreRatio)).ToList();
            var triWP = triPoly.Select(x => offsetTri(x.Duplicate(), wpRatio)).ToList();
            var triFC = triPoly.Select(x => offsetTri(x.Duplicate(), fcRatio)).ToList();

            var curWaterLn = triPoly.Select(x => offsetTri(x.Duplicate(), rWater)).ToList();

            // creating hatches for the water content
            List<List<Polyline>> hatchCore = new List<List<Polyline>>();
            List<List<Polyline>> hatchPAW = new List<List<Polyline>>();

            for (int i = 0; i < triCore.Count; i++)
            {
                var tmpL = new List<Polyline>();
                for (int j = 1; j < denEmbedWater + 1; j++)
                {
                    double ratio = j / (denEmbedWater + 1);
                    tmpL.Add(new Polyline(triCore[i].Zip(triWP[i], (x, y) => x * ratio + y * (1 - ratio))));
                }
                hatchCore.Add(tmpL);

                var tmpL2 = new List<Polyline>();
                for (int j = 1; j < denAvailWater + 1; j++)
                {
                    double ratio = j / (denAvailWater + 1);
                    tmpL2.Add(new Polyline(triWP[i].Zip(curWaterLn[i], (x, y) => x * ratio + y * (1 - ratio))));
                }
                hatchPAW.Add(tmpL2);
            }

            return (triCore, triWP, triFC, curWaterLn, hatchCore, hatchPAW);
        }


        /// <summary>
        /// Get string-based soil info
        /// </summary>
        public string SoilText(SoilProperty sProperty)
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
        private readonly Func<double, double> CustomExpFit = x => 0.0210324 * Math.Exp(3.8621 * x);

        /// <summary>
        /// generate organic matter for soil inner
        /// </summary>
        public List<List<Line>> GenOrganicMatterInner(in Rectangle3d bnd, in SoilProperty sInfo, in List<Curve> tri, double dOM)
        {
            var coreRatio = 1 - sInfo.saturation;
            var triPoly = tri.Select(x => Utils.CvtCrvToTriangle(x)).ToList();
            var triCore = triPoly.Select(x => offsetTri(x.Duplicate(), coreRatio)).ToList();

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

            var newDen = denLst.Select(x => CustomExpFit((x - dMin) / (dMax - dMin))).ToList();


            // generate lines
            List<List<Line>> res = new List<List<Line>>();
            for (int i = 0; i < triPoly.Count; i++)
            {
                // for each triangle, divide pts based on the density param, and create OM lines
                int divN = (int)Math.Round(triPoly[i].Length / newDen[i] * dOM / 10) * 3;
                if (divN == 0)
                    continue;
                var param = triCore[i].ToPolylineCurve().DivideByCount(divN, true, out Point3d[] startPt);
                var endPt = param.Select(x => triPoly[i].ToPolylineCurve().PointAt(x)).ToArray();

                var curLn = startPt.Zip(endPt, (s, e) => new Line(s, e)).ToList();
                res.Add(curLn);
            }

            return res;

        }
    }
}
