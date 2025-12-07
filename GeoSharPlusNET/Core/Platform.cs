using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GSP.Core {
  /// <summary>
  /// Platform detection and native library path management.
  /// </summary>
  public static class Platform {
    /// <summary>
    /// Windows native library name
    /// </summary>
    public const string WindowsLib = @"GeoSharPlusCPP.dll";

    /// <summary>
    /// macOS native library name
    /// </summary>
    public const string MacLib = @"libGeoSharPlusCPP.dylib";

    /// <summary>
    /// Returns true if running on Windows
    /// </summary>
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Returns true if running on macOS
    /// </summary>
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Returns true if running on Linux
    /// </summary>
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <summary>
    /// Get the current native library name based on platform
    /// </summary>
    public static string NativeLibraryName => IsWindows ? WindowsLib : MacLib;

    private static bool _isNativeLibraryLoaded = false;
    private static string _loadedLibraryPath = string.Empty;
    private static readonly List<string> _errorLog = new List<string>();

    /// <summary>
    /// Returns true if the native library was successfully loaded
    /// </summary>
    public static bool IsNativeLibraryLoaded => _isNativeLibraryLoaded;

    /// <summary>
    /// Path where the library was loaded from (empty if not loaded)
    /// </summary>
    public static string LoadedLibraryPath => _loadedLibraryPath;

    /// <summary>
    /// Get error messages for debugging
    /// </summary>
    public static string[] GetErrorMessages() {
      lock (_errorLog) {
        return _errorLog.ToArray();
      }
    }

    /// <summary>
    /// Clear the error log
    /// </summary>
    public static void ClearErrorLog() {
      lock (_errorLog) {
        _errorLog.Clear();
      }
    }

    /// <summary>
    /// Add an error message to the log
    /// </summary>
    internal static void LogError(string message) {
      lock (_errorLog) {
        _errorLog.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");

        // Keep log at a reasonable size
        if (_errorLog.Count > 100)
          _errorLog.RemoveAt(0);
      }
    }

    /// <summary>
    /// Initialize native library loading
    /// </summary>
    static Platform() {
      InitializeNativeLibrary();
    }

    private static void InitializeNativeLibrary() {
      if (IsMacOS) {
        InitializeMacOS();
      } else if (IsWindows) {
        InitializeWindows();
      }
    }

    private static void InitializeMacOS() {
      try {
        var searchLocations = new List<string>();

        // 1. Load from assembly directory
        string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        string? assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
        if (assemblyDirectory != null) {
          searchLocations.Add(Path.Combine(assemblyDirectory, MacLib));
        }

        // 2. Try parent directory (sometimes needed for GH plugins)
        if (assemblyDirectory != null) {
          string? parentDir = Path.GetDirectoryName(assemblyDirectory);
          if (parentDir != null) {
            searchLocations.Add(Path.Combine(parentDir, MacLib));
          }
        }

        // 3. Try current directory
        searchLocations.Add(Path.Combine(Directory.GetCurrentDirectory(), MacLib));

        // 4. Add standard system locations
        searchLocations.Add(MacLib);

        LogError($"Searching for {MacLib} in the following locations:");
        foreach (var path in searchLocations) {
          LogError($"- {path} (exists: {File.Exists(path)})");
        }

        // Try to load from each location
        IntPtr handle = IntPtr.Zero;
        foreach (var libraryPath in searchLocations) {
          if (File.Exists(libraryPath)) {
            LogError($"Attempting to load native library from: {libraryPath}");
            handle = dlopen(libraryPath, 2);  // RTLD_NOW = 2

            if (handle != IntPtr.Zero) {
              _isNativeLibraryLoaded = true;
              _loadedLibraryPath = libraryPath;
              LogError($"Successfully loaded native library from: {libraryPath}");
              break;
            } else {
              string errorMsg = dlerror();
              LogError($"Failed to load library from {libraryPath}: {errorMsg}");
            }
          }
        }

        if (handle == IntPtr.Zero) {
          LogError($"Failed to load native library from any location.");
        }
      } catch (Exception ex) {
        LogError($"Exception while setting up native library path: {ex.Message}");
        LogError($"Stack trace: {ex.StackTrace}");
      }
    }

    private static void InitializeWindows() {
      try {
        string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        string? assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
        string dllPath = string.Empty;

        if (assemblyDirectory != null) {
          dllPath = Path.Combine(assemblyDirectory, WindowsLib);
          if (!File.Exists(dllPath)) {
            string? parentDir = Path.GetDirectoryName(assemblyDirectory);
            if (parentDir != null) {
              dllPath = Path.Combine(parentDir, WindowsLib);
            }
          }
        }

        if (File.Exists(dllPath)) {
          _isNativeLibraryLoaded = true;
          _loadedLibraryPath = dllPath;
          LogError($"Successfully located native library at: {dllPath}");
        } else {
          LogError($"Failed to locate native library {WindowsLib} in expected locations.");
        }
      } catch (Exception ex) {
        LogError($"Exception while locating native library path: {ex.Message}");
        LogError($"Stack trace: {ex.StackTrace}");
      }
    }

    // P/Invoke for macOS dynamic library loading
    [DllImport("libdl.dylib")]
    private static extern IntPtr dlopen(string path, int flags);

    [DllImport("libdl.dylib")]
    private static extern string dlerror();
  }
}
