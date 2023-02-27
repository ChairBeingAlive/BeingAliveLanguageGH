using System;
using System.Runtime.InteropServices;

namespace BeingAliveLanguageRC
{

    internal static class Import
    {
        // absolute path
        //public const string cppLib = @"C:/repo/igm-GH/x64/Release/igmCppPort.dll";

        // relative path
        public const string cppLib = @"BeingAliveLanguageCpp.dll";
    }

    internal static class cppBAL
    {
        /// <summary>
        /// Possion Disc Sampling using the reduction approach, no need for radius 
        /// </summary>
        [DllImport(Import.cppLib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void BAL_possionDiskElimSample(IntPtr inPt, int n, IntPtr outPt);
    }
}
