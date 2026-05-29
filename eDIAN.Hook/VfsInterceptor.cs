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

        /// <summary>QSAVE/SAVE 구간 — Native 저장 창(8s) 시작 (zws sidecar 없을 때).</summary>
        public static void ArmSaveWindow()
        {
            if (!_isInstalled) return;
            try
            {
                NativeMethods.ArmZwcadSaveWindow();
            }
            catch (Exception ex)
            {
                logger.Warn($"[VFS] ArmSaveWindow failed: {ex.Message}");
            }
        }

        /// <summary>CAD Open 완료 후 — canonical temp _uuid.dwg 디스크 기화 (L1).</summary>
        public static void FinalizeOpenVaporize(string mipTempDwgPath)
        {
            if (!_isInstalled || string.IsNullOrWhiteSpace(mipTempDwgPath))
                return;
            try
            {
                NativeMethods.FinalizeMipTempDwgAfterCadOpen(mipTempDwgPath);
            }
            catch (Exception ex)
            {
                logger.Warn($"[VFS] FinalizeOpenVaporize failed: {ex.Message}");
            }
        }

        /// <summary>닫기 ApplyProtection 직전 — temp _uuid.dwg 실물 확보.</summary>
        public static bool PrepareCloseCommit(string mipTempDwgPath)
        {
            if (!_isInstalled || string.IsNullOrWhiteSpace(mipTempDwgPath))
                return false;
            try
            {
                return NativeMethods.PrepareMipTempDwgForCloseCommit(mipTempDwgPath);
            }
            catch (Exception ex)
            {
                logger.Warn($"[VFS] PrepareCloseCommit failed: {ex.Message}");
                return false;
            }
        }
    }
}
