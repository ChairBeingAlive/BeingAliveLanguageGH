using Grasshopper.Kernel;
using System;
using BeingAliveLanguage.BalCore;

namespace BeingAliveLanguage {
  public class BeingAliveLanguageInfo : GH_AssemblyInfo {
    public override string Name => "BeingAliveLanguage";

    // Return a short string describing the purpose of this GHA library.
    public override string Description =>
        "This is the plugin for automatically using the set of language " +
        "developed by the Chair of Being Alive at ETH Zurich.\n" + "\nKeywords:" + "\n- drawing" +
        "\n- climate" + "\n- soil" + "\n- language";

    public override Guid Id => new Guid("43E47992-4A44-4951-9F57-30300CFE12A2");

    public override string AuthorName => "Dr. Zhao Ma @ BeingAlive";
    public override string AuthorContact => "https://beingalivelanguage.arch.ethz.ch";
    public override GH_LibraryLicense License => GH_LibraryLicense.opensource;

    // public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    public override string AssemblyVersion => "0.9.4";

    // this is currently the variable used by McNeel for plugin system
    public override string Version => AssemblyVersion;
  }

  // update plugin icons in the tab
  public class IGM_CategoryIcon : GH_AssemblyPriority {
    public override GH_LoadingInstruction PriorityLoad() {
      Grasshopper.Instances.ComponentServer.AddCategorySymbolName("BAL", 'B');
      Grasshopper.Instances.ComponentServer.AddCategoryIcon(
          "BAL", SysUtils.cvtByteBitmap(Properties.Resources.pluginIcon));
      return GH_LoadingInstruction.Proceed;
    }
  }
}
