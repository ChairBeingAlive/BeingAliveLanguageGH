using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rhino.Geometry;

namespace BALcontract
{
    public interface IPlugin 
    {
        (double, List<List<PolylineCurve>>) MakeTriMap(ref Rectangle3d rec, int re);
    }
}
