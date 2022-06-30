using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace BeingAliveLanguage
{
    public class BeingAliveLanguageInfo : GH_AssemblyInfo
    {
        public override string Name => "BeingAliveLanguage";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => Properties.Resources.pluginIcon;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "";

        public override Guid Id => new Guid("43E47992-4A44-4951-9F57-30300CFE12A2");

        //Return a string identifying you or your company.
        public override string AuthorName => "Dr. Zhao Ma";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "ma@arch.ethz.ch";

        public override string AssemblyVersion => "0.0.1";
        public override string Version => "0.0.2";
    }
}