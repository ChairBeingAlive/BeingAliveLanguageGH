using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        public SoilMap(in Plane pl)
        {
            this.pln = pl;
            this.kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath());
            this.topoMap = new ConcurrentDictionary<string, List<Tuple<float, string>>>();
            this.ptMap = new ConcurrentDictionary<string, Point3d>();
            this.distNorm = new Normal(3.5, 0.5);
        }

        private void AddNeighbour(string strLoc, int idx, in Point3d refP, in Point3d P)
        {
            var dist = (float)refP.DistanceTo(P);
            if (topoMap[strLoc][idx].Item1 < 0 || dist < topoMap[strLoc][idx].Item1)
            {
                topoMap[strLoc][idx] = new Tuple<float, string>(dist, Utils.PtString(P));
            }
        }

        private void AddTri(in Polyline tri)
        {
            // use kdTree for duplication removal
            // use concurrentDict for neighbour storage 
            for (int i = 0; i < 3; i++)
            {
                var pt = tri[i];
                var floatPT = new Point3f((float)pt.X, (float)pt.Y, (float)pt.Z);

                var kdKey = new[] { floatPT.X, floatPT.Y, floatPT.Z };
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

                List<Point3d> surLst = new List<Point3d> { tri[(i + 1) % 3], tri[(i + 2) % 3] };
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

        public void BuildMap(in List<Polyline> triLst)
        {
            foreach (var tri in triLst)
            {
                this.AddTri(in tri);

                if (tri.Length < unitLen)
                    unitLen = tri.Length;
            }
            // one side length
            unitLen /= 3;
        }

        public string GetNearestPoint(in Point3d pt)
        {
            var resNode = kdMap.GetNearestNeighbours(new float[] { (float)pt.X, (float)pt.Y, (float)pt.Z }, 1);

            // error case
            if (resNode.Length == 0)
            {
                return "";
            }

            return resNode[0].Value;
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


        Plane pln;
        double unitLen = float.MaxValue;
        readonly KdTree<float, string> kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath());
        readonly ConcurrentDictionary<string, List<Tuple<float, string>>> topoMap;
        public ConcurrentDictionary<string, Point3d> ptMap;
        readonly Normal distNorm = new Normal();

    }

    class RootSec
    {
        public RootSec()
        {

        }

        public RootSec(in SoilMap map, in Point3d anchor, int rootType = 1)
        {
            sMap = map;
            anc = anchor;
            rType = rootType;
        }

        // rootTyle: 0 - single, 1 - multi(branching)
        public void GrowRoot(double radius)
        {
            // init starting ptKey
            var anchorOnMap = sMap.GetNearestPoint(anc);
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
                case 0:
                    branchNum = 1;
                    break;
                case 1:
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
        int rType = 2;

    }

    class RootPlanar
    {
        public RootPlanar() { }

        public RootPlanar(in SoilMap soilmap, in Point3d anchor, double scale, int phase, int divN,
            in List<Curve> envA, in List<Curve> envR, bool envToggle)
        { }

        SoilMap sMap = new SoilMap();
    }
}
