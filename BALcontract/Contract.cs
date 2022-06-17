using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rhino.Geometry;

namespace BALcontract
{
    public interface IPlugin
    {
        // make base triangle map
        (double, List<List<PolylineCurve>>) MakeTriMap(ref Rectangle3d rec, int re);

        // subdiv triangle into different content
        (List<Polyline>, List<Polyline>, List<Polyline>, string) divBaseMap(in List<Polyline> triL, in double[] ratio, in List<Curve> rock);
    }
}
