using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.ApplicationSettings;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeingAliveLanguage
{
    public class BALtree : GH_Component
    {
        public BALtree()
        : base("Tree", "balTree",
              "Generate the BAL tree model.",
              "BAL", "03::plant")
        { }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Plane", "P", "Base plane(s) where the tree(s) is drawn.", GH_ParamAccess.list, Plane.WorldXY);
            pManager.AddNumberParameter("Height", "H", "Height of the tree.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Phase", "p", "Phase of the tree.", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Branch", "B", "Tree branch curves.", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var plnLst = new List<Plane>();
            if (!DA.GetDataList("Plane", plnLst))
            { return; }

            var hLst = new List<double>();
            if (!DA.GetDataList("Height", hLst))
            { return; }

            var phLst = new List<int>();
            if (!DA.GetDataList("Phase", phLst))
            { return; }


            var res = new List<Curve>();
            foreach (var (pln, i) in plnLst.Select((pln, i) => (pln, i)))
            {
                var t = new Tree(pln, hLst[i]);
                t.Draw(phLst[i]);

                // output the curves 
                res.AddRange(t.mTrunkCol);
            }

        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("930148B1-014A-43AA-845C-FB0C711D6AA0");
    }
}
