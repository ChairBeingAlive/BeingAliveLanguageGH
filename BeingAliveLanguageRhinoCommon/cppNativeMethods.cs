using System;
using System.Runtime.InteropServices;

namespace BeingAliveLanguageRC
{

  internal static class Import
  {
    // absolute path
    //public const string cppLib = @"C:/Users/xarthur/source/repo/BeingAliveLanguageGH/bin/BeingAliveLanguageCpp.dll";

    // relative path
    public const string cppLib = @"BeingAliveLanguageCppPort.dll";
  }

  internal static class cppBAL
  {
    /// <summary>
    /// Possion Disc Sampling using the reduction approach, no need for radius.
    /// </summary>
    [DllImport(Import.cppLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void BAL_possionDiskElimSample(IntPtr inPt, double area, int n, IntPtr outPt);

    /// <summary>
    /// Possion Disc Sampling using the reduction approach, no need for radius.
    /// </summary>
    [DllImport(Import.cppLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void BAL_possionDiskElimSampleBound(IntPtr minMaxBound, int n, IntPtr gPt, IntPtr outPt);

    /// <summary>
    /// Testing Function to make sure the cpp part is integrated correctly.
    /// </summary>
    [DllImport(Import.cppLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern double BAL_Addition(double a, double b);
  }
}
