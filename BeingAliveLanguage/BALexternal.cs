using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Rhino.Geometry;
using BeingAliveLanguage;
//using ClipperLib;

namespace BeingAliveLanguage
{
    //using PathD = List<PointD>;
    using PathsD = List<List<PointD>>;

    public static class ClipperUtils
    {
        public static Polyline OffsetPolygon(in Plane pln, in Polyline polyIn, in double ratio)
        {
            // ! 1. construct plane conversion
            Transform toLocal = Transform.ChangeBasis(Plane.WorldXY, pln);
            Transform toWorld = Transform.ChangeBasis(pln, Plane.WorldXY);

            // ! 2. convert rhino polyline to clipper paths, remove last point
            List<double> polyInArray = new List<double>();
            for (int i = 0; i < polyIn.Count - 1; i++)
            {
                //pln.RemapToPlaneSpace(polyIn[i], out Point3d p2d);
                Point3d p2d = polyIn[i];
                p2d.Transform(toLocal);

                polyInArray.Add(p2d.X);
                polyInArray.Add(p2d.Y);
            }


            var polyPath = new PathsD();
            polyPath.Add(Clipper.MakePath(polyInArray.ToArray()));


            // ! 3. offset
            var cen = polyIn.Aggregate(new Point3d(), (x, y) => x + y) / polyIn.Count;
            var aveR = polyIn.Select(x => x.DistanceTo(cen)).ToList().Sum() / polyIn.Count;
            var dis = -(1 - ratio) * aveR;
            //var dis = -1;
            var res = Clipper.InflatePaths(polyPath, dis, JoinType.Miter, EndType.Polygon, Math.Abs(dis) * 5);
            var resOut = res[0].ToList();

            // ! 4. convert back, add last point
            var polyOut = new List<Point3d>();
            for (int i = 0; i < resOut.Count; i++)
            {
                var pt = new Point3d(resOut[i].x, resOut[i].y, 0);
                pt.Transform(toWorld);

                polyOut.Add(pt);
            }


            //int lstOffset = 1;
            //polyOut = polyOut.Skip(lstOffset).Concat(polyOut.Take(lstOffset)).ToList();

            polyOut.Add(polyOut[0]);

            return new Polyline(polyOut);
        }

    }
}
