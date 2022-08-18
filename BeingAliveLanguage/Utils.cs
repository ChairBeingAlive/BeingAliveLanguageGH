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
                var res = kdMap.RadialSearch(kdKey, (float)0.01, 1);
                var strLoc = Utils.PtString(pt);

                if (res.Length == 0)
                {
                    kdMap.Add(kdKey, strLoc);
                    ptMap.TryAdd(strLoc, pt);
                }
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
}
