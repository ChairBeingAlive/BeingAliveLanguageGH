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
    public override Bitmap AssemblyIcon => Properties.Resources.pluginIcon;

    //Return a short string describing the purpose of this GHA library.
    public override string Description =>
        "This is the plugin for automatically using the set of language " +
        "developed by the Chair of Being Alive at ETH Zurich.";

    public override Guid Id => new Guid("43E47992-4A44-4951-9F57-30300CFE12A2");

    public override string AuthorName => "Dr. Zhao Ma @ BeingAlive";
    public override string AuthorContact => "https://beingalivelanguage.arch.ethz.ch";
    public override GH_LibraryLicense License => GH_LibraryLicense.opensource;

    public override string Version => "0.6.9";
  }

  // update plugin icons in the tab
  public class IGM_CategoryIcon : GH_AssemblyPriority
  {
    public override GH_LoadingInstruction PriorityLoad()
    {
      Grasshopper.Instances.ComponentServer.AddCategoryIcon("BAL", Properties.Resources.pluginIcon);
      Grasshopper.Instances.ComponentServer.AddCategorySymbolName("BAL", 'B');
      return GH_LoadingInstruction.Proceed;
    }
  }
}