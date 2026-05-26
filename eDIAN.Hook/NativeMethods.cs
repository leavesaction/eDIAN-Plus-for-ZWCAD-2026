using System;
using System.Runtime.InteropServices;

namespace eDIAN.Hook
{
    /**
     * @brief P/Invoke Interface for eDIAN.Hook.Native.dll
     */
    internal static class NativeMethods
    {
        private const string DllName = "eDIAN.Hook.Native.dll";

        // --- Callback Delegates ---
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        public delegate IntPtr FnVfsOpenCallback(string lpFileName, uint dwAccess, uint dwShare);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate void FnVfsCloseCallback(IntPtr hFile);

        // --- Exported Functions ---

        [DllImport("eDIAN.Hook.Native.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public static extern void InitializeVfs(string lpTempPath, string lpLogPath, int nLogLevel, string lpConfigString, FnVfsOpenCallback openCb, FnVfsCloseCallback closeCb);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern bool InstallHooks();

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern void UninstallHooks();



        // --- Win32 APIs for Surrogate Management ---

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);
    }
}
