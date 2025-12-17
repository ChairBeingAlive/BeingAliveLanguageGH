using System;
using System.IO;
using Grasshopper.Kernel;
using GSP.Core;
using BeingAliveLanguage.BalCore;

namespace BeingAliveLanguage {
public class BALinfoExporter : GH_Component {
  public BALinfoExporter()
      : base("BAL_Info",
             "balInfo",
             "Check BAL version and whether everything is loaded correctly.",
             "BAL",
             "00::info") {}

  public override GH_Exposure Exposure => GH_Exposure.primary;
  protected override System.Drawing.Bitmap Icon => SysUtils.cvtByteBitmap(Properties.Resources.pluginIcon);
  public override Guid ComponentGuid => new Guid("dc1c6827-4e84-48f5-be11-a18e9f410291");

  protected override void RegisterInputParams(GH_InputParamManager pManager) {}

  protected override void RegisterOutputParams(GH_OutputParamManager pManager) {
    pManager.AddTextParameter(
        "Info", "info", "Plugin info for loading native lib.", GH_ParamAccess.item);
  }

  protected override void SolveInstance(IGH_DataAccess DA) {
    try {
      // Check if the native library is loaded before proceeding
      if (!Platform.IsNativeLibraryLoaded) {
        string[] errors = Platform.GetErrorMessages();
        string errorMsg = string.Join("\n", errors);
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                          $"Native library not loaded. Check the following errors:\n{errorMsg}");
        return;
      } else {
        // Create an instance of BeingAliveLanguageInfo to access the non-static property
        string version = new BeingAliveLanguageInfo().AssemblyVersion;
        string loadedPath = Platform.LoadedLibraryPath;
        string libName = Path.GetFileName(loadedPath);

        string infoText = $"Native library loaded successfully from: {loadedPath}\n" +
                          $"Library name: {libName}\n" + $"Version: {version}";

        DA.SetData(0, infoText);
      }

    } catch (Exception ex) {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Exception: {ex.Message}");
    }
  }
}
}
