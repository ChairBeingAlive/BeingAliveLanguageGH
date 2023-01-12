using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;
using Rhino.ApplicationSettings;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Rhino.UI.Controls;
using System.ComponentModel.Composition;

namespace BeingAliveLanguage
{
    public class BALtree : GH_Component
    {
        public BALtree()
        : base("Tree", "balTree",
              "Generate the BAL tree model.",
              "BAL", "03::plant")
        { }

        string modeUnitary = "non-unitary";
        protected override System.Drawing.Bitmap Icon => Properties.Resources.balTree;
        public override Guid ComponentGuid => new Guid("930148B1-014A-43AA-845C-FB0C711D6AA0");

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
            pManager.AddCurveParameter("BabyBranch", "BB", "Tree baby branch at dying phase curves.", GH_ParamAccess.tree);
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
            var babyB = new List<Curve>();

            var debug = new List<Curve>();

            //! 1. determine horizontal scaling factor of the trees
            var ratio = new List<Tuple<double, double>>();
            var tmpDLst = new List<double>();
            if (plnLst.Count == 0)
                return;
            else if (plnLst.Count == 1)
            {
                ratio.Add(Tuple.Create(1.0, 1.0));
            }
            else if (plnLst.Count > 1)
            {
                if (hLst.Count == 1)
                    hLst = Enumerable.Repeat(hLst[0], plnLst.Count).ToList();
                else if (hLst.Count != plnLst.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Height # does not match Plane #, please check.");
                }
                if (phLst.Count == 1)
                    phLst = Enumerable.Repeat(phLst[0], plnLst.Count).ToList();
                else if (phLst.Count != plnLst.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Phase # does not match Plane #, please check.");
                }

                // after list length check:
                for (int i = 0; i < plnLst.Count - 1; i++)
                {
                    var dis = Math.Abs(plnLst[i].Origin.DistanceTo(plnLst[i + 1].Origin));
                    tmpDLst.Add(dis);
                }

                ratio.Add(Tuple.Create(1.0, Math.Min(1, tmpDLst[0] / hLst[0])));
                for (int i = 0; i < tmpDLst.Count - 1; i++)
                {
                    ratio.Add(Tuple.Create(Math.Min(1, tmpDLst[i] / hLst[i + 1]), Math.Min(1, tmpDLst[i + 1] / hLst[i + 1])));
                }
                ratio.Add(Tuple.Create(Math.Min(1, tmpDLst.Last() / hLst.Last()), 1.0));
            }

            //! 2. draw the trees.
            foreach (var (pln, i) in plnLst.Select((pln, i) => (pln, i)))
            {
                var t = new Tree(pln, hLst[i], modeUnitary == "unitary", ratio[i]);
                var res = t.Draw(phLst[i]);

                if (!res)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Phase out of range.");
                    return;
                }

                // output the curves 
                trunk.Add(t.mCurTrunk);
                canopy.Add(t.mCurCanopy);
                circ.AddRange(t.mCircCol);
                sideB.AddRange(t.mSideBranch);
                topB.AddRange(t.mSubBranch);
                babyB.AddRange(t.mNewBornBranch);

                debug.AddRange(t.mDebug);
            }

            DA.SetDataList("Circumference", circ);
            DA.SetDataList("Canopy", canopy);
            DA.SetDataList("Trunk", trunk);
            DA.SetDataList("SideBranch", sideB);
            DA.SetDataList("TopBranch", topB);
            DA.SetDataList("BabyBranch", babyB);
            DA.SetDataList("Debug", debug);
        }


        private bool CheckMode(string _modeCheck) => modeUnitary == _modeCheck;
        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "Tree Type:", (sender, e) => { }, false).Font = GH_FontServer.StandardItalic;
            Menu_AppendItem(menu, " Unitary", (sender, e) => Menu.SelectMode(this, sender, e, ref modeUnitary, "unitary"), true, CheckMode("unitary"));
            Menu_AppendItem(menu, " Non-Unitary", (sender, e) => Menu.SelectMode(this, sender, e, ref modeUnitary, "non-unitary"), true, CheckMode("non-unitary"));
        }

        public override bool Write(GH_IWriter writer)
        {
            if (modeUnitary != "")
                writer.SetString("modeUnitary", modeUnitary);
            return base.Write(writer);
        }
        public override bool Read(GH_IReader reader)
        {
            if (reader.ItemExists("modeUnitary"))
                modeUnitary = reader.GetString("modeUnitary");

            Message = reader.GetString("modeUnitary").ToUpper();

            return base.Read(reader);
        }

    }
}
