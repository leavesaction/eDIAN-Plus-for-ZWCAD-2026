using eDIAN.Data;
using System;
using System.Runtime.InteropServices;

namespace eDIAN.Main.Core
{
    public class DisplayProtector
    {
        // Window 화면 캡처 방지
        [DllImport("user32.dll")]
        public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        // MIP 화면 캡처 방지
        [DllImport("msipc\\msipc.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int IpcProtectWindow(IntPtr hWnd);

        [DllImport("msipc\\msipc.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int IpcUnprotectWindow(IntPtr hWnd);

        // 캡처 방지 플래그 정의
        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_MONITOR = 0x00000001; // 모니터 화면에만 표시 (캡처 시 제외)

        /// <summary>
        /// 화면 캡처 방지/해제 (Window Display Affinity 방식)
        /// </summary>
        /// <param name="windowHandle"></param>
        public static String protectDisplayByWindow(IntPtr windowHandle, bool isProtect)
        {
            bool result = false;

            if (!CommonConstants.IS_SCREEN_PROTECT)
            {
                return result.ToString();
            }

            if (windowHandle == IntPtr.Zero)
            {
                return result.ToString();
            }

            try
            {
                if (isProtect)
                {
                    // 화면 캡처 방지
                    result = SetWindowDisplayAffinity(windowHandle, WDA_MONITOR);
                }
                else
                {
                    // 화면 캡처 방지 해제
                    result = SetWindowDisplayAffinity(windowHandle, WDA_NONE);
                }
            }
            catch (Exception ex)
            {
                return $"\n - protectDisplayByWindow Exception : \n{ex}";
            }

            return result.ToString();
        }

        /// <summary>
        /// 화면 캡처 방지/해제 (Window Display Affinity 방식)
        /// </summary>
        /// <param name="windowHandle"></param>
        public static String protectScreen(IntPtr windowHandle, bool isProtect)
        {
            int result = -9;

            if (!CommonConstants.IS_SCREEN_PROTECT)
            {
                return $"0x{-8:X8}";
            }

            if (windowHandle == IntPtr.Zero)
            {
                return $"0x{-7:X8}";
            }

            try
            {
                if (isProtect)
                {
                    // 화면 캡처 방지
                    result = IpcProtectWindow(windowHandle); ;
                }
                else
                {
                    // 화면 캡처 방지 해제
                    result = IpcUnprotectWindow(windowHandle);
                }
            }
            catch (Exception ex)
            {

                return $"\n - protectDisplayByMIP Exception : \n{ex}";
            }

            return $"0x{result:X8}";
        }
    }
}