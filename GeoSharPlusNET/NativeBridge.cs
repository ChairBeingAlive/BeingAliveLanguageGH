//using System;
using System.Runtime.InteropServices;

namespace GSP
{
  public static class NativeBridge
  {
    private const string WinLibName = @"GeoSharPlusCPP.dll";
    private const string MacLibName = @"libGeoSharPlusCPP.dylib";

    // Set DllImport search path to include the current assembly directory for macOS
    static NativeBridge()
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
      {
        try
        {
          // Load the native library from the plugin directory
          string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
          string? assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
          if (assemblyDirectory != null)
          {
            string libraryPath = Path.Combine(assemblyDirectory, MacLibName);

            // Try to explicitly load the library from this path
            if (File.Exists(libraryPath))
            {
              // On macOS, we use dlopen to load the library
              IntPtr handle = dlopen(libraryPath, 2); // RTLD_NOW = 2
              if (handle == IntPtr.Zero)
              {
                string errorMsg = dlerror();
                Console.WriteLine($"Failed to load library: {errorMsg}");
              }
            }
          }
          else
          {
            Console.WriteLine("Error: Unable to determine the assembly directory.");
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine($"Error setting up native library path: {ex.Message}");
        }
      }
    }}

    // P/Invoke declarations for macOS dynamic library loading
    [DllImport("libdl.dylib")]
    private static extern IntPtr dlopen(string path, int flags);

    [DllImport("libdl.dylib")]
    private static extern string dlerror();

    // =========
    // For each function, we create 3 functions: Windows, macOS implementations, and the public API
    // =========

    // Example: Point Round Trip -- Passing a Point3d to C++ and back 
    [DllImport(WinLibName, EntryPoint = "point3d_roundtrip", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Point3dRoundTripWin(byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize);
    [DllImport(MacLibName, EntryPoint = "point3d_roundtrip", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Point3dRoundTripMac(byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize);
    public static bool Point3dRoundTrip(byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize)
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return Point3dRoundTripWin(inBuffer, inSize, out outBuffer, out outSize);
      else
        return Point3dRoundTripMac(inBuffer, inSize, out outBuffer, out outSize);
    }

    // Example: Point Array Round Trip -- Passing an array of Point3d to C++ and back
    [DllImport(WinLibName, EntryPoint = "point3d_array_roundtrip", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Point3dArrayRoundTripWin(byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize);
    [DllImport(MacLibName, EntryPoint = "point3d_array_roundtrip", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Point3dArrayRoundTripMac(byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize);
    public static bool Point3dArrayRoundTrip(byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize)
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return Point3dArrayRoundTripWin(inBuffer, inSize, out outBuffer, out outSize);
      else
        return Point3dArrayRoundTripMac(inBuffer, inSize, out outBuffer, out outSize);
    }

    // BAL Poisson Disk Elimination Sample
    [DllImport(WinLibName, EntryPoint = "BALpossionDiskElimSample", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool BALpossionDiskElimSampleWin(byte[] inBuffer, int inSize, double generalArea, int dim, int n, out IntPtr outBuffer, out int outSize);
    [DllImport(MacLibName, EntryPoint = "BALpossionDiskElimSample", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool BALpossionDiskElimSampleMac(byte[] inBuffer, int inSize, double generalArea, int dim, int n, out IntPtr outBuffer, out int outSize);
    public static bool BALpossionDiskElimSample(byte[] inBuffer, int inSize, double generalArea, int dim, int n, out IntPtr outBuffer, out int outSize)
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return BALpossionDiskElimSampleWin(inBuffer, inSize, generalArea, dim, n, out outBuffer, out outSize);
      else
        return BALpossionDiskElimSampleMac(inBuffer, inSize, generalArea, dim, n, out outBuffer, out outSize);
    }

  }
}
