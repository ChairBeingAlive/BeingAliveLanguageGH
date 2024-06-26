﻿using System;
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
    /// Testing Function to make sure the cpp part is integrated correctly.
    /// </summary>
    [DllImport(Import.cppLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern double BAL_Addition(double a, double b);

    /// <summary>
    /// Possion Disc Sampling using the reduction approach, no need for radius.
    /// area-based 2D/3D version -- generalArea: area for 2d, volume for 3d
    /// </summary>
    [DllImport(Import.cppLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void BAL_possionDiskElimSample(IntPtr inPt, double generalArea, int dim, int n, IntPtr outPt);

    /// <summary>
    /// Possion Disc Sampling using the reduction approach, no need for radius.
    /// range-based 2D version  
    /// </summary>
    [DllImport(Import.cppLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void BAL_possionDiskElimSampleBound(IntPtr minMaxBound, int n, IntPtr gPt, IntPtr outPt);

    /// <summary>
    /// Compute the convex hull of a set of points.
    /// </summary>
    //[DllImport(Import.cppLib, CallingConvention = CallingConvention.Cdecl)]
    //internal static extern double BAL_computeHull(IntPtr inPt, IntPtr outV, IntPtr outF);
  }
}
