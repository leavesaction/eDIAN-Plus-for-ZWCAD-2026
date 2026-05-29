using eDIAN.Data;
using eDIAN.Core;
using eDIAN.Service.Core;
using log4net;
using System.IO;
using System.Threading;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace eDIAN.Service
{
    internal class ServiceServer
    {
        private static readonly ILog slogger = PluginLogger.getLogger("ServiceServer", "service_server.log");

        private static ServiceServer serviceMain;

        private static string mySessionDir;

        private NamedPipeServer serviceServer;

        private CancellationTokenSource cancelationTokenSource;

        private enum ConnectStatus { CONNECTED, PAUSED, DISCONNECTED }

        private static ConnectStatus connectStatus;

        private static int retryCount;

        private const int VFS_DELETE_RETRY_COUNT = 20;
        private const int VFS_DELETE_RETRY_INITIAL_DELAY_MS = 150;
        private const int VFS_DELETE_RETRY_MAX_DELAY_MS = 1500;

        [Flags]
        private enum MoveFileFlags : uint
        {
            MOVEFILE_REPLACE_EXISTING = 0x00000001,
            MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004,
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, MoveFileFlags dwFlags);

        static ServiceServer() {

            connectStatus = ConnectStatus.DISCONNECTED;
            retryCount = 0;
        }

        public ServiceServer(String serviceId) 
        {
            this.serviceServer = new NamedPipeServer(serviceId);

            // NamedPipeServer 초기화

            this.serviceServer.ServerStarted   += OnServerStarted;
            this.serviceServer.ClientConnected += OnClientConnected;
            this.serviceServer.MessageReceived += OnMessageReceived;
            this.serviceServer.Disconnected    += OnDisconnected;

            this.cancelationTokenSource = new CancellationTokenSource();
        }

        static async Task Main(String[] args)
        {
            if (args == null || args.Length == 0)
            { 
                slogger.Error(" - eDIAN Service Server Start Fail : No arguments.");

                return; 
            }
            else
            {
                slogger.Debug($" - eDIAN service server start with serviceId : {args[0]}");
            }

            string serviceId = args[0];
            if (args.Length > 1)
            {
                mySessionDir = args[1];
                slogger.Debug($" - [VFS] Registered Dedicated VFS Session Directory: {mySessionDir}");
            }

            serviceMain = new ServiceServer(serviceId);

            await serviceMain.serviceServer.Start();

            // 서버 기동 후 시간 지연을 시키다가 Cancel 요청 발생
            while (!serviceMain.cancelationTokenSource.Token.IsCancellationRequested)
            {
                await Task.Delay(CommonConstants.SERVICE_TASK_INTERVAL);

                if (connectStatus == ConnectStatus.CONNECTED)
                {
                    retryCount++;
                }
                else if (connectStatus == ConnectStatus.PAUSED)
                {
                    slogger.Debug($" - check to edian+ Service Server. Retry Count: {retryCount}");
                    retryCount = 0;
                }


                if (retryCount > CommonConstants.SERVICE_MAX_RETRY_COUNT)
                {
                    serviceMain.cancelationTokenSource.Cancel();      // Cancel 요청
                }
            }

            // 1. 임시 파일 삭제 (기존 레거시 청소)
            serviceMain.deleteMipTempFiles();

            // 2. 나 자신의 VFS 전용 가상 세션 디렉터리 정밀 강제 소거
            if (!string.IsNullOrEmpty(mySessionDir))
            {
                // CAD 종료 직후에는 파일 핸들이 잠시 남아있는 경우가 있어, 약간의 유예 후 소거를 시도한다.
                Thread.Sleep(1000);
                serviceMain.deleteVfsDirectory(mySessionDir);
            }
        }

        private void OnServerStarted(Object sender, EventArgs e)
        {
            slogger.Debug(" - Start edian+ Service Server.");
            retryCount = 0;
        }

        private void OnClientConnected(Object sender, EventArgs e)
        {
            connectStatus = ConnectStatus.CONNECTED;
            slogger.Debug(" - Connect to edian+ Service Client");
        }

        private async void OnMessageReceived(Object sender, MessageReceivedEventArgs e)
        {
            String msg = e.Message;
            String sendMsg = String.Empty;

            // slogger.Debug($" - Receive from client's Message : {msg} : {retryCount}");

            if (msg.CompareTo($"PING_{CommonConstants.SERVICE_PIPE_UUID}") == 0)
            {
                connectStatus = ConnectStatus.CONNECTED;
                retryCount = 0; // 연결 성공 시 재시도 횟수 초기화

                //slogger.Debug("PING received from cilent.");

                sendMsg = $"PINGED_{CommonConstants.SERVICE_PIPE_UUID}";
            }
            else if (msg.CompareTo($"PAUSE_{CommonConstants.SERVICE_PIPE_UUID}") == 0)
            {
                connectStatus = ConnectStatus.PAUSED;
                retryCount = 0; // 연결 성공 시 재시도 횟수 초기화

                //slogger.Debug("PING received from cilent.");

                sendMsg = $"PAUSED_{CommonConstants.SERVICE_PIPE_UUID}";
            }

            if(!String.IsNullOrEmpty(sendMsg))
            {
                await serviceServer.Send($"{sendMsg}");
            }
        }

        private void OnDisconnected(Object sender, EventArgs e)
        {
            slogger.Debug($" - Disconnect to edian+ Service Client.");

            serviceMain?.cancelationTokenSource.Cancel();
        }

        /// <summary>
        /// 임시 MIP 임시 경로내 파일 삭제
        /// </summary>
        /// <param name="mipDataPath"></param>
        private void deleteMipTempFiles()
        {
            String mipTempPath = CommonConstants.PLUGIN_MIP_TEMP_PATH;

            // slogger.Debug($" - deleteMipTempFiles : Mip Temp Directory : {mipTempPath}");

            if (Directory.Exists(mipTempPath) == false)
            {
                slogger.Debug($" - deleteMipTempFiles : Mip Temp Directory not exists.");

                return;
            }

            String[] files = Directory.GetFiles(mipTempPath, "*.*");

            if (files == null || files.Length == 0)
            {
                slogger.Debug($" - deleteMipTempFiles : No files to delete in Mip Temp Directory : {mipTempPath}");
                return;
            }

            foreach (String file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    slogger.Error($" - deleteMipTempFiles : deleting file error : {file} : {ex.Message}");
                }

                slogger.Debug($" - deleteMipTempFiles : ...\\*.*");
            }
        }

        /// <summary>
        /// 어떠한 보안 속성이 걸려있어도 100% 안전하게 나 자신의 VFS 가상 샌드박스를 강제 증발시키는 3회 지연 재시도 삭제 함수
        /// </summary>
        private void deleteVfsDirectory(string targetDir)
        {
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                slogger.Warn(" - [VFS] deleteVfsDirectory skipped: targetDir is empty.");
                return;
            }

            if (!Directory.Exists(targetDir))
            {
                slogger.Debug($" - [VFS] deleteVfsDirectory skipped (not exists): {targetDir}");
                return;
            }

            slogger.Debug($" - [VFS] deleteVfsDirectory begin: {targetDir}");

            int delayMs = VFS_DELETE_RETRY_INITIAL_DELAY_MS;
            for (int retry = 1; retry <= VFS_DELETE_RETRY_COUNT; retry++)
            {
                try
                {
                    // 폴더 자체도 숨김/시스템일 수 있으므로 우선 속성 해제
                    try { File.SetAttributes(targetDir, FileAttributes.Normal); } catch { }

                    bool hadDeleteFailures = false;

                    // 1) 하위 파일 속성 완전 해제 및 강제 소거
                    foreach (string file in Directory.EnumerateFiles(targetDir, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                        }
                        catch (IOException ioex) // 다른 임시 백신/탐색기 락 충돌 시
                        {
                            hadDeleteFailures = true;
                            slogger.Warn($" - [VFS] 파일 삭제 IOException (retry {retry}): {file} - {ioex.Message}");
                        }
                        catch (UnauthorizedAccessException uaex)
                        {
                            hadDeleteFailures = true;
                            slogger.Warn($" - [VFS] 파일 삭제 Unauthorized (retry {retry}): {file} - {uaex.Message}");
                        }
                        catch (Exception ex)
                        {
                            hadDeleteFailures = true;
                            slogger.Error($" - [VFS] 파일 삭제 에러 (retry {retry}): {file} - {ex.Message}");
                        }
                    }

                    // 2) 하위 폴더 속성 해제 후 디렉터리 삭제 시도 (파일 일부 실패가 있어도 시도)
                    foreach (string subDir in Directory.EnumerateDirectories(targetDir, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            File.SetAttributes(subDir, FileAttributes.Normal);
                        }
                        catch
                        {
                            // ignore
                        }
                    }

                    try
                    {
                        Directory.Delete(targetDir, true);
                        slogger.Debug($" - [VFS] 가상 세션 디렉터리 소거 완수: {targetDir} (hadDeleteFailures={hadDeleteFailures})");
                        return;
                    }
                    catch (Exception dex)
                    {
                        slogger.Error($" - [VFS] 디렉터리 삭제 실패 (retry {retry}): {targetDir} (사유: {dex.Message})");
                    }
                }
                catch (Exception ex)
                {
                    slogger.Error($" - [VFS] 디렉터리 소거 {retry}회차 예외: {targetDir} (사유: {ex.Message})");
                }

                // 백신 및 셸 스캔/지연 해제 시간을 주고 재시도 (완만한 백오프)
                Thread.Sleep(delayMs);
                delayMs = Math.Min(delayMs * 2, VFS_DELETE_RETRY_MAX_DELAY_MS);
            }

            // 최종 실패 시 현재 속성/존재 여부를 남겨서 현장 추적 가능하게 함
            try
            {
                bool exists = Directory.Exists(targetDir);
                string attrs = exists ? new DirectoryInfo(targetDir).Attributes.ToString() : "(n/a)";
                slogger.Warn($" - [VFS] deleteVfsDirectory final failed: exists={exists}, attrs={attrs}, dir={targetDir}");
            }
            catch (Exception ex)
            {
                slogger.Warn($" - [VFS] deleteVfsDirectory final failed (unable to stat): {targetDir} : {ex.Message}");
            }

            // 최후 수단: 재부팅 시 삭제 예약 (잠금이 오래 가는 환경 대비)
            try
            {
                scheduleDeleteOnReboot(targetDir);
            }
            catch (Exception ex)
            {
                slogger.Warn($" - [VFS] scheduleDeleteOnReboot failed: {targetDir} : {ex.Message}");
            }
        }

        private void scheduleDeleteOnReboot(string targetDir)
        {
            if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
            {
                return;
            }

            int scheduledCount = 0;

            // 파일부터 예약 (디렉터리 삭제는 파일이 먼저 없어져야 성공 확률이 높음)
            foreach (string file in Directory.EnumerateFiles(targetDir, "*", SearchOption.AllDirectories))
            {
                if (MoveFileEx(file, null, MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT))
                {
                    scheduledCount++;
                }
            }

            // 디렉터리도 예약 (실패해도 로그만 남김)
            bool dirScheduled = MoveFileEx(targetDir, null, MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT);
            int err = Marshal.GetLastWin32Error();

            slogger.Warn($" - [VFS] deleteVfsDirectory scheduled on reboot: files={scheduledCount}, dirScheduled={dirScheduled}, win32err={err}, dir={targetDir}");
        }
    }
}
