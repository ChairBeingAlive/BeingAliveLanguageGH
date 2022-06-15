using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Rhino.Geometry;
using BALcontract;

namespace BALcore
{

    [Export(typeof(IPlugin))]
    public class BALcompute : IPlugin
    {

        /// <summary>
        ///  create a position vector from given 2D coordinates in a plane.
        /// </summary>
        private Vector3d createVec(Plane pln, double x, double y)
        {
            return pln.XAxis * x + pln.YAxis * y;
        }

        /// <summary>
        /// create a triangle polyline from a set of position vectors.
        /// </summary>
        private Polyline createTri(ref Point3d cen, List<Vector3d> vecs)
        {
            Polyline ply = new Polyline(4);
            foreach (var v in vecs)
            {
                ply.Add(Point3d.Add(cen, v));
            }
            ply.Add(ply[0]);

            return ply;
        }

        /// <summary>
        /// align the triangles on the border with vertical boundary.
        /// associate with the triUp/triDown point order. type: 0 - startTri, 1 - endTri.
        /// </summary>
        private void alignTri(ref Polyline tri, ref Plane pln, int type = 0)
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

        private List<PolylineCurve> createTriLst(ref Point3d pt, ref Plane pln, Vector3d dirVec, int num, int type, ref List<List<Vector3d>> triType)
        {
            List<PolylineCurve> triLst = new List<PolylineCurve>();

            for (int i = 0; i < num; i++)
            {
                var tmpPt = Point3d.Add(pt, dirVec / 2 * i);
                var triTypeIdx = (type + i % 2) % 2;
                var triPolyline = createTri(ref tmpPt, triType[triTypeIdx]);

                // modify the beginning and end triangle so that the border aligns
                if (i == 0)
                    alignTri(ref triPolyline, ref pln, 0);
                if (i == num - 1)
                    alignTri(ref triPolyline, ref pln, 1);


                triLst.Add(triPolyline.ToPolylineCurve());
            }

            return triLst;
        }


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

            List<List<Vector3d>> triType = new List<List<Vector3d>> { triUp, triDown };


            var refPt = rec.Corner(0);
            List<List<PolylineCurve>> gridMap = new List<List<PolylineCurve>>();
            for (int i = 0; i < nVertical; i++)
            {
                var pt = Point3d.Add(refPt, vTopLeft * i);
                pt = Point3d.Add(pt, -0.5 * sTri * pln.XAxis); // compensate for the alignment
                gridMap.Add(createTriLst(ref pt, ref pln, vForward, nHorizontal + 1, i % 2, ref triType));

            }

            return (sTri, gridMap);
        }

    }
}
