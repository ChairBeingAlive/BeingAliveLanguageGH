using Rhino.Geometry;
using KdTree;
using System;
using System.Collections.Generic;
using System.Linq;
using MIConvexHull;

namespace BeingAliveLanguage
{
  namespace BalCore
  {
    static class MeshUtils
    {
      // create a convex hull mesh from a list of points
      public static Mesh CreateCvxHull(in List<Point3d> inPt)
      {
        var cvxPt = inPt.Select(p =>
                        new DefaultVertex { Position = new[] { p.X, p.Y, p.Z } }).ToList();

        var mesh = new Mesh();
        var hull = ConvexHull.Create(cvxPt).Result;
        var convexHullVertices = hull.Points.ToArray();

        foreach (var pt in hull.Points)
        {
          double[] pos = pt.Position;
          mesh.Vertices.Add(new Point3d(pos[0], pos[1], pos[2]));
        }

        foreach (var f in hull.Faces)
        {
          int a = Array.IndexOf(convexHullVertices, f.Vertices[0]);
          int b = Array.IndexOf(convexHullVertices, f.Vertices[1]);
          int c = Array.IndexOf(convexHullVertices, f.Vertices[2]);
          mesh.Faces.AddFace(a, b, c);
        }
        mesh.RebuildNormals();

        return mesh;
      }

      //public static Mesh CreateLineMesh(in Curve ln)
      //{
      //  var mesh = new Mesh();

      //  // trim trunk rail and prepair for trunk mesh generation
      //  var trunkRail = ln.Trim(0.0, 0.7);
      //  var radius = trunkRail.GetLength() * 0.2;

      //  mesh = Mesh.CreateFromCurvePipe(trunkRail, radius, 8, 1, MeshPipeCapStyle.Flat, true);

      //  return mesh;
      //}
    }

    static class MathUtils
    {

    }

    static class BrepUtils
    {

    }
  }


  /// <summary>
  /// Utility Class, containing all funcs that needed by the BAL system
  /// </summary>
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

      return targetMin + (val - originMin) / (originMax - originMin) * (targetMax - targetMin);
    }

    // get the closest distance of each point to all points in the same list
    public static void GetLstNearestDist(in List<Point3d> pts, out List<double> distLst)
    {
      var kdMap = new KdTree<float, Point3d>(3, new KdTree.Math.FloatMath());
      foreach (var pt in pts)
      {
        var kdKey = new[] { (float)pt.X, (float)pt.Y, (float)pt.Z };
        kdMap.Add(kdKey, pt);
      }

      distLst = new List<double>();
      foreach (var pt in pts)
      {
        var ptArray = new[] { (float)pt.X, (float)pt.Y, (float)pt.Z };
        var nearest2pt = kdMap.GetNearestNeighbours(ptArray, 2);

        if (nearest2pt.Length < 2)
          distLst.Add(0);
        else
          distLst.Add(nearest2pt[1].Value.DistanceTo(pt));
      }
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

    public static string PtString(in Point3d pt, int dec = 4)
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
    static readonly Dictionary<int, List<double>> correctionFactorPET =
        new Dictionary<int, List<double>>() {
                // north hemisphere
                {  0, new List<double>{ 1.04, 0.94, 1.04, 1.01, 1.04, 1.01, 1.04, 1.04, 1.01, 1.04, 1.01, 1.04 } },
                {  5, new List<double>{ 1.02, 0.93, 1.03, 1.02, 1.06, 1.03, 1.06, 1.05, 1.01, 1.03, 0.99, 1.02 } },
                { 10, new List<double>{ 1.00, 0.91, 1.03, 1.03, 1.08, 1.06, 1.08, 1.07, 1.02, 1.02, 0.98, 0.99 } },
                { 15, new List<double>{ 0.97, 0.91, 1.03, 1.04, 1.11, 1.08, 1.12, 1.08, 1.02, 1.01, 0.95, 0.97 } },
                { 20, new List<double>{ 0.95, 0.90, 1.03, 1.05, 1.13, 1.11, 1.14, 1.11, 1.02, 1.00, 0.93, 0.94 } },
                { 25, new List<double>{ 0.93, 0.89, 1.03, 1.06, 1.15, 1.14, 1.17, 1.12, 1.02, 0.99, 0.91, 0.91 } },
                { 26, new List<double>{ 0.92, 0.88, 1.03, 1.06, 1.15, 1.15, 1.17, 1.12, 1.02, 0.99, 0.91, 0.91 } },
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
                { 45, new List<double>{ 0.80, 0.81, 1.02, 1.13, 1.28 ,1.29 ,1.31 ,1.21, 1.04, 0.94, 0.79, 0.75 } },
                { 46, new List<double>{ 0.79, 0.81, 1.02, 1.13, 1.29, 1.31, 1.32, 1.22, 1.04, 0.94, 0.79, 0.74 } },
                { 47, new List<double>{ 0.77, 0.80, 1.02, 1.14, 1.30, 1.32, 1.33, 1.22, 1.04, 0.93, 0.78, 0.73 } },
                { 48, new List<double>{ 0.76, 0.80, 1.02, 1.14, 1.31, 1.33, 1.34, 1.23, 1.05, 0.93, 0.77, 0.72 } },
                { 49, new List<double>{ 0.75, 0.79, 1.02, 1.14, 1.32, 1.34, 1.35, 1.24, 1.05, 0.93, 0.76, 0.71 } },
                { 50, new List<double>{ 0.74, 0.78, 1.02, 1.15, 1.33, 1.36, 1.37, 1.25, 1.06, 0.92, 0.76, 0.70 } },


                // south hemisphere (use negative deg)
                {-5,  new List<double>{1.06, 0.95, 1.04, 1.00, 1.02, 0.99, 1.02, 1.03, 1.00, 1.05, 1.03, 1.06}},
                {-10, new List<double>{1.08, 0.97, 1.05, 0.99, 1.01, 0.96, 1.00, 1.01, 1.00, 1.06, 1.05, 1.10}},
                {-15, new List<double>{1.12, 0.98, 1.05, 0.98, 0.98, 0.94, 0.97, 1.00, 1.00, 1.07, 1.07, 1.12}},
                {-20, new List<double>{1.14, 1.00, 1.05, 0.97, 0.96, 0.91, 0.95, 0.99, 1.00, 1.08, 1.09, 1.15}},
                {-25, new List<double>{1.17, 1.01, 1.05, 0.96, 0.94, 0.88, 0.93, 0.98, 1.00, 1.10, 1.11, 1.18}},
                {-30, new List<double>{1.20, 1.03, 1.06, 0.95, 0.92, 0.85, 0.90, 0.96, 1.00, 1.12, 1.14, 1.21}},
                {-35, new List<double>{1.23, 1.04, 1.06, 0.94, 0.89, 0.82, 0.87, 0.94, 1.00, 1.13, 1.17, 1.25}},
                {-40, new List<double>{1.27, 1.06, 1.07, 0.93, 0.86, 0.78, 0.84, 0.92, 1.00, 1.15, 1.20, 1.29}},
                {-42, new List<double>{1.28, 1.07, 1.07, 0.92, 0.85, 0.76, 0.82, 0.92, 1.00, 1.16, 1.22, 1.31}},
                {-44, new List<double>{1.30, 1.08, 1.07, 0.92, 0.83, 0.74, 0.81, 0.91, 0.99, 1.17, 1.23, 1.33}},
                {-46, new List<double>{1.32, 1.10, 1.07, 0.91, 0.82, 0.72, 0.79, 0.90, 0.99, 1.17, 1.25, 1.35}},
                {-48, new List<double>{1.34, 1.11, 1.08, 0.90, 0.80, 0.70, 0.76, 0.89, 0.99, 1.18, 1.27, 1.37}},
                {-50, new List<double>{1.37, 1.12, 1.08, 0.89, 0.77, 0.67, 0.74, 0.88, 0.99, 1.19, 1.29, 1.41}},
            };

    public static List<double> GetCorrectionFactorPET(double lat)
    {
      // for lat larger than 50 or smaller than -50, use the +/-50 value.
      lat = lat > 50 ? 50 : lat;
      lat = lat < -50 ? -50 : lat;

      // find upper/lower bound for interpolation
      int lBound = correctionFactorPET.Keys.Where(x => x <= lat).Max();
      int uBound = correctionFactorPET.Keys.Where(x => x >= lat).Min();

      if (lBound == uBound)
        return correctionFactorPET[lBound].ToList();

      var factorL = new List<double>();
      for (int i = 0; i < 12; i++)
      {
        var dat = correctionFactorPET[lBound][i] +
            (lat - lBound) / (uBound - lBound) * (correctionFactorPET[uBound][i] - correctionFactorPET[lBound][i]);
        factorL.Add(dat);
      }

      return factorL;
    }

    // compute transformation between local coordinates and world coordinates
    public static (Transform, Transform) GetTransformation(in Plane localPln, in Plane worldPln)
    {
      var toLocal = Transform.ChangeBasis(worldPln, localPln);
      var toWorld = Transform.ChangeBasis(localPln, worldPln);
      return (toLocal, toWorld);
    }

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
    /// constructTrans: if true, return the transformation to scale the base grid to the given rectangle. -- when constructing main soil grid, set to true. When constructing OM grid, set to false.
    /// </summary>
    public static (double, List<List<PolylineCurve>>, Transform) MakeTriMap(ref Rectangle3d rec, int resolution, BaseGridState gridState = BaseGridState.NonScaledVertical, Transform trans = default)
    {
      // basic param
      var pln = rec.Plane;
      var refPln = rec.Plane.Clone();

      // move plane to the starting corner of the rectangle
      var refPt = rec.Corner(0);
      refPln.Translate(refPt - rec.Plane.Origin);

      double hTri = 1.0;
      double rTri = 1.0;
      int nHorizontal = 1;
      int nVertical = 1;

      double recH = rec.Height;
      double recW = rec.Width;

      //if (resMode == "vertical")
      if (gridState == BaseGridState.ScaledVertical || gridState == BaseGridState.NonScaledVertical)
      {
        hTri = recH / resolution; // height of base triangle
        rTri = hTri * 2 * Math.Sqrt(3.0) / 3; // side length of base triangle

        nHorizontal = (int)(recW / rTri * 2);
        nVertical = resolution;
      }
      //else if (resMode == "horizontal")
      else
      {
        rTri = recW / resolution;
        hTri = rTri / 2.0 * Math.Sqrt(3.0);

        nHorizontal = resolution * 2;
        nVertical = (int)(recH / hTri);
      }

      // up-triangle's three position vector from bottom left corner
      var vTop = createVec(refPln, rTri / 2, hTri);
      var vForward = createVec(refPln, rTri, 0);
      List<Vector3d> triUp = new List<Vector3d> { createVec(refPln, 0, 0), vForward, vTop };

      // down-triangle's three position vector from top left corner
      var vTopLeft = createVec(refPln, 0, hTri);
      var vTopRight = createVec(refPln, rTri, hTri);
      List<Vector3d> triDown = new List<Vector3d> { vTopLeft, vForward / 2, vTopRight };

      // collection of the two types
      List<List<Vector3d>> triType = new List<List<Vector3d>> { triUp, triDown };

      // start making triGrid
      List<List<PolylineCurve>> gridMap = new List<List<PolylineCurve>>();
      for (int i = 0; i < nVertical; i++)
      {
        var pt = Point3d.Add(refPt, vTopLeft * i);
        pt = Point3d.Add(pt, -0.5 * rTri * refPln.XAxis); // compensate for the alignment
        gridMap.Add(CreateTriLst(in pt, in refPln, vForward, nHorizontal + 1, i % 2, in triType));
      }

      // ! As trans will have non-zero value for OM grid, we use this criteria to determine whether the func is used for OM grid or soil grid
      var scalingTrans = new Transform();
      // main grid
      if (trans.IsZero)
      {
        if (gridState == BaseGridState.NonScaledVertical || gridState == BaseGridState.NonScaledHorizontal)
          return (rTri, gridMap, trans); // main grid

        //  otherwise, scale the base grid 
        else if (gridState == BaseGridState.ScaledVertical)
        {
          scalingTrans = Transform.Scale(refPln, recW / (rTri * nHorizontal * 0.5), 1, 1);
        }
        else if (gridState == BaseGridState.ScaledHorizontal)
        {
          scalingTrans = Transform.Scale(refPln, 1, recH / (hTri * nVertical), 1);
        }
      }
      else // OM grid
      {
        if (gridState != BaseGridState.ScaledVertical)
          return (rTri, gridMap, trans); // not (vertial div + scaled), do nothing
        else
          scalingTrans = trans; // for OM grid, use the given transformation
      }

      // scale the triangles
      foreach (var lst in gridMap)
      {
        foreach (var tri in lst)
        {
          tri.Transform(scalingTrans);
        }
      }

      return (rTri, gridMap, scalingTrans);
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
    public static readonly Func<double, double, double, SoilProperty> SoilType = (rSand, rSilt, rClay) =>
    {
      var sPro = new SoilProperty();
      sPro.SetRatio(rSand, rSilt, rClay);

      bool isSand = (rClay <= 0.1 && rSand > 0.5 * rClay + 0.85);
      // for loamy sand, use the upper inclined line of loamy sand and exclude the sand part
      bool isLoamySand = (rClay <= 0.15 && rSand > rClay + 0.7) && (!isSand);

      if (rClay > 0.4 && rSand <= 0.45 && rSilt <= 0.4)
        sPro.setInfo("clay", 0.42, 0.30, 0.5);

      else if (rClay > 0.35 && rSand > 0.45)
        sPro.setInfo("sandy clay", 0.36, 0.25, 0.44);

      else if (rClay > 0.4 && rSilt > 0.4)
        sPro.setInfo("silty clay", 0.41, 0.27, 0.52);

      else if (rClay > 0.27 && rClay <= 0.4 && rSand > 0.2 && rSand <= 0.45)
        sPro.setInfo("clay loam", 0.36, 0.22, 0.48);

      else if (rClay > 0.27 && rClay <= 0.4 && rSand <= 0.2)
        sPro.setInfo("silty clay loam", 0.38, 0.22, 0.51);

      else if (rClay > 0.2 && rClay <= 0.35 && rSand > 0.45 && rSilt <= 0.28)
        sPro.setInfo("sandy clay loam", 0.27, 0.17, 0.43);

      else if (rClay > 0.07 && rClay <= 0.27 && rSand <= 0.53 && rSilt > 0.28 && rSilt <= 0.5)
        sPro.setInfo("loam", 0.28, 0.14, 0.46);

      else if (rClay <= 0.27 && ((rSilt > 0.5 && rSilt <= 0.8) || (rSilt > 0.8 && rClay > 0.14)))
        sPro.setInfo("silt loam", 0.31, 0.11, 0.48);

      else if (rClay <= 0.14 && rSilt > 0.8)
        sPro.setInfo("silt", 0.3, 0.06, 0.48);

      // three special cases for conditioning
      else if (isSand)
        sPro.setInfo("sand", 0.1, 0.05, 0.46);

      else if (isLoamySand)
        sPro.setInfo("loamy sand", 0.18, 0.08, 0.45);

      else if (((!isLoamySand) && rClay <= 0.2 && rSand > 0.53) || (rClay <= 0.07 && rSand > 0.53 && rSilt <= 0.5))
        sPro.setInfo("sandy loam", 0.18, 0.08, 0.45);

      else // default check
        sPro.setInfo("errorSoil", 0, 0, 0);

      return sPro;
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


    // offset using scale mechanism
    public static readonly Func<Polyline, double, Polyline> OffsetTri = (tri, ratio) =>
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


    //! Get string-based soil info
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


    // custom fit {{0, 1}, {0.5, 0.15}, {0.6, 0.1}, {1, 0}}, customized for the organic matter purpose.
    // only works for [0-1] range.
    private static readonly Func<double, double> CustomFit = x => 0.0210324 * Math.Exp(3.8621 * (1 - x));

    // create organic matter around a triangle
    public static readonly Func<Polyline, Polyline, double, List<Line>> createOM = (polyout, polyin, divN) =>
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


    // generate organic matter for soil inner
    public static (List<List<Line>>, OrganicMatterProperty) GenOrganicMatterInner(in SoilBase sBase, in SoilProperty sInfo, in List<Polyline> tri, double dOM)
    {
      var bnd = sBase.bnd;
      var coreRatio = 1 - sInfo.saturation;
      var triCore = tri.Select(x => OffsetTri(x.Duplicate(), coreRatio)).ToList();

      // compute density based on distance to the soil surface, reparametrized to [0, 1]
      List<double> distToSrf = new List<double>();
      foreach (var t in tri)
      {
        bnd.Plane.ClosestParameter(t.CenterPoint(), out double x, out double y);
        distToSrf.Add((bnd.Height - y) / bnd.Height);
      }

      // remap density
      var dMin = distToSrf.Min();
      var dMax = distToSrf.Max();

      var triLen = tri.Select(x => x.Length).ToList();

      // fitting: result in (0, 1), the higher the position, the larger the value
      var distDen = distToSrf.Select(x => CustomFit((x - dMin) / (dMax - dMin))).ToList(); // [0.02, 1.004]

      // generate lines
      List<List<Line>> res = new List<List<Line>>();
      for (int i = 0; i < tri.Count; i++)
      {
        // for each triangle, divide pts based on the density param, and create OM lines
        double triLenRatio = tri[i].Length / (sBase.unitL * 3); // 0.25, 0.5, 1
        int szScalingFactor = 1;
        if (Math.Abs(triLenRatio - 1) < 0.1)
          szScalingFactor = 4;
        else if (Math.Abs(triLenRatio - 0.5) < 0.1)
          szScalingFactor = 2;
        else if (Math.Abs(triLenRatio - 0.25) < 0.1)
          szScalingFactor = 1;

        int divN = 3 * (int)Math.Round(10 * distDen[i] * dOM) * szScalingFactor;
        if (divN == 0)
          continue;

        var omLn = createOM(triCore[i], tri[i], divN);
        res.Add(omLn);
      }

      return (res, new OrganicMatterProperty(sBase, dOM));
    }

    //! Main Func: Generate the top layer organic matter
    public static List<List<Line>> GenOrganicMatterTop(in SoilBase sBase, int type, double dOM, int layer)
    {
      var omP = new OrganicMatterProperty(sBase, dOM);
      return GenOrganicMatterTop(omP, type, layer);
    }

    //! Main Func: (overload) Generate the top layer organic matter, using params from existing OM 
    public static List<List<Line>> GenOrganicMatterTop(in OrganicMatterProperty omProp, int type, int layer)
    {
      int szScalingFactor = (int)Math.Pow(2, type);
      var height = 0.25 * omProp.sBase.unitL * 0.5 * Math.Sqrt(3) * szScalingFactor;

      // create the top OM's boundary based on the soil boundary.
      int horizontalDivNdbl = (int)Math.Floor(omProp.sBnd.Corner(1).DistanceTo(omProp.sBnd.Corner(0)) / omProp.sBase.unitL * 2);
      var intWid = (horizontalDivNdbl / 2 + (horizontalDivNdbl % 2) * 0.5) * omProp.sBase.unitL;

      var cornerB = omProp.sBnd.Corner(3) + intWid * omProp.sBnd.Plane.XAxis * 1.001 + omProp.sBnd.Plane.YAxis * height * layer;
      Rectangle3d topBnd = new Rectangle3d(omProp.sBnd.Plane, omProp.sBnd.Corner(3), cornerB);

      // ! For soil surface OM, always use vertical resolution
      var omGridState = (omProp.sBase.gridState == BaseGridState.ScaledVertical) ? BaseGridState.ScaledVertical : BaseGridState.NonScaledVertical;
      var (_, omTri, _) = MakeTriMap(ref topBnd, layer, omGridState, omProp.sBase.trans);

      var flattenTri = omTri.SelectMany(x => x).ToList();
      var coreTri = flattenTri.Select(x => OffsetTri(x.ToPolyline().Duplicate(), 0.4));

      // generate division number and om lines
      int divN = 3 * (int)Math.Round(omProp.dOM * 10) * szScalingFactor;
      var res = coreTri.Zip(flattenTri, (i, o) => createOM(i, o.ToPolyline(), divN)).ToList();

      return res;
    }

    //! Main Func: Generate the urban soil organic matter
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

    /// <summary>
    /// Use environment to affect the EndPoint.
    /// If the startPt is inside any attractor / repeller area, that area will dominant the effect;
    /// Otherwise, we accumulate weighted (dist-based) effect of all the attractor/repeller area.
    /// </summary>
    public static Point3d ExtendDirByAffector(in Point3d pt, in Vector3d scaledDir,
        in SoilMap sMap, in bool envToggle = false,
        in double envDectectDist = 0.0,
        in List<Curve> envA = null, in List<Curve> envR = null)
    {

      // if the environment is not activated, directly output the extension
      if (!envToggle)
      {
        return pt + scaledDir;
      }

      // temporary value for rooth growth
      double forceInAttactor = 2;
      double forceOutAttractor = 1.5;
      double forceInRepeller = 0.3;
      double forceOutRepeller = 0.5;

      var sortingDict = new SortedDictionary<double, Tuple<Curve, char>>();

      // attractor
      foreach (var crv in envA)
      {
        var contain = crv.Contains(pt, sMap.mPln, 0.01);
        if (contain == PointContainment.Inside || contain == PointContainment.Coincident)
          return Point3d.Add(pt, scaledDir * forceInAttactor); // grow faster
        else
        {
          double dist;
          if (crv.ClosestPoint(pt, out double t) && (dist = crv.PointAt(t).DistanceTo(pt)) < envDectectDist)
            sortingDict[dist] = new Tuple<Curve, char>(crv, 'a');
        }
      }

      //repeller
      foreach (var crv in envR)
      {
        var contain = crv.Contains(pt, sMap.mPln, 0.01);
        if (contain == PointContainment.Inside || contain == PointContainment.Coincident)
          return Point3d.Add(pt, scaledDir * forceInRepeller); // grow slower
        else
        {
          double dist;
          if (crv.ClosestPoint(pt, out double t) && (dist = crv.PointAt(t).DistanceTo(pt)) < envDectectDist)
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
        var (v0, v1) = Utils.GetPtCrvFacingVector(pt, sMap.mPln, pair.Value.Item1);

        // enlarge the ray range by 15-deg
        var v0_enlarge = v0;
        var v1_enlarge = v1;
        v0_enlarge.Rotate(Utils.ToRadian(-15), sMap.mPln.Normal);
        v1_enlarge.Rotate(Utils.ToRadian(15), sMap.mPln.Normal);

        // calcuate angles between dir and the 4 vec
        var ang0 = Utils.SignedVecAngle(scaledDir, v0, sMap.mPln.Normal);
        var ang0_rot = Utils.SignedVecAngle(scaledDir, v0_enlarge, sMap.mPln.Normal);
        var ang1 = Utils.SignedVecAngle(scaledDir, v1, sMap.mPln.Normal);
        var ang1_rot = Utils.SignedVecAngle(scaledDir, v1_enlarge, sMap.mPln.Normal);

        // clamp force
        var K = envDectectDist * envDectectDist;
        var forceAtt = Math.Min(K / (pair.Key * pair.Key), forceOutAttractor);
        var forceRep = Math.Min(K / (pair.Key * pair.Key), forceOutRepeller);
        var newDir = scaledDir;

        // conditional decision:
        // dir in [vec0_enlarge, vec0] => rotate CCW
        if (ang0 * ang0_rot < 0 && Math.Abs(ang0) < 90 && Math.Abs(ang0_rot) < 90)
        {
          var rotA = pair.Value.Item2 == 'a' ? -ang0_rot : ang0_rot;
          newDir.Rotate(Utils.ToRadian(rotA), sMap.mPln.Normal);

          newDir *= (pair.Value.Item2 == 'a' ? forceAtt : forceRep);
        }
        // dir in [vec1, vec1_enlarge] => rotate CW
        else if (ang1 * ang1_rot < 0 && Math.Abs(ang1) < 90 && Math.Abs(ang1_rot) < 90)
        {
          var rotA = pair.Value.Item2 == 'a' ? -ang1_rot : ang1_rot;
          newDir.Rotate(Utils.ToRadian(rotA), sMap.mPln.Normal);

          newDir *= (pair.Value.Item2 == 'a' ? forceAtt : forceRep);
        }
        // dir in [vec0, vec1] => grow with force
        else if (ang0 * ang1 < 0 && Math.Abs(ang0) < 90 && Math.Abs(ang1) < 90)
          newDir *= (pair.Value.Item2 == 'a' ? forceAtt : forceRep);


        ptCol.Add(pt + newDir);
      }

      return ptCol.Aggregate(new Point3d(0, 0, 0), (s, v) => s + v) / ptCol.Count;
    }
  }

}