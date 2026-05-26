using eDIAN.Data;
using eDIAN.Core;
using eDIAN.Service.Core;
using log4net;
using System.IO;
using System.Threading;
using System;
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
            if (!Directory.Exists(targetDir)) return;

            for (int retry = 1; retry <= 3; retry++)
            {
                try
                {
                    string[] files = Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories);
                    bool allFilesDeleted = true;

                    // 1. 하위 파일 속성 완전 해제 및 강제 1차 소거
                    foreach (string file in files)
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                        }
                        catch (IOException) // 다른 임시 백신/탐색기 락 충돌 시
                        {
                            allFilesDeleted = false;
                        }
                        catch (Exception ex)
                        {
                            allFilesDeleted = false;
                            slogger.Error($" - [VFS] 파일 삭제 에러: {file} - {ex.Message}");
                        }
                    }

                    // 2. 파일 소거가 무사히 끝났다면, 폴더 최종 소거 후 반환
                    if (allFilesDeleted)
                    {
                        string[] subDirs = Directory.GetDirectories(targetDir, "*", SearchOption.AllDirectories);
                        foreach (string subDir in subDirs)
                        {
                            try { File.SetAttributes(subDir, FileAttributes.Normal); } catch { }
                        }

                        Directory.Delete(targetDir, true);
                        slogger.Debug($" - [VFS] 가상 세션 디렉터리 소거 완수: {targetDir}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    slogger.Error($" - [VFS] 디렉터리 소거 {retry}회차 실패: {targetDir} (사유: {ex.Message})");
                }

                // 백신 및 셸 스캔이 풀릴 시간을 주기 위해 100ms 대기 후 재시도
                if (retry < 3)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}
