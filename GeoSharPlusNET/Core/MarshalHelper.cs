using System;
using System.Runtime.InteropServices;

namespace GSP.Core {
  /// <summary>
  /// Helper utilities for marshaling data between managed and unmanaged memory.
  /// </summary>
  public static class MarshalHelper {
    /// <summary>
    /// Copy data from unmanaged memory to a managed byte array and free the unmanaged memory.
    /// </summary>
    /// <param name="ptr">Pointer to unmanaged memory</param>
    /// <param name="size">Size of data in bytes</param>
    /// <returns>Managed byte array containing the copied data</returns>
    public static byte[] CopyAndFree(IntPtr ptr, int size) {
      if (ptr == IntPtr.Zero || size <= 0)
        return Array.Empty<byte>();

      var buffer = new byte[size];
      Marshal.Copy(ptr, buffer, 0, size);
      Marshal.FreeCoTaskMem(ptr);
      return buffer;
    }

    /// <summary>
    /// Copy data from unmanaged memory to a managed byte array without freeing.
    /// </summary>
    /// <param name="ptr">Pointer to unmanaged memory</param>
    /// <param name="size">Size of data in bytes</param>
    /// <returns>Managed byte array containing the copied data</returns>
    public static byte[] Copy(IntPtr ptr, int size) {
      if (ptr == IntPtr.Zero || size <= 0)
        return Array.Empty<byte>();

      var buffer = new byte[size];
      Marshal.Copy(ptr, buffer, 0, size);
      return buffer;
    }

    /// <summary>
    /// Free unmanaged memory allocated with CoTaskMemAlloc.
    /// </summary>
    /// <param name="ptr">Pointer to unmanaged memory</param>
    public static void Free(IntPtr ptr) {
      if (ptr != IntPtr.Zero) {
        Marshal.FreeCoTaskMem(ptr);
      }
    }

    /// <summary>
    /// Allocate unmanaged memory and copy managed byte array to it.
    /// </summary>
    /// <param name="data">Managed byte array</param>
    /// <returns>Pointer to unmanaged memory (caller must free)</returns>
    public static IntPtr AllocateAndCopy(byte[] data) {
      if (data == null || data.Length == 0)
        return IntPtr.Zero;

      IntPtr ptr = Marshal.AllocCoTaskMem(data.Length);
      Marshal.Copy(data, 0, ptr, data.Length);
      return ptr;
    }
  }
}
