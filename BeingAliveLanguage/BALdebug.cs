using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeingAliveLanguage
{
    public class BALdebug : GH_Component
    {
        public BALdebug()
            : base("balDebug", "balDebug", "debugging component, default hidden.", "BAL", "99::debug")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("d24bc4b1-646b-4642-b684-d053f489e5e1");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            //pManager.AddPointParameter("inputPts", "iP", "input points for the elimination approach.", GH_ParamAccess.list);
            //pManager.AddNumberParameter("area", "A", "area of the sampling domain", GH_ParamAccess.item);

            pManager.AddRectangleParameter("bound", "B", "sampling boundary", GH_ParamAccess.item);
            pManager.AddIntegerParameter("num", "N", "output number of pts.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("generatedPts", "gP", "sampled points using the elimination approach.", GH_ParamAccess.list);

            pManager.AddPointParameter("sampledPts", "sP", "sampled points using the elimination approach.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            //var ip = new List<Point3d>();
            //double area = 1.0;
            //DA.GetDataList("inputPts", ip);
            //DA.GetData("area", ref area);

            Rectangle3d rec = new Rectangle3d();
            DA.GetData("bound", ref rec);
            int num = 0;
            DA.GetData("num", ref num);

            BeingAliveLanguageRC.Utils.SampleElim(rec, num, out List<Point3d> gp, out List<Point3d> op);

            DA.SetDataList("generatedPts", gp);
            DA.SetDataList("sampledPts", op);
        }
    }
}
