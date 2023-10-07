using KdTree;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BeingAliveLanguage
{
  class SoilGeneral
  {
    public SoilGeneral(in SoilBase sBase, in SoilProperty sInfo, in List<Curve> stone, in int seed)
    {
      mBase = sBase;
      mInfo = sInfo;
      mStone = stone;
      mSeed = seed;

      toLocal = Transform.ChangeBasis(Plane.WorldXY, sBase.pln);
      toWorld = Transform.ChangeBasis(sBase.pln, Plane.WorldXY);
    }

    public SoilGeneral(in SoilBase sBase, in SoilProperty sInfo, in List<Curve> stone, in int seed, in int stage = 5)
    {
      mBase = sBase;
      mInfo = sInfo;
      mStone = stone;
      mSeed = seed;
      mStage = stage;

      toLocal = Transform.ChangeBasis(Plane.WorldXY, sBase.pln);
      toWorld = Transform.ChangeBasis(sBase.pln, Plane.WorldXY);
    }

    public void Build(bool macOS = false)
    {

      var doRndControl = mStage == -1 ? false : true;
      var triLOrigin = mBase.soilT;

      // get area
      double totalArea = triLOrigin.Sum(x => Utils.triArea(x));

      var totalASand = totalArea * mInfo.rSand;
      var totalASilt = totalArea * mInfo.rSilt;
      var totalAClay = totalArea * mInfo.rClay;

      // we randomize the triangle list's sequence to simulate a random-order Poisson Disk sampling 
      var rnd = mSeed >= 0 ? new Random(mSeed) : Utils.balRnd;

      var triL = triLOrigin;
      //var triL = triLOrigin.OrderBy(x => rnd.Next()).ToList();

      //var kdMap = new KdTree<double, Point3d>(2, new KdTree.Math.DoubleMath(), AddDuplicateBehavior.Skip);
      //foreach (var pl in triL)
      //{
      //    var cen = (pl[0] + pl[1] + pl[2]) / 3;
      //    var originalCen = cen;
      //    cen.Transform(toLocal);
      //    kdMap.Add(new[] { cen.X, cen.Y }, originalCen);
      //}

      // sand
      var triCen = triL.Select(x => (x[0] + x[1] + x[2]) / 3).ToList();
      List<Point3d> outSandCen = new List<Point3d>();
      Utils.CreateCentreMap(triL, out cenMap);

      //! sand
      var numSand = (int)Math.Round(triL.Count * mInfo.rSand);
      if (!doRndControl)
      {
        outSandCen = triCen.OrderBy(x => rnd.Next()).Take(numSand).ToList();
      }
      else // RndControl
      {
        if (mStage == 0)
        {
          var nStep = (int)Math.Round(1 / mInfo.rSand);
          // a special case (hidden option to have very regularized grid, regardless of the ratio)
          outSandCen = triCen.Where((x, i) => i % nStep == 0).ToList();
        }
        else
        {
          /*  part 1
            * convert stage to randomness param: 1-10 --> 5% - 95% from Poisson's Disk Sampling, the rest from random sampling.
            * 100% will cause all clay/silt triangle accumulated to the edge if sand ratio > 90%
           */
          int numPoissonSand = Convert.ToInt32(numSand * Utils.remap(mStage, 1.0, 8.0, 1.0, 0.05));
          //BeingAliveLanguageRC.Utils.SampleElim(triCen, mBase.bnd.Area, numPoissonSand, out outSandCen);
          cppUtils.SampleElim(triCen, mBase.bnd.Area, numPoissonSand, out outSandCen);

          // part 2
          var remainingTriCen = triCen.Except(outSandCen).ToList();
          var randomTriCen = remainingTriCen.OrderBy(x => rnd.Next()).Take(numSand - numPoissonSand);

          // combine the two parts
          outSandCen.AddRange(randomTriCen);
        }
      }

      //#region method 2
      //  sample general points, then find the corresponding triangles, final results not as clean as method 1
      //List<Point3d> genPt = new List<Point3d>();
      //List<Point3d> sampledPt = new List<Point3d>();
      //BeingAliveLanguageRC.Utils.SampleElim(mBase.bnd, numSand, out genPt, out sampledPt, mSeed, 1, mStage);

      //var outSandCen = new List<Point3d>();
      //foreach (var pt in sampledPt)
      //{
      //    var tmpP = pt;
      //    tmpP.Transform(toLocal);
      //    var kdRes = kdMap.GetNearestNeighbours(new[] { tmpP.X, tmpP.Y }, 1);
      //    outSandCen.Add(kdRes[0].Value);
      //    kdMap.RemoveAt(kdRes[0].Point);
      //}
      //#endregion

      mSandT = outSandCen.Select(x => cenMap[Utils.PtString(x)].Item2).ToList();

      //! silt
      var preSiltT = triL.Except(mSandT).ToList();
      var preSiltTDiv = Utils.subDivTriLst(preSiltT);
      var preSiltCen = preSiltTDiv.Select(x => (x[0] + x[1] + x[2]) / 3).ToList();
      Utils.CreateCentreMap(preSiltTDiv, out cenMap);
      double avgPreSiltTArea = preSiltTDiv.Sum(x => Utils.triArea(x)) / preSiltTDiv.Count;

      List<Point3d> outSiltCen = new List<Point3d>();


      var numSilt = (int)Math.Round(totalASilt / avgPreSiltTArea);
      if (!doRndControl)
      {
        outSiltCen = preSiltCen.OrderBy(x => rnd.Next()).Take(numSilt).ToList();
      }
      else // with RndControl 
      {
        // part 1
        int numPoissonSilt = Convert.ToInt32(numSilt * Utils.remap(mStage, 1.0, 8.0, 1.0, 0.05));
        //BeingAliveLanguageRC.Utils.SampleElim(preSiltCen, mBase.bnd.Area, numPoissonSilt, out outSiltCen);
        cppUtils.SampleElim(preSiltCen, mBase.bnd.Area, numPoissonSilt, out outSiltCen);

        // part 2
        var curRemainTriCen = preSiltCen.Except(outSiltCen).ToList();
        var curRandomTriCen = curRemainTriCen.OrderBy(x => rnd.Next()).Take(numSilt - numPoissonSilt);

        // combine
        outSiltCen.AddRange(curRandomTriCen);
      }

      mSiltT = outSiltCen.Select(x => cenMap[Utils.PtString(x)].Item2).ToList();

      //! clay
      var preClayT = preSiltTDiv.Except(mSiltT).ToList();
      mClayT = Utils.subDivTriLst(preClayT);


      // if rock exists, avoid it 
      if (mStone.Any() && mStone[0] != null)
      {
        var rockLocal = mStone;
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
        mSandT = mSandT.Where(x => !hitRock(x)).ToList();
        mSiltT = mSiltT.Where(x => !hitRock(x)).ToList();
        mClayT = mClayT.Where(x => !hitRock(x)).ToList();
      }
    }

    public List<Polyline> Collect()
    {
      return mSandT.Concat(mSiltT).Concat(mClayT).ToList();
    }

    // in param
    SoilBase mBase;
    SoilProperty mInfo;
    List<Curve> mStone;
    int mSeed;
    int mStage = -1;

    // out param
    public List<Polyline> mClayT, mSiltT, mSandT;

    // private
    private Dictionary<string, ValueTuple<Point3d, Polyline>> cenMap;

    Transform toLocal;
    Transform toWorld;
  }

  class SoilUrban
  {
    public SoilUrban(in SoilBase sBase, in double rSand, in double rClay, in double rBiochar, in List<double> rStone, in List<double> szStone)
    {
      this.sBase = sBase;
      this.rSand = rSand;
      this.rClay = rClay;
      this.rBiochar = rBiochar;
      this.rStone = rStone;
      this.szStone = szStone;

      totalArea = sBase.soilT.Sum(x => Utils.triArea(x));

      sandT = new List<Polyline>();
      clayT = new List<Polyline>();
      biocharT = new List<Polyline>();
      stonePoly = new List<List<Polyline>>();

      toLocal = Transform.ChangeBasis(Plane.WorldXY, sBase.pln);
      toWorld = Transform.ChangeBasis(sBase.pln, Plane.WorldXY);

    }

    /// <summary>
    /// main func divide triMap into subdivisions based on the urban ratio
    /// </summary>
    public void Build()
    {
      #region Sand    
      //List<Polyline> sandT = new List<Polyline>();
      sandT.Clear();
      var postSandT = sBase.soilT;
      var totalASand = totalArea * rSand;

      if (totalASand > 0)
      {
        // sand
        var triCen = postSandT.Select(x => (x[0] + x[1] + x[2]) / 3).ToList();
        Utils.CreateCentreMap(postSandT, out cenMap);

        // sand
        //todo: add OSX variation
        var numSand = (int)Math.Round(postSandT.Count * rSand);
        //BeingAliveLanguageRC.Utils.SampleElim(triCen, sBase.bnd.Area, numSand, out List<Point3d> outSandCen);
        cppUtils.SampleElim(triCen, sBase.bnd.Area, numSand, out List<Point3d> outSandCen);
        sandT = outSandCen.Select(x => cenMap[Utils.PtString(x)].Item2).ToList();

        //var ptCen = SamplingUtils.uniformSampling(ref this.sBase, (int)(numSand * 1.2));
        //tmpPt = ptCen;
        //BalCore.CreateCentreMap(postSandT, out cenMap);

        // build a kd-map for polygon centre. We need to transform into 2d, otherwise, collision box will overlap
        //var kdMap = new KdTree<double, Polyline>(2, new KdTree.Math.DoubleMath(), AddDuplicateBehavior.Skip);
        //foreach (var pl in postSandT)
        //{
        //    var cen = (pl[0] + pl[1] + pl[2]) / 3;
        //    var originalCen = cen;
        //    cen.Transform(toLocal);
        //    kdMap.Add(new[] { cen.X, cen.Y }, pl);
        //}

        //HashSet<Polyline> sandTPrepare = new HashSet<Polyline>();
        //foreach (var pt in ptCen)
        //{
        //    var tmpP = pt;
        //    tmpP.Transform(toLocal);
        //    var kdRes = kdMap.GetNearestNeighbours(new[] { tmpP.X, tmpP.Y }, 1);

        //    sandTPrepare.Add(kdRes[0].Value);
        //}

        //sandT = sandTPrepare.OrderBy(x => Guid.NewGuid()).Take(numSand).ToList();
        //sandT = postSandT.OrderBy(x => Guid.NewGuid()).Take(numSand).ToList();
        postSandT = sBase.soilT.Except(sandT).ToList();
      }

      var lv3T = Utils.subDivTriLst(Utils.subDivTriLst(postSandT));
      #endregion

      #region Stone
      // at this stage, there are a collection of small-level triangles to be grouped into stones.
      var preStoneT = lv3T;
      var postStoneT = preStoneT;

      if (rStone.Sum() > 0)
      {
        postStoneT = PickAndCluster(preStoneT, rStone, szStone);
      }
      #endregion

      #region clay, biochar 
      var totalAclay = totalArea * rClay;
      var postClayT = postStoneT;
      if (totalAclay > 0)
      {
        var numClay = (int)Math.Round(lv3T.Count * rClay);
        clayT = postStoneT.OrderBy(x => Guid.NewGuid()).Take(numClay).ToList();
        postClayT = postStoneT.Except(clayT).ToList();
      }

      var totalABiochar = totalArea * rBiochar;
      var postBiocharT = postClayT;
      if (totalABiochar > 0)
      {
        var numBiochar = (int)Math.Round(lv3T.Count * rBiochar);
        biocharT = postClayT.OrderBy(x => Guid.NewGuid()).Take(numBiochar).ToList();
        postBiocharT = postClayT.Except(biocharT).ToList();
      }

      #endregion

      // if there're small triangles left, give it to the bigger 
      var leftOverT = postBiocharT;
      if (leftOverT.Count > 0)
      {
        if (clayT == null)
          biocharT = biocharT.Concat(leftOverT).ToList();

        else if (biocharT == null)
          clayT = clayT.Concat(leftOverT).ToList();

        else if (clayT.Count > biocharT.Count)
          clayT = clayT.Concat(leftOverT).ToList();

        else
          biocharT = biocharT.Concat(leftOverT).ToList();
      }
    }


    /// <summary>
    /// Main func: pick and cluster lv3 triangles into stones, according to the input sz and ratio list
    /// 1. randomly generate evenly distributed stone centres
    /// 2. Expand triangle from these centres until stone area reached the target
    /// 3. Collect the rest triangle for clay, biochar, etc.
    /// </summary>
    public List<Polyline> PickAndCluster(in List<Polyline> polyIn, List<double> ratioLst, List<double> szLst)
    {
      var curPln = sBase.pln;
      var singleTriA = Utils.triArea(polyIn[0]);
      //Transform toLocal = Transform.ChangeBasis(Plane.WorldXY, curPln);
      //Transform toWorld = Transform.ChangeBasis(curPln, Plane.WorldXY);

      // nbMap: mapping of each triangle to the neighbouring triangles
      Utils.CreateNeighbourMap(polyIn, out nbMap);
      // cenMap: mapping of centre to the triangle centre Point3D and triangle polyline
      Utils.CreateCentreMap(polyIn, out cenMap);

      List<double> areaLst = ratioLst.Select(x => x * totalArea).ToList(); // the target area for each stone type
      HashSet<string> allTriCenStr = new HashSet<string>(cenMap.Keys);
      HashSet<string> pickedTriCenStr = new HashSet<string>();

      // build a kd-map for polygon centre. We need to transform into 2d, otherwise, collision box will overlap
      var kdMap = new KdTree<double, Point3d>(2, new KdTree.Math.DoubleMath(), AddDuplicateBehavior.Skip);
      foreach (var pl in polyIn)
      {
        var cen = (pl[0] + pl[1] + pl[2]) / 3;
        var originalCen = cen;
        cen.Transform(toLocal);
        kdMap.Add(new[] { cen.X, cen.Y }, originalCen);
      }

      // convert relative stone radii for generating distributed points  24 ~ 64 triangles makes small ~ big stones
      var stoneSzTriLst = szLst.Select(x => (int)Utils.remap(x, 1, 5, 24, 64)).ToList();
      var stoneCntLst = areaLst.Zip(stoneSzTriLst, (a, n) => (int)Math.Round(a / (singleTriA * n))).ToList();


      // ! Poisson Disc sampling for stone centres

      var genCen = new List<Point3d>();
      var stoneCen = new List<Point3d>();

      // scale the bnd a bit to allow clay appears on borders
      //BeingAliveLanguageRC.Utils.SampleElim(sBase.bnd, stoneCntLst.Sum(), out genCen, out stoneCen, -1, 0.93);
      cppUtils.SampleElim(sBase.bnd, stoneCntLst.Sum(), out genCen, out stoneCen, -1, 0.93);

      // ! separate the stoneCen into several clusters according to the number of stone types, and collect the initial triangle
      // we use a new struct "StoneCluster" to store info related to the final stones
      var stoneCol = new List<StoneCluster>(stoneCen.Count);

      // ! Explanation:
      /// to decide the number of stones for each ratio, we actually need to solve a linear programming question of:
      ///
      /// Sum(N_i) = N
      /// Sum(N_i * f_area(stone_i)) = A_i
      /// N_i * f_area(stone_i) = A_i
      /// 
      /// to get multiple solutions.
      ///
      /// To get a usable solution, we need more assumptions, for instance, N_i ~ ratio_i, etc.
      ///
      /// 
      /// However, for the two stone type case, we can direct solve the only solution without the linear programming issue:
      /// 
      /// N_1 * Area(stone_1) = A_1
      /// N_2 * Area(stone_2) = A_2
      /// N_1 + N_2 = N
      /// 
      /// Area(stone_1) : Area(stone_2) = sz_1 : sz_2

      #region Initialize Stone Collection
      int idxCnt = 0;
      var tmpStoneCen = stoneCen;

      //for (int i = ratioLst.Count - 1; i >= 0; i--) // reverse the order, from bigger elements to smaller
      for (int i = 0; i < ratioLst.Count; i++)
      {
        var curLst = new List<Point3d>();
        //BeingAliveLanguageRC.Utils.SampleElim(tmpStoneCen, sBase.bnd.Area, stoneCntLst[i], out curLst);
        cppUtils.SampleElim(tmpStoneCen, sBase.bnd.Area, stoneCntLst[i], out curLst);

        // record centre triangle
        foreach (var pt in curLst)
        {
          var tmpP = pt;
          tmpP.Transform(toLocal);
          var kdRes = kdMap.GetNearestNeighbours(new[] { tmpP.X, tmpP.Y }, 1);

          stoneCol.Add(new StoneCluster(idxCnt, kdRes[0].Value, ref cenMap, ref nbMap));
          kdMap.RemoveAt(kdRes[0].Point);

          // if added to the stone, then also store the picked cenId for all stones
          pickedTriCenStr.Add(Utils.PtString(kdRes[0].Value));
        }

        tmpStoneCen = tmpStoneCen.Except(curLst).ToList();

        idxCnt++; // next stone type
      }
      #endregion

      // ! start to aggregate stone triangles and boolean into bigger ones. The target area for each cluster is stoneArea[i]
      bool areaReached = false;
      List<double> stoneTypeArea = Enumerable.Repeat(0.0, ratioLst.Count).ToList(); // store the total area of each stone type

      // add default centre tri area
      foreach (var st in stoneCol)
      {
        stoneTypeArea[st.typeId] += Utils.triArea(cenMap[Utils.PtString(st.cen)].Item2);
      }

      // idx list, used for randomize sequence when growing stone
      var stoneIndices = Enumerable.Range(0, stoneCol.Count).ToList();
      stoneIndices = stoneIndices.OrderBy(_ => Guid.NewGuid()).ToList();

      while (!areaReached)
      {
        // the recordArea is used to guarantee that when stoneTypeArea cannot expand to targetArea, we also stop safely.
        double recordArea = stoneTypeArea.Sum();
        foreach (var i in stoneIndices)
        {
          // ! 1. select a non-picked triangle in the neighbour set based on distance
          var curStoneType = stoneCol[i].typeId;
          var orderedNeigh = stoneCol[i].strIdNeigh.OrderBy(x => stoneCol[i].distMap[x]);

          string nearestT = "";
          foreach (var orderedId in orderedNeigh)
          {
            if (!pickedTriCenStr.Contains(orderedId))
            {
              nearestT = orderedId;
              break;
            }
          }

          // if no available neighbour, this stone is complete (cannot expand any more)
          if (nearestT == "")
            continue;

          // ! 2. find a neighbour of this triangle, update the area of the stone type
          //if (stoneTypeArea[curStoneType] < areaLst[curStoneType] && stoneCol[i].GetAveRadius() < stoneR[curStoneType])
          if (stoneTypeArea[curStoneType] < areaLst[curStoneType])
          {
            stoneCol[i].strIdInside.Add(nearestT); // add to the collection
            stoneCol[i].strIdNeigh.Remove(nearestT);

            pickedTriCenStr.Add(nearestT);
            stoneTypeArea[curStoneType] += Utils.triArea(cenMap[nearestT].Item2); // add up area
          }

          // ! 3. expand, and update corresponding neighbouring set
          foreach (var it in nbMap[nearestT])
          {
            if (!pickedTriCenStr.Contains(it))
            {
              // add all neighbour that are in the outer set
              stoneCol[i].strIdNeigh.Add(it);
              stoneCol[i].AddToDistMap(it, stoneCol[i].cen.DistanceTo(cenMap[it].Item1));
            }
          }

          // ! 5. compare area condition
          if (stoneTypeArea.Sum() >= areaLst.Sum())
          {
            areaReached = true;
            break; // foreach loop
          }
        }
        // randomize the stone list for the next iteration
        stoneIndices = stoneIndices.OrderBy(_ => Guid.NewGuid()).ToList();

        // stone cannot expand anymore
        if (recordArea == stoneTypeArea.Sum())
          break;
      }

      // ! collect polyline for each stone and boolean
      stonePoly = Enumerable.Range(0, ratioLst.Count).Select(x => new List<Polyline>()).ToList();

      // stoneCollection: for debugging, collection of small-triangle in each stone cluster
      //stoneCollection = new List<List<Polyline>>();
      stoneCol.ForEach(x =>
      {
        x.T = x.strIdInside.Select(id => cenMap[id].Item2).ToList(); // optional
                                                                     //stoneCollection.Add(x.T);

        x.MakeBoolean();
        stonePoly[x.typeId].AddRange(x.bndCrvCol);
      });

      //todo: make correct set boolean of restPoly
      // add back the rest neighbouring triangle of the stone to the main collection
      var restPoly = allTriCenStr.Except(pickedTriCenStr).Select(id => cenMap[id].Item2).ToList();

      return restPoly;
    }


    public void CollectAll(out List<Polyline> allT)
    {
      allT = new List<Polyline>();
    }

    SoilBase sBase;
    readonly double rSand, rClay, rBiochar, totalArea;
    readonly List<double> rStone;
    readonly List<double> szStone;
    public List<Polyline> sandT, clayT, biocharT;
    public List<List<Polyline>> stonePoly;

    public List<Polyline> tmpT;

    public List<Point3d> tmpPt;
    public List<Point3d> stoneCen;
    public List<List<Polyline>> stoneCollection;

    public Dictionary<string, ValueTuple<Point3d, Polyline>> cenMap;
    public Dictionary<string, HashSet<string>> nbMap;

    Transform toLocal;
    Transform toWorld;
  }
}
