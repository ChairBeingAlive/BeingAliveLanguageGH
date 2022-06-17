﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rhino.Geometry;

namespace BALcontract
{

    /// <summary>
    /// a basic soil info container used for both coreLib and ghLib
    /// </summary>
    public struct soilProperty
    {
        public string soilType;
        public double fieldCapacity;
        public double wiltingPoint;
        public double saturation;

        public soilProperty(string st, double fc, double wp, double sa)
        {
            soilType = st;
            fieldCapacity = fc;
            wiltingPoint = wp;
            saturation = sa;
        }
    }

    public static class Utils
    {
        // convert the "Curve" type taken in by GH to a Rhino.Geometry.Polyline
        public static Polyline CvtCrvToTriangle(in Curve c)
        {
            if (c.TryGetPolyline(out Polyline tmp) && tmp.IsClosed)
                return tmp;
            else
                return null;
        }
    }


    public interface IPlugin
    {
        // make base triangle map
        (double, List<List<PolylineCurve>>) MakeTriMap(ref Rectangle3d rec, int re);

        // subdiv triangle into different content
        (List<Polyline>, List<Polyline>, List<Polyline>, soilProperty) DivBaseMap(in List<Polyline> triL, in double[] ratio, in List<Curve> rock);

        // offset triangle based on soil property
        (List<Polyline>, List<Polyline>, List<Polyline>) OffsetWater(in List<Curve> tri, soilProperty sType);
    }
}
