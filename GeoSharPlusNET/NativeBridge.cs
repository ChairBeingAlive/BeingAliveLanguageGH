//using System;
using System.Runtime.InteropServices;

namespace GSP
{

  public static class NativeBridge
  {
    private const string WinLibName = @"GeoSharPlusCPP.dll";
    private const string MacLibName = @"libGeoSharPlusCPP.dylib";

    // For macOS
    [DllImport("libdl.dylib")]
    private static extern IntPtr dlopen(string path, int mode);

    static NativeBridge()
    {
      // Pre-load the library on macOS
      if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
      {
        string libraryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MacLibName);
        IntPtr handle = dlopen(libraryPath, 2); // RTLD_NOW = 2

        if (handle == IntPtr.Zero)
        {
          throw new DllNotFoundException($"Failed to load {libraryPath}");
        }
      }
    }

    // For each function, we create 3 functions: Windows, macOS implementations, and the public API

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
