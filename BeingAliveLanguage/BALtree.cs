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
            pManager.AddIntegerParameter("Phase", "phase", "Phase of the tree.", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Circumference", "C", "Circumference Ellipses that controls the boundary of the tree.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Trunk", "T", "Tree trunk curves.", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Canopy", "C", "Tree trunk curves.", GH_ParamAccess.tree);
            pManager.AddCurveParameter("SideBranch", "SB", "Tree side branch curves.", GH_ParamAccess.tree);
            pManager.AddCurveParameter("TopBranch", "TB", "Tree top branch curves.", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Debug", "debug", "Debug curves.", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var plnLst = new List<Rhino.Geometry.Plane>();
            if (!DA.GetDataList("Plane", plnLst))
            { return; }

            var hLst = new List<double>();
            if (!DA.GetDataList("Height", hLst))
            { return; }

            var phLst = new List<int>();
            if (!DA.GetDataList("Phase", phLst))
            { return; }

            var circ = new List<Curve>();
            var canopy = new List<Curve>();
            var trunk = new List<Curve>();
            var sideB = new List<Curve>();
            var topB = new List<Curve>();

            var debug = new List<Curve>();
            foreach (var (pln, i) in plnLst.Select((pln, i) => (pln, i)))
            {
                var t = new Tree(pln, hLst[i]);
                t.Draw(phLst[i]);

                // output the curves 
                trunk.Add(t.mCurTrunk);
                canopy.Add(t.mCurCanopy);
                circ.AddRange(t.mCircCol);
                sideB.AddRange(t.mSideBranch);
                topB.AddRange(t.mSubBranch);


                debug.AddRange(t.mDebug);
            }

            DA.SetDataList("Circumference", circ);
            DA.SetDataList("Canopy", canopy);
            DA.SetDataList("Trunk", trunk);
            DA.SetDataList("SideBranch", sideB);
            DA.SetDataList("TopBranch", topB);


            DA.SetDataList("Debug", debug);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("930148B1-014A-43AA-845C-FB0C711D6AA0");
    }
}
