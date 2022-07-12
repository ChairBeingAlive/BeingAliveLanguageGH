using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rhino.Geometry;
using System.Collections.Concurrent;
using KdTree;
using BALcontract;

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
        }

        public SoilMap(in Plane pl)
        {
            this.pln = pl;
            this.kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath());
            this.topoMap = new ConcurrentDictionary<string, List<Tuple<float, string>>>();
            this.ptMap = new ConcurrentDictionary<string, Point3d>();
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
                var floatPT = new Point3f((float)pt[0], (float)pt[1], (float)pt[2]);

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

        public Point3d GetPoint(string strKey)
        {
            return ptMap[strKey];
        }


        Plane pln;
        double unitLen = float.MaxValue;
        KdTree<float, string> kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath());
        ConcurrentDictionary<string, List<Tuple<float, string>>> topoMap;
        ConcurrentDictionary<string, Point3d> ptMap;

    }

    class Root
    {
        public Root()
        {

        }

        public Root(in SoilMap map, in Point3d anchor, int rootType = 2)
        {
            sMap = map;
            anc = anchor;
            rType = rootType;
        }

        public void GrowRoot(double radius, List<double> distr = null)
        {
            if (distr == null)
                distr = (rType == 4 ? distr4 : distr3);

            // init starting ptKey


            // grow root until given radius is reached

        }

        // public variables
        public List<Line> crv = new List<Line>();

        // internal variables
        HashSet<string> frontKey = new HashSet<string>();
        Point3d anc = new Point3d();
        SoilMap sMap = new SoilMap();
        int rType = 2;

        // default distribution
        List<double> distr3 = new List<double> { 0.1, 0.8, 0.1 };
        List<double> distr4 = new List<double> { 0.05, 0.45, 0.45, 0.05 };
    }
}
