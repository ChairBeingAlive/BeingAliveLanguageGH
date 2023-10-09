using Grasshopper.GUI;
using KdTree;
using MathNet.Numerics.Distributions;
using Rhino.Geometry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BeingAliveLanguage
{
  class RootNode
  {
    public Point3d pos;
    public List<RootNode> nextNode = new List<RootNode>();
    public Vector3d dir = new Vector3d();

    public int curStep = 0;
    public int lifeSpan = -1; // unlimited; if > 0, then grow to 0.
    public int mBranchLevel = 0;
    public int stepCounting = 0;

    public RootNodeType nType = RootNodeType.Stem;

    public RootNode(in Point3d pt)
    {
      this.pos = pt;
      this.stepCounting = 0;
    }

    public void AddChildNode(in RootNode node, RootNodeType nT = RootNodeType.Stem)
    {
      // pos, distance, direction
      node.curStep = this.curStep + 1;
      node.stepCounting = this.stepCounting + 1;
      node.lifeSpan = this.lifeSpan - 1;
      node.mBranchLevel = this.mBranchLevel;

      node.dir = node.pos - this.pos;
      node.dir.Unitize();


      if (nType == RootNodeType.Side && nT == RootNodeType.Stem)
      {
        throw new InvalidOperationException("root node [side] cannot have root node [stem] as a child.");
      }

      node.nType = nT;
      nextNode.Add(node);
    }
  }

  class SoilMap
  {
    public SoilMap()
    {
      this.mPln = Plane.WorldXY;
      this.kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath(), AddDuplicateBehavior.Skip);
      this.topoMap = new ConcurrentDictionary<string, List<Tuple<float, string>>>();
      this.ptMap = new ConcurrentDictionary<string, Point3d>();
      this.distNorm = new Normal(3.5, 0.5);
    }

    //public SoilMap(in Plane pl, in string mapMode)
    public SoilMap(in Plane pl)
    {
      // kd-tree map
      this.kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath(), AddDuplicateBehavior.Skip);

      // topological map
      this.topoMap = new ConcurrentDictionary<string, List<Tuple<float, string>>>();

      // point map
      this.ptMap = new ConcurrentDictionary<string, Point3d>();

      this.mPln = pl;
      this.distNorm = new Normal(3.5, 0.5);
      //this.mapMode = mapMode;
    }

    private void AddNeighbour(string strLoc, int idx, in Point3d refP, in Point3d P)
    {
      var dist = (float)refP.DistanceTo(P);
      if (topoMap[strLoc][idx].Item1 < 0 || dist < topoMap[strLoc][idx].Item1)
      {
        topoMap[strLoc][idx] = new Tuple<float, string>(dist, Utils.PtString(P));
      }
    }

    // obsolete
    //private void AddSectionalTriPt(in Polyline poly)
    //{
    //    // tolerance for angle in the grid map
    //    double tol = 5; // considering the fact of the scaling, this should be adqueate

    //    // if triangle contains a 90deg corner, it is a side-triangle, ignore it.
    //    for (int i = 0; i < 3; i++)
    //    {
    //        var v0 = poly[1] - poly[0];
    //        var v1 = poly[2] - poly[1];
    //        var v2 = poly[0] - poly[2];

    //        double triTol = 1e-3;
    //        if (Math.Abs(Vector3d.Multiply(v0, v1)) < triTol ||
    //            Math.Abs(Vector3d.Multiply(v1, v2)) < triTol ||
    //            Math.Abs(Vector3d.Multiply(v2, v0)) < triTol)
    //            return;
    //    }

    //    // use kdTree for duplication removal
    //    // use concurrentDict for neighbour storage 
    //    for (int i = 0; i < 3; i++)
    //    {
    //        var pt = poly[i];
    //        var kdKey = new[] { (float)pt.X, (float)pt.Y, (float)pt.Z };
    //        var strLoc = Utils.PtString(pt);
    //        if (kdMap.Add(kdKey, strLoc))
    //        {
    //            ptMap.TryAdd(strLoc, pt);
    //            topoMap.TryAdd(strLoc, new List<Tuple<float, string>> {
    //                new Tuple<float, string>(-1, ""),
    //                new Tuple<float, string>(-1, ""),
    //                new Tuple<float, string>(-1, ""),
    //                new Tuple<float, string>(-1, ""),
    //                new Tuple<float, string>(-1, ""),
    //                new Tuple<float, string>(-1, ""),
    //            });
    //        }

    //        List<Point3d> surLst = new List<Point3d> { poly[(i + 1) % 3], poly[(i + 2) % 3] };
    //        foreach (var pNext in surLst)
    //        {
    //            var vP = pNext - pt;
    //            var ang = Utils.ToDegree(Vector3d.VectorAngle(mPln.XAxis, vP, mPln.ZAxis));

    //            if (Math.Abs(ang - 60) < tol)
    //                AddNeighbour(strLoc, 0, pt, pNext);
    //            else if (Math.Abs(ang - 120) < tol)
    //                AddNeighbour(strLoc, 1, pt, pNext);
    //            else if (Math.Abs(ang - 180) < tol)
    //                AddNeighbour(strLoc, 2, pt, pNext);
    //            else if (Math.Abs(ang - 240) < tol)
    //                AddNeighbour(strLoc, 3, pt, pNext);
    //            else if (Math.Abs(ang - 300) < tol)
    //                AddNeighbour(strLoc, 4, pt, pNext);
    //            else if (Math.Abs(ang) < tol || Math.Abs(ang - 360) < tol)
    //                AddNeighbour(strLoc, 5, pt, pNext);
    //            else
    //                throw new ArgumentException($"Error: point {strLoc} has no neighbour!");
    //        }
    //    }
    //}

    // obsolete
    //public void BuildMap(in ConcurrentBag<Polyline> polyBag)
    //{
    //    // for sectional version, we need to get neighbouring relations.
    //    // cannot use parallel, need sequential.
    //    if (this.mapMode == "sectional")
    //    {
    //        var polyLst = polyBag.ToList();
    //        foreach (var tri in polyLst)
    //        {
    //            // 1. add all pts 
    //            this.AddSectionalTriPt(in tri);
    //            // 2. create topology mapping
    //            //this.CreateSectionalTriTopoMap(in tri);
    //        }

    //        // check topoMap is successfully built
    //        foreach (var m in topoMap)
    //        {
    //            var sumIdx = m.Value.Select(x => x.Item1).Sum();
    //            if (sumIdx == -6)
    //                throw new ArgumentException("Error: Topo map is not built successfully. Check if the plane is aligned with the triangles.");
    //        }


    //    }
    //    // for planar version, adding to the kdTree can be parallel.
    //    else if (this.mapMode == "planar")
    //    {
    //        var ptBag = new ConcurrentBag<Point3d>();
    //        Parallel.ForEach(polyBag, pl =>
    //        {
    //            foreach (var p in pl)
    //                ptBag.Add(p);
    //        });
    //        BuildMap(ptBag);
    //    }

    //    // ! compute unitLen
    //    polyBag.TryPeek(out Polyline tmp);
    //    unitLen = polyBag.Select(x => x.Length).Average() / (tmp.Count - 1);
    //}

    public void BuildMap(
        in ConcurrentBag<Point3d> ptLst,
        in ConcurrentBag<Polyline> polyLst)
    {
      var ptBag = new ConcurrentBag<Point3d>();
      Parallel.ForEach(polyLst, pl =>
      {
        foreach (var p in pl)
          ptBag.Add(p);
      });

      var ptCollection = ptBag.Concat(ptLst);

      Parallel.ForEach(ptCollection, pt =>
      {
        // for general cases, just build map and remove duplicated points
        var kdKey = new[] { (float)pt.X, (float)pt.Y, (float)pt.Z };
        var strLoc = Utils.PtString(pt);
        if (kdMap.Add(kdKey, strLoc))
        {
          ptMap.TryAdd(strLoc, pt);
        }
      });

      // average around 100 random selected pt to its nearest point as unitLen
      var tmpN = (int)Math.Round(Math.Min(ptCollection.Count() * 0.4, 100));
      var pt10 = ptCollection.OrderBy(x => Guid.NewGuid()).Take(tmpN).ToList();
      unitLen = pt10.Select(x =>
      {
        // find the 2 nearest point and measure distance (0 and a p-p dist).
        var res = kdMap.GetNearestNeighbours(new[] { (float)x.X, (float)x.Y, (float)x.Z }, 2);
        var nearest2Dist = res.Select(m => ptMap[m.Value].DistanceTo(x)).ToList();
        return nearest2Dist.Max();
      }).Average();
    }

    public void BuildBound()
    {
      List<double> uLst = new List<double>();
      List<double> vLst = new List<double>();
      foreach (var node in kdMap)
      {
        var pt3d = new Point3d(node.Point[0], node.Point[1], node.Point[2]);
        double u, v;
        if (mPln.ClosestParameter(pt3d, out u, out v))
        {
          uLst.Add(u);
          vLst.Add(v);
        }
      }
      mBndParam = new Tuple<double, double, double, double>(uLst.Min(), uLst.Max(), vLst.Min(), vLst.Max());
    }

    public bool IsOnBound(in Point3d pt)
    {
      double u, v;
      if (mPln.ClosestParameter(pt, out u, out v))
      {
        if ((mBndParam.Item1 - u) * (mBndParam.Item1 - u) < 1e-2
            || (mBndParam.Item2 - u) * (mBndParam.Item2 - u) < 1e-2
            || (mBndParam.Item3 - v) * (mBndParam.Item3 - v) < 1e-2
            || (mBndParam.Item4 - v) * (mBndParam.Item4 - v) < 1e-2)
          return true;
      }

      return false;
    }

    public Point3d GetNearestPoint(in Point3d pt)
    {
      var resNode = kdMap.GetNearestNeighbours(new[] { (float)pt.X, (float)pt.Y, (float)pt.Z }, 1)[0];

      return new Point3d(resNode.Point[0], resNode.Point[1], resNode.Point[2]);
    }

    public string GetNearestPointStr(in Point3d pt)
    {
      var resNode = kdMap.GetNearestNeighbours(new[] { (float)pt.X, (float)pt.Y, (float)pt.Z }, 1)[0];

      return resNode.Value;
    }

    public List<Point3d> GetNearestPoints(in Point3d pt, int N)
    {
      var resNode = kdMap.GetNearestNeighbours(new[] { (float)pt.X, (float)pt.Y, (float)pt.Z }, N);

      // error case
      if (resNode.Length == 0)
      {
        return new List<Point3d>();
      }

      var resL = resNode.Select(x => new Point3d(x.Point[0], x.Point[1], x.Point[2])).ToList();
      return resL;
    }

    public List<string> GetNearestPointsStr(in Point3d pt, int N)
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

    private int SampleIdx(int i0 = 2, int i1 = 5)
    {
      if (i0 > i1)
        return -1;

      // make sure fall into [2, 5] due to the hex arrangement and index
      var sampleIdx = (int)Math.Round(distNorm.Sample());
      while (sampleIdx < i0 || sampleIdx > i1)
        sampleIdx = (int)Math.Round(distNorm.Sample());

      return sampleIdx;
    }

    // sample the next point from idx range [i0, i1], using the current pt
    public (double, string) GetNextPointAndDistance(in string pt, int i0 = 2, int i1 = 5)
    {
      var idx = SampleIdx(i0, i1);

      var (dis, nextPt) = topoMap[pt][idx];
      while (nextPt == "")
      {
        idx = SampleIdx(i0, i1);
        (dis, nextPt) = topoMap[pt][idx];
      }

      return (dis, nextPt);
    }

    public Point3d GetPoint(string strKey)
    {
      return ptMap[strKey];
    }


    public Plane mPln;
    // boundary param based on the basePlane
    Tuple<double, double, double, double> mBndParam = new Tuple<double, double, double, double>(0, 0, 0, 0);

    public double unitLen = float.MaxValue;
    public readonly KdTree<float, string> kdMap = new KdTree<float, string>(3, new KdTree.Math.FloatMath());
    readonly ConcurrentDictionary<string, List<Tuple<float, string>>> topoMap;
    public ConcurrentDictionary<string, Point3d> ptMap;
    readonly Normal distNorm = new Normal();
    //public string mapMode = "sectional";

  }
}
