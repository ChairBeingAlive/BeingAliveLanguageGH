using System;
using System.IO;
using System.Reflection;
using Grasshopper.Kernel;

namespace BeingAliveLanguage
{
  public class InforExporter : GH_Component
  {
    InforExporter()
      : base("BAL_Check", "balCheck",
          "Check if everything is loaded correctly.",
          "BAL", "09::utils")
    { }

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override System.Drawing.Bitmap Icon => null;
    public override Guid ComponentGuid => new Guid("22179f27-ddad-40aa-bd16-f4c4d5d13c15");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddTextParameter("Info Text", "info", "Plugin info for loading native lib.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      try
      {
        // Check if the native library is loaded before proceeding
        if (!GSP.NativeBridge.IsNativeLibraryLoaded)
        {
          string[] errors = GSP.NativeBridge.GetErrorMessages();
          string errorMsg = string.Join("\n", errors);
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
              $"Native library not loaded. Check the following errors:\n{errorMsg}");
          return;
        }
        else
        {
          string loadedPath = GSP.NativeBridge.LoadedLibraryPath;
          string libName = Path.GetFileName(loadedPath);
          string infoText = $"Native library loaded successfully from: {loadedPath}\n" +
                            $"Library name: {libName}\n" +
                            $"Version: {Assembly.GetExecutingAssembly().GetName().Version}";

          DA.SetData(0, infoText);
        }

      }
      catch (Exception ex)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Exception: {ex.Message}");
      }
    }


  }
}
