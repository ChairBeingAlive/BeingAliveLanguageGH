using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Collections.Concurrent;

using KdTree;
using BALcontract;
using MathNet.Numerics.Distributions;

namespace BeingAliveLanguage
{
    class SoilMap
    {
        public SoilMap()
        {
            this.pln = Plane.WorldXY;
            this.kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath());
            this.topoMap = new ConcurrentDictionary<string, List<Tuple<float, string>>>();
            this.ptMap = new ConcurrentDictionary<string, Point3d>();
            this.distNorm = new Normal(3.5, 0.5);

        }

        public SoilMap(in Plane pl, in string mapMode)
        {
            this.pln = pl;
            this.kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath());
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

        private void AddPoly(in Polyline poly)
        {
            // for sectional drawing, all triangle build degree-based point relations 
            if (this.mapMode == "sectional")
            {
                // use kdTree for duplication removal
                // use concurrentDict for neighbour storage 
                for (int i = 0; i < 3; i++)
                {
                    var pt = poly[i];
                    var kdKey = new[] { (float)pt.X, (float)pt.Y, (float)pt.Z };
                    var res = kdMap.RadialSearch(kdKey, (float)0.01, 1);
                    var strLoc = Utils.PtString(pt);

                    if (res.Length == 0)
                    {
                        kdMap.Add(kdKey, strLoc);
                        ptMap.TryAdd(strLoc, pt);
                        topoMap.TryAdd(strLoc, new List<Tuple<float, string>> {
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, ""),
                        new Tuple<float, string>(-1, "")
                    });
                    }

                    List<Point3d> surLst = new List<Point3d> { poly[(i + 1) % 3], poly[(i + 2) % 3] };
                    foreach (var pNext in surLst)
                    {
                        var vP = pNext - pt;
                        var ang = Utils.ToDegree(Vector3d.VectorAngle(pln.XAxis, vP, pln.ZAxis));

                        if (Math.Abs(ang - 60) < 1e-3)
                            AddNeighbour(strLoc, 0, pt, pNext);
                        else if (Math.Abs(ang - 120) < 1e-3)
                            AddNeighbour(strLoc, 1, pt, pNext);
                        else if (Math.Abs(ang - 180) < 1e-3)
                            AddNeighbour(strLoc, 2, pt, pNext);
                        else if (Math.Abs(ang - 240) < 1e-3)
                            AddNeighbour(strLoc, 3, pt, pNext);
                        else if (Math.Abs(ang - 300) < 1e-3)
                            AddNeighbour(strLoc, 4, pt, pNext);
                        else if (Math.Abs(ang) < 1e-3)
                            AddNeighbour(strLoc, 5, pt, pNext);
                    }
                }
            }
            else if (this.mapMode == "planar")
            {
                // for general cases, just build map and remove duplicated points
                for (int i = 0; i < poly.Count - 1; i++)
                {
                    var pt = poly[i];
                    var kdKey = new[] { (float)pt.X, (float)pt.Y, (float)pt.Z };
                    var res = kdMap.RadialSearch(kdKey, (float)0.01, 1);
                    var strLoc = Utils.PtString(pt);

                    if (res.Length == 0)
                    {
                        kdMap.Add(kdKey, strLoc);
                        ptMap.TryAdd(strLoc, pt);
                    }
                }
            }
        }

        public void BuildMap(in List<Polyline> polyLst)
        {
            foreach (var tri in polyLst)
            {
                this.AddPoly(in tri);

                if (tri.Length < unitLen)
                    unitLen = tri.Length;
            }
            // one side length
            unitLen /= 3;
        }

        public List<string> GetNearestPoint(in Point3d pt, int N)
        {
            var resNode = kdMap.GetNearestNeighbours(new float[] { (float)pt.X, (float)pt.Y, (float)pt.Z }, N);

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

        public RootSec(in SoilMap map, in Point3d anchor, string rootType = "s")
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
                case "s":
                    branchNum = 1;
                    break;
                case "m":
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
                var rndIdx = new Random().Next(0, frontKey.Count()) % frontKey.Count;
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

            for (int i = 0; i < 6; i++)
            {
                rCrv.Add(new List<Line>());
                frontId.Add(new List<string>());
                frontDir.Add(new List<Vector3d>());
            }
        }

        public List<List<Line>> GrowRoot()
        {
            for (int i = 0; i < phase; i++)
            {
                switch (i)
                {
                    case 0:
                        DrawPhaseCentre(0);
                        break;
                    case 1:
                        break;
                    default:
                        break;
                }
            }

            return new List<List<Line>>();
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

            // TODO: add AbsorbTrunk
        }


        protected void BranchExtend(int lvId, in Point3d startP, in Vector3d dir, double L)
        {
            var endPtOffGrid = envT ? GrowPointWithEnvEffect(startP, dir * L) : Point3d.Add(startP, dir * L);

            // record
            var ptKey2 = sMap.GetNearestPoint(endPtOffGrid, 2);
            var endPkey = Utils.PtString(endPtOffGrid) == ptKey2[0] ? ptKey2[1] : ptKey2[0];
            var endP = sMap.ptMap[endPkey];

            var branchLn = new Line(startP, endP);

            // draw
            rCrv[lvId].Add(branchLn);
            frontId[lvId].Add(endPkey);
            frontDir[lvId].Add(branchLn.Direction);
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

            // for how attractor and repeller affect the growth, considering the following cases:
            // 1. for a given area inside the detecting range, get the facing segment to the testing point.
            // 2. if the growing dir intersect the seg, growth intensity is affected;
            // 2.1 accumulate the forces
            // 3. if the growing dir doesn't interset the seg, but near the "facing cone", growing direction is affected;
            // 4. otherwise, growing is not affected.


            // each attractor/repeller curve act independently ==> resolve one by one, and average afterwards
            var ptCol = new List<Point3d>();
            foreach (var pair in sortingDict)
            {
                var (v0, v1) = Utils.GetPtCrvFacingVector(pt, sMap.pln, pair.Value.Item1);

                // enlarge the ray range by 15-deg
                var v0_enlarge = v0;
                var v1_enlarge = v1;
                v0_enlarge.Rotate(Utils.ToRadian(-15), sMap.pln.ZAxis);
                v1_enlarge.Rotate(Utils.ToRadian(15), sMap.pln.ZAxis);

                // calcuate angles between dir and the 4 vec
                var ang0 = Utils.SignedVecAngle(scaledDir, v0, sMap.pln.ZAxis);
                var ang0_rot = Utils.SignedVecAngle(scaledDir, v0_enlarge, sMap.pln.ZAxis);
                var ang1 = Utils.SignedVecAngle(scaledDir, v1, sMap.pln.ZAxis);
                var ang1_rot = Utils.SignedVecAngle(scaledDir, v1_enlarge, sMap.pln.ZAxis);

                // conditional decision:
                // dir in [vec0_enlarge, vec0] => rotate CCW


                // dir in [vec0, vec1] => grow with force


                // dir in [vec1, vec1_enlarge] => rotate CW



                // clamp force
                var force = Math.Max(1 / (pair.Key * pair.Key), 2);



            }



            // return the modified point
            return new Point3d();
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
        List<List<string>> frontId = new List<List<string>>();
        List<List<Vector3d>> frontDir = new List<List<Vector3d>>();
    }


    static class Menu
    {
        public static void SelectMode(GH_Component _this, object sender, EventArgs e, ref string _mode, string _setTo)
        {
            _mode = _setTo;
            _this.ExpireSolution(true);
        }

    }
}
