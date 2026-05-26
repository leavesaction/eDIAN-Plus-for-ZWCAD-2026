using eDIAN.Data;
using System;
using System.IO;
using System.Runtime.InteropServices;
using log4net;

namespace eDIAN.Hook
{
    public static class VfsInterceptor
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(VfsInterceptor));
        private static bool _isInstalled = false;
        private static readonly NativeMethods.FnVfsOpenCallback _openCb = (path, access, share) => IntPtr.Zero;
        private static readonly NativeMethods.FnVfsCloseCallback _closeCb = (handle) => { };

        public static void Install()
        {
            if (_isInstalled) return;

            try
            {
                logger.Info("[VFS 3.0] Deploying Pure Memory Shield Engine...");

                // Initialize the native VFS engine with settings from CommonConstants
                NativeMethods.InitializeVfs(
                    CommonConstants.PLUGIN_MIP_TEMP_PATH, 
                    CommonConstants.PLUGIN_LOG_PATH, 
                    CommonConstants.PLUGIN_LOG_LEVEL, 
                    HookConstants.GetNativeConfigString(), 
                    _openCb, 
                    _closeCb);

                if (NativeMethods.InstallHooks())
                {
                    _isInstalled = true;
                    logger.Info("[VFS 3.0] Pure Memory Shield Engine Active.");
                }
            }
            catch (Exception ex)
            {
                logger.Fatal($"[VFS 3.0] Deployment error: {ex.Message}", ex);
            }
        }

        public static void Uninstall()
        {
            if (!_isInstalled) return;
            NativeMethods.UninstallHooks();
            _isInstalled = false;
        }

    }
}
