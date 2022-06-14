using System.Collections.Generic;
using System.ComponentModel.Composition;
using Rhino.Geometry;
using BALcontract;

namespace BALcore
{
    [Export(typeof(IPlugin))]
    public class BALcompute : IPlugin
    {
        public List<Curve> MakeTriMap(ref Rectangle3d rec, int re)
        {
            return 10.0;
        }

    }
}
