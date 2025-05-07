using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GSP
{
  public static class NativeBridge
  {
    private const string WinLibName = @"GeoSharPlusCPP.dll";
    private const string MacLibName = @"libGeoSharPlusCPP.dylib";

    // Store error messages that can be retrieved by Grasshopper components
    private static readonly List<string> _errorLog = new List<string>();
    private static bool _isNativeLibraryLoaded = false;
    private static string _loadedLibraryPath = string.Empty;

    /// <summary>
    /// Returns true if the native library was successfully loaded
    /// </summary>
    public static bool IsNativeLibraryLoaded => _isNativeLibraryLoaded;

    /// <summary>
    /// Path where the library was loaded from (empty if not loaded)
    /// </summary>
    public static string LoadedLibraryPath => _loadedLibraryPath;

    /// <summary>
    /// Get error messages that can be displayed in Grasshopper components
    /// </summary>
    public static string[] GetErrorMessages()
    {
      lock (_errorLog)
      {
        return _errorLog.ToArray();
      }
    }

    /// <summary>
    /// Clear the error log
    /// </summary>
    public static void ClearErrorLog()
    {
      lock (_errorLog)
      {
        _errorLog.Clear();
      }
    }

    /// <summary>
    /// Add an error message to the log
    /// </summary>
    private static void LogError(string message)
    {
      lock (_errorLog)
      {
        _errorLog.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");

        // Keep log at a reasonable size
        if (_errorLog.Count > 100)
          _errorLog.RemoveAt(0);
      }
    }

    // Set DllImport search path to include the current assembly directory for macOS
    static NativeBridge()
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
      {
        try
        {
          // List all possible library locations to try
          var searchLocations = new List<string>();

          // 1. Load from assembly directory
          string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
          string? assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
          if (assemblyDirectory != null)
          {
            searchLocations.Add(Path.Combine(assemblyDirectory, MacLibName));
          }

          // 2. Try parent directory (sometimes needed for GH plugins)
          if (assemblyDirectory != null)
          {
            string? parentDir = Path.GetDirectoryName(assemblyDirectory);
            if (parentDir != null)
            {
              searchLocations.Add(Path.Combine(parentDir, MacLibName));
            }
          }

          // 3. Try current directory
          searchLocations.Add(Path.Combine(Directory.GetCurrentDirectory(), MacLibName));

          // 4. Add standard system locations
          searchLocations.Add(MacLibName); // Default system search paths

          LogError($"Searching for {MacLibName} in the following locations:");
          foreach (var path in searchLocations)
          {
            LogError($"- {path} (exists: {File.Exists(path)})");
          }

          // Try to load from each location
          IntPtr handle = IntPtr.Zero;
          foreach (var libraryPath in searchLocations)
          {
            if (File.Exists(libraryPath))
            {
              LogError($"Attempting to load native library from: {libraryPath}");
              handle = dlopen(libraryPath, 2); // RTLD_NOW = 2

              if (handle != IntPtr.Zero)
              {
                _isNativeLibraryLoaded = true;
                _loadedLibraryPath = libraryPath;
                LogError($"Successfully loaded native library from: {libraryPath}");
                break;
              }
              else
              {
                string errorMsg = dlerror();
                LogError($"Failed to load library from {libraryPath}: {errorMsg}");
              }
            }
          }

          if (handle == IntPtr.Zero)
          {
            LogError($"Failed to load native library from any location. Calls to native methods will likely fail.");
          }
        }
        catch (Exception ex)
        {
          LogError($"Exception while setting up native library path: {ex.Message}");
          LogError($"Stack trace: {ex.StackTrace}");
        }
      }
      else
      {
        // For Windows, we don't need special loading as DllImport handles it
        _isNativeLibraryLoaded = true;
      }
    }

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
      try
      {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
          return Point3dRoundTripWin(inBuffer, inSize, out outBuffer, out outSize);
        else
          return Point3dRoundTripMac(inBuffer, inSize, out outBuffer, out outSize);
      }
      catch (Exception ex)
      {
        LogError($"Exception in Point3dRoundTrip: {ex.Message}");
        outBuffer = IntPtr.Zero;
        outSize = 0;
        return false;
      }
    }

    // Example: Point Array Round Trip -- Passing an array of Point3d to C++ and back
    [DllImport(WinLibName, EntryPoint = "point3d_array_roundtrip", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Point3dArrayRoundTripWin(byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize);
    [DllImport(MacLibName, EntryPoint = "point3d_array_roundtrip", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Point3dArrayRoundTripMac(byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize);
    public static bool Point3dArrayRoundTrip(byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize)
    {
      try
      {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
          return Point3dArrayRoundTripWin(inBuffer, inSize, out outBuffer, out outSize);
        else
          return Point3dArrayRoundTripMac(inBuffer, inSize, out outBuffer, out outSize);
      }
      catch (Exception ex)
      {
        LogError($"Exception in Point3dArrayRoundTrip: {ex.Message}");
        outBuffer = IntPtr.Zero;
        outSize = 0;
        return false;
      }
    }

    // BAL Poisson Disk Elimination Sample
    [DllImport(WinLibName, EntryPoint = "BALpossionDiskElimSample", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool BALpossionDiskElimSampleWin(byte[] inBuffer, int inSize, double generalArea, int dim, int n, out IntPtr outBuffer, out int outSize);
    [DllImport(MacLibName, EntryPoint = "BALpossionDiskElimSample", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool BALpossionDiskElimSampleMac(byte[] inBuffer, int inSize, double generalArea, int dim, int n, out IntPtr outBuffer, out int outSize);
    public static bool BALpossionDiskElimSample(byte[] inBuffer, int inSize, double generalArea, int dim, int n, out IntPtr outBuffer, out int outSize)
    {
      try
      {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
          return BALpossionDiskElimSampleWin(inBuffer, inSize, generalArea, dim, n, out outBuffer, out outSize);
        else
          return BALpossionDiskElimSampleMac(inBuffer, inSize, generalArea, dim, n, out outBuffer, out outSize);
      }
      catch (Exception ex)
      {
        LogError($"Exception in BALpossionDiskElimSample: {ex.Message}");
        outBuffer = IntPtr.Zero;
        outSize = 0;
        return false;
      }
    }
  }
}
