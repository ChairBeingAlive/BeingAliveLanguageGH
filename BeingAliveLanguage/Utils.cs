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
        public SoilMap(in Plane pl)
        {
            this.pln = pl;

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


        protected Plane pln;
        protected double unitLen = float.MaxValue;
        protected KdTree<float, string> kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath());
        protected ConcurrentDictionary<string, List<Tuple<float, string>>> topoMap;
        protected ConcurrentDictionary<string, Point3d> ptMap;

    }
}
