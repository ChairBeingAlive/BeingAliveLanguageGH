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
using System.Linq.Expressions;
using MathNet.Numerics;

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
            //pManager.AddCurveParameter("Debug", "debug", "Debug curves.", GH_ParamAccess.tree);

            pManager.AddGenericParameter("TreeInfo", "Tinfo", "Information about the tree.", GH_ParamAccess.list);
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

            //! 1. determine horizontal scaling factor of the trees
            var tscal = new List<Tuple<double, double>>();
            var distLst = new List<double>();
            var treeCol = new List<Tree>();
            if (plnLst.Count == 0)
                return;
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

                // ! sort root location 
                plnLst.Sort((pln0, pln1) =>
                {
                    Vector3d res = pln0.Origin - pln1.Origin;
                    if (Math.Abs(res[0]) > 1e-5)
                        return pln0.OriginX.CompareTo(pln1.OriginX);
                    else if (Math.Abs(res[1]) > 1e-5)
                        return pln0.OriginY.CompareTo(pln1.OriginY);
                    else // align on z axis or overlap, use the same criteria
                        return pln0.OriginZ.CompareTo(pln1.OriginZ);
                });

                // after list length check:
                for (int i = 0; i < plnLst.Count - 1; i++)
                {
                    var dis = Math.Abs(plnLst[i].Origin.DistanceTo(plnLst[i + 1].Origin));
                    distLst.Add(dis);
                }

                //! 2. draw the trees, collect tree width
                var widCol = new List<double>();
                foreach (var (pln, i) in plnLst.Select((pln, i) => (pln, i)))
                {
                    var t = new Tree(pln, hLst[i], modeUnitary == "unitary");
                    var res = t.Draw(phLst[i]);

                    if (!res.Item1)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, res.Item2);
                        return;
                    }

                    treeCol.Add(t);
                    widCol.Add(t.CalWidth());
                }

                //! 3. calculate scaling factor between trees
                var inbetweenScale = new List<double>();
                for (int i = 0; i < widCol.Count - 1; i++)
                {
                    inbetweenScale.Add(Math.Min(1, distLst[i] / ((widCol[i] + widCol[i + 1]) * 0.5)));
                }

                //! 4. generate scaling Tuple for each tree
                tscal.Add(Tuple.Create(1.0, inbetweenScale[0]));
                for (int i = 0; i < inbetweenScale.Count - 1; i++)
                {
                    tscal.Add(Tuple.Create(inbetweenScale[i], inbetweenScale[i + 1]));
                }
                tscal.Add(Tuple.Create(inbetweenScale.Last(), 1.0));
            }
            else if (plnLst.Count == 1) // special case: only one tree
            {
                tscal.Add(Tuple.Create(1.0, 1.0));
                treeCol.Add(new Tree(plnLst[0], hLst[0], modeUnitary == "unitary"));
                var res = treeCol.Last().Draw(phLst[0]);

                if (!res.Item1)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, res.Item2);
                    return;
                }
            }


            //! 5. scale each tree and output
            foreach (var (t, i) in treeCol.Select((t, i) => (t, i)))
            {
                t.Scale(tscal[i]);

                // output the curves 
                trunk.Add(t.mCurTrunk);
                canopy.Add(t.mCurCanopy);
                circ.AddRange(t.mCircCol);
                sideB.AddRange(t.mSideBranch);
                topB.AddRange(t.mSubBranch);
                babyB.AddRange(t.mNewBornBranch);

                //debug.AddRange(t.mDebug);
            }

            DA.SetDataList("Circumference", circ);
            DA.SetDataList("Canopy", canopy);
            DA.SetDataList("Trunk", trunk);
            DA.SetDataList("SideBranch", sideB);
            DA.SetDataList("TopBranch", topB);
            DA.SetDataList("BabyBranch", babyB);
            //DA.SetDataList("Debug", debug);
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

    public class BALtreeRoot : GH_Component
    {
        public BALtreeRoot()
        : base("TreeRoot", "balTreeRoot",
              "Generate the BAL tree-root drawing using the BAL tree and soil information.",
              "BAL", "03::plant")
        { }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.balTree; //todo: update img
        public override Guid ComponentGuid => new Guid("27C279E0-08C9-4110-AE40-81A59C9D9EB8");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("TreeInfo", "Tinfo", "Information about the tree.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Soil Base", "soilBase", "The base object used for soil diagram generation.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("All Roots", "rootAll", "The planar root drawing, collection of all level roots.", GH_ParamAccess.list);

            pManager.AddLineParameter("RootLevel-1", "rootLv1", "Primary roots.", GH_ParamAccess.list);
            pManager.AddLineParameter("RootLevel-2", "rootLv2", "Secondary roots.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            throw new NotImplementedException();
        }
    }
}
