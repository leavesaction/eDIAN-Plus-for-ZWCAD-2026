using eDIAN.Core;
using log4net;
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace eDIAN.Main.Core
{
    public class WindowFormHandle : HwndHost
    {
        private static readonly ILog logger = PluginLogger.getLogger("WindowFormHandle", "application.log");

        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);

        public IntPtr handle { get; set; }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            this.handle = CreateWindowEx(
                0, "STATIC", "Hello from Win32",
                WS_CHILD | WS_VISIBLE,
                0, 0, 0, 0,
                hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            logger.Debug($" - BuildWindowCore : handle={handle}");

            return new HandleRef(this, handle);
        }

        protected override void DestroyWindowCore(HandleRef handleRef)
        {
            DestroyWindow(handleRef.Handle);
        }
    }
}
