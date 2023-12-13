using Clipper2Lib;
using Rhino.Geometry;
using System;
using System.Linq;
using System.Collections.Generic;
//using ClipperLib;

namespace BeingAliveLanguage
{
  // ! For offsetting complex polylines (Rhino's build-in offset is not as good)
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
      var refinedPolyln = polyIn.Take(polyIn.Count() - 1).ToList();
      var cen = refinedPolyln.Aggregate(new Point3d(), (x, y) => x + y) / refinedPolyln.Count;
      var aveR = refinedPolyln.Select(x => x.DistanceTo(cen)).ToList().Sum() / refinedPolyln.Count;
      var dis = -(1 - ratio) * aveR * 0.5;

      var res = Clipper.InflatePaths(polyPath, dis, JoinType.Miter, EndType.Polygon, Math.Abs(dis) * 10);
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

  // ! For Poisson Sampling, use the WeightedSamplingElimination Approach in the CppPort project.
  // ! The following approach is obsolete
  public static class SamplingUtils
  {
    static public List<Point3d> uniformSampling(ref SoilBase sBase, int num)
    {
      var pt2d = new List<System.Numerics.Vector2>();

      // calculate approximate radius, based on the max density 0.9069: https://en.wikipedia.org/wiki/Circle_packing
      double maxDen = 0.90;
      var approxR = (float)Math.Sqrt(sBase.bnd.Height * sBase.bnd.Width * maxDen / num / Math.PI);
      float lowB = 0;
      float highB = approxR * 5;

      //search to find the approximate number of sampled points
      while (true)
      {
        pt2d = FastPoisson.GenerateSamples((float)(sBase.bnd.Width), (float)(sBase.bnd.Height), (float)approxR).ToList();

        if (pt2d.Count > num * 1.1)
        {
          lowB = approxR;
        }
        else if (pt2d.Count < num * 0.9)
        {
          highB = approxR;
        }
        else
        {
          break;
        }

        approxR = (highB + lowB) / 2;

      }

      // scale pt from center to avoid points on the edge
      var fastCen = pt2d.Aggregate(new System.Numerics.Vector2(), (x, y) => x + y) / pt2d.Count;
      pt2d = pt2d.Select(x => fastCen + (x - fastCen) * (float)0.97).ToList();

      var curPln = sBase.pln;
      // Notice: stoneCen is not aligned with polyTri cen.
      return pt2d.Select(x => curPln.Origin + curPln.XAxis * x.Y + curPln.YAxis * x.X).ToList();
    }

  }
  public static class FastPoisson
  {
    private static int _k = 30; // recommended value from the paper TODO provide a means for configuring this value

    //public struct Vector2
    //{
    //    public float X;
    //    public float Y;

    //    public Vector2(float width, float height)
    //    {
    //        X = width;
    //        Y = height;
    //    }

    //    public DistanceSquared()
    //}

    /// <summary>
    ///     Generates a Poisson distribution of <see cref="Vector2"/> within some rectangular shape defined by <paramref name="height"/> * <paramref name="width"/>.
    /// </summary>
    /// <param name="width">The width of the plane.</param>
    /// <param name="height">The height of the plane.</param>
    /// <param name="radius">The minimum distance between any two points.</param>
    /// <returns>Enumeration of <see cref="Vector2"/> elements where no element is within <paramref name="radius"/> distance to any other element.</returns>
    public static IEnumerable<System.Numerics.Vector2> GenerateSamples(float width, float height, float radius)
    {
      List<System.Numerics.Vector2> samples = new List<System.Numerics.Vector2>();
      //Random random = new Random(); // TODO evaluate whether this Random can generate uniformly random numbers

      // cell size to guarantee that each cell within the accelerator grid can have at most one sample
      float cellSize = radius / (float)Math.Sqrt(2);
      //float cellSize = radius / (float)Math.Sqrt(radius);

      // dimensions of our accelerator grid
      int acceleratorWidth = (int)Math.Ceiling(width / cellSize);
      int acceleratorHeight = (int)Math.Ceiling(height / cellSize);

      // backing accelerator grid to speed up rejection of generated samples
      int[,] accelerator = new int[acceleratorHeight, acceleratorWidth];

      // initializer point right at the center
      System.Numerics.Vector2 initializer = new System.Numerics.Vector2(width / 2, height / 2);

      // keep track of our active samples
      List<System.Numerics.Vector2> activeSamples = new List<System.Numerics.Vector2>();

      activeSamples.Add(initializer);

      // begin sample generation
      while (activeSamples.Count != 0)
      {
        // pop off the most recently added samples and begin generating addtional samples around it
        int index = Utils.balRnd.Next(0, activeSamples.Count);
        System.Numerics.Vector2 currentOrigin = activeSamples[index];
        bool isValid = false; // need to keep track whether or not the sample we have meets our criteria

        // attempt to randomly place a point near our current origin up to _k rejections
        for (int i = 0; i < _k; i++)
        {
          // create a random direction to place a new sample at
          float angle = (float)(Utils.balRnd.NextDouble() * Math.PI * 2);
          System.Numerics.Vector2 direction;
          direction.X = (float)Math.Sin(angle);
          direction.Y = (float)Math.Cos(angle);

          // create a random distance between r and 2r away for that direction
          float distance = Utils.balRnd.Next((int)(radius * 100), (int)(2 * radius * 100)) / (float)100.0;
          direction.X *= distance;
          direction.Y *= distance;

          // create our generated sample from our currentOrigin plus our new direction vector
          System.Numerics.Vector2 generatedSample;
          generatedSample.X = currentOrigin.X + direction.X;
          generatedSample.Y = currentOrigin.Y + direction.Y;

          isValid = IsGeneratedSampleValid(generatedSample, width, height, radius, cellSize, samples, accelerator);

          if (isValid)
          {
            activeSamples.Add(generatedSample); // we may be able to add more samples around this valid generated sample later
            samples.Add(generatedSample);

            // mark the generated sample as "taken" on our accelerator
            accelerator[(int)(generatedSample.X / cellSize), (int)(generatedSample.Y / cellSize)] = samples.Count;

            break; // restart since we successfully generated a point
          }
        }

        if (!isValid)
        {
          activeSamples.RemoveAt(index);
        }
      }
      return samples;
    }

    private static bool IsGeneratedSampleValid(System.Numerics.Vector2 generatedSample, float width, float height, float radius, float cellSize, List<System.Numerics.Vector2> samples, int[,] accelerator)
    {
      // is our generated sample within our boundaries?
      if (generatedSample.X < 0 || generatedSample.X >= height || generatedSample.Y < 0 || generatedSample.Y >= width)
      {
        return false; // out of bounds
      }

      int acceleratorX = (int)(generatedSample.X / cellSize);
      int acceleratorY = (int)(generatedSample.Y / cellSize);

      // TODO - for some reason my math for initially have +/- 2 for the area bounds causes some points to just slip
      //        through with a distance just below the radis - bumping this up to +/- 3 solves it at the cost of additional compute
      // create our search area bounds
      int startX = Math.Max(0, acceleratorX - 3);
      int endX = Math.Min(acceleratorX + 3, accelerator.GetLength(0) - 1);

      int startY = Math.Max(0, acceleratorY - 3);
      int endY = Math.Min(acceleratorY + 3, accelerator.GetLength(1) - 1);

      // search within our boundaries for another sample
      for (int x = startX; x <= endX; x++)
      {
        for (int y = startY; y <= endY; y++)
        {
          int index = accelerator[x, y] - 1; // index of sample at this point (if there is one)

          if (index >= 0) // in each point for the accelerator where we have a sample we put the current size of the number of samples
          {
            // compute Euclidean distance squared (more performant as there is no square root)
            float distance = System.Numerics.Vector2.DistanceSquared(generatedSample, samples[index]);
            if (distance < radius * radius)
            {
              return false; // too close to another point
            }
          }
        }
      }
      return true; // this is a valid generated sample as there are no other samples too close to it
    }
  }
}
