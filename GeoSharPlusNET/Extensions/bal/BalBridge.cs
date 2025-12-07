using System;
using System.Runtime.InteropServices;
using GSP.Core;

namespace GSP.Extensions.Bal {
  /// <summary>
  /// P/Invoke bridge for BAL (BeingAliveLanguage) specific native functions.
  /// Contains DllImport declarations for Windows and macOS platforms.
  /// </summary>
  public static class BalBridge {
    // =========
    // Example functions (for testing/reference)
    // =========

    #region Point Round Trip (Example)

    [DllImport(Platform.WindowsLib, EntryPoint = "point3d_roundtrip",
               CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Point3dRoundTripWin(
        byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize);

    [DllImport(Platform.MacLib, EntryPoint = "point3d_roundtrip",
               CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Point3dRoundTripMac(
        byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize);

    /// <summary>
    /// Round-trip a single Point3d through C++ (for testing).
    /// </summary>
    public static bool Point3dRoundTrip(
        byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize) {
      try {
        if (Platform.IsWindows)
          return Point3dRoundTripWin(inBuffer, inSize, out outBuffer, out outSize);
        else
          return Point3dRoundTripMac(inBuffer, inSize, out outBuffer, out outSize);
      } catch (Exception ex) {
        Platform.LogError($"Exception in Point3dRoundTrip: {ex.Message}");
        outBuffer = IntPtr.Zero;
        outSize = 0;
        return false;
      }
    }

    #endregion

    #region Point Array Round Trip (Example)

    [DllImport(Platform.WindowsLib, EntryPoint = "point3d_array_roundtrip",
               CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Point3dArrayRoundTripWin(
        byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize);

    [DllImport(Platform.MacLib, EntryPoint = "point3d_array_roundtrip",
               CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Point3dArrayRoundTripMac(
        byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize);

    /// <summary>
    /// Round-trip an array of Point3d through C++ (for testing).
    /// </summary>
    public static bool Point3dArrayRoundTrip(
        byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize) {
      try {
        if (Platform.IsWindows)
          return Point3dArrayRoundTripWin(inBuffer, inSize, out outBuffer, out outSize);
        else
          return Point3dArrayRoundTripMac(inBuffer, inSize, out outBuffer, out outSize);
      } catch (Exception ex) {
        Platform.LogError($"Exception in Point3dArrayRoundTrip: {ex.Message}");
        outBuffer = IntPtr.Zero;
        outSize = 0;
        return false;
      }
    }

    #endregion

    // =========
    // BAL-specific functions
    // =========

    #region Poisson Disk Elimination Sampling

    [DllImport(Platform.WindowsLib, EntryPoint = "BALpossionDiskElimSample",
               CallingConvention = CallingConvention.Cdecl)]
    private static extern bool BALpossionDiskElimSampleWin(
        byte[] inBuffer, int inSize, double generalArea, int dim, int n,
        out IntPtr outBuffer, out int outSize);

    [DllImport(Platform.MacLib, EntryPoint = "BALpossionDiskElimSample",
               CallingConvention = CallingConvention.Cdecl)]
    private static extern bool BALpossionDiskElimSampleMac(
        byte[] inBuffer, int inSize, double generalArea, int dim, int n,
        out IntPtr outBuffer, out int outSize);

    /// <summary>
    /// Perform Poisson disk elimination sampling on a point cloud.
    /// </summary>
    /// <param name="inBuffer">Serialized input points</param>
    /// <param name="inSize">Size of input buffer</param>
    /// <param name="generalArea">General area for sampling density</param>
    /// <param name="dim">Dimension (2 or 3)</param>
    /// <param name="n">Target number of output points</param>
    /// <param name="outBuffer">Output buffer (caller must free)</param>
    /// <param name="outSize">Size of output buffer</param>
    public static bool PoissonDiskElimSample(
        byte[] inBuffer, int inSize, double generalArea, int dim, int n,
        out IntPtr outBuffer, out int outSize) {
      try {
        if (Platform.IsWindows)
          return BALpossionDiskElimSampleWin(
              inBuffer, inSize, generalArea, dim, n, out outBuffer, out outSize);
        else
          return BALpossionDiskElimSampleMac(
              inBuffer, inSize, generalArea, dim, n, out outBuffer, out outSize);
      } catch (Exception ex) {
        Platform.LogError($"Exception in PoissonDiskElimSample: {ex.Message}");
        outBuffer = IntPtr.Zero;
        outSize = 0;
        return false;
      }
    }

    #endregion
  }
}
