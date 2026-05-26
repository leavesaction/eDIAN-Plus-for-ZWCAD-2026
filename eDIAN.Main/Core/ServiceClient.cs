using eDIAN.Core;
using eDIAN.Data;
using eDIAN.Service.Core;
using log4net;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace eDIAN.Main.Core
{
    public class ServiceClient
    {
        private readonly ILog slogger = PluginLogger.getLogger("ServiceClient", "application.log");
        private readonly ILog clogger = PluginLogger.getLogger("Client", "service_client.log");

        public event EventHandler OnConnectToServiceServer;        // 서버 연결시 발생하는 이벤트 (기존에 열려 있는 문서를 모두 닫는다)

        private NamedPipeClient serviceClient;

        private String serviceId;

        // 서비스 서버 연결 시도 수
        private int retryCount;


        public ServiceClient()
        {
            this.retryCount = 0;

            // 서비스 아이디 구성 (EDIAN_UID_YYYYMMDDHHMMSS)
            this.serviceId = $"{CommonConstants.SERVICE_ID}_{DateTime.Now.ToString("yyyyMMddHHmmss")}";
        }

        // 이벤트 핸들러 초기화
        private void initialize() 
        {
            // 파이프 명 : pipe(SystemCore.APPLICATION_NAME + "_" + SystemCore.PIPE_UUID + "_" + 현재일시(14자리문자열)) 명을 인수로 전달
            // 프로세스간 통신을 위한 클라이언트 생성 및 이벤트 핸들러 등록
            this.serviceClient = new NamedPipeClient(this.serviceId);

            this.serviceClient.ClientStarted     += OnClientStarted;
            this.serviceClient.ConnectedToServer += OnConnectedToServer;
            this.serviceClient.MessageReceived   += OnMessageReceived;
            this.serviceClient.Disconnected      += OnDisconnected;
        }

        public Task PausedToServiceServerAsync()
        {
            return pausedToServiceServerAsync();
        }

        private async Task pausedToServiceServerAsync()
        {
            try
            {
                String message = $"PAUSE_{CommonConstants.SERVICE_PIPE_UUID}";

                clogger.Debug($"* Send ping message to Server. {message}");

                if (this.serviceClient != null)
                {
                    await this.serviceClient.Send(message).ConfigureAwait(false);
                }

                retryCount = 0;

                PluginApplication.connectStatus = CommonConstants.ConnectStatus.PAUSED;
            }
            catch (Exception ex)
            {
                clogger.Error("pausedToServiceServerAsync", ex);
                throw;
            }
        }

        /// <summary>
        /// 서비스 서버 실행 및 클라이언트와 연결
        /// </summary>
        /// <returns></returns>
        public async Task connectToServiceServer()
        {
            // 서비스 서버 실행 파일 경로
            String serviceProgramPath = Path.Combine(CommonConstants.PLUGIN_PATH, "eDIAN.Service.exe");

            // 서비스 서버 실행 파일 처리 객체 생성
            FileInfo fi = new FileInfo(serviceProgramPath);

            if (!fi.Exists)
            {
                slogger.Debug("eDIAN Service Server waiting.");

                MessageHandler.Show("error.service.server");        // 서비스 서버가 생성되지 않았습니다.\n서비스 서버 실행환경을 확인하세요.

                return;
            }

            // 서비스 서버 실행 파일이 존재하는 경우
            try
            {
                slogger.Debug("1. 서비스 서버 실행");

                // 1. 서비스 서버 프로그램 실행 
                Process ps = new Process();

                // 실행 파일 경로 설정
                ps.StartInfo.FileName = serviceProgramPath;

                // 전달할 인수 설정 (서비스 아이디. 클라이언트에서 생성된 서비스 아이디로 서버를 기동)
                string sessionDir = Environment.GetEnvironmentVariable("EDIAN_VFS_SESSION_DIR") ?? "";
                ps.StartInfo.Arguments = $"{this.serviceId} \"{sessionDir}\"";

                ps.StartInfo.UseShellExecute = false; // 인수/리다이렉션 시 권장
                ps.StartInfo.CreateNoWindow = true;   // 실행창 표시 안함
                ps.Start();

                slogger.Debug("2. 서비스 클라이언트 실행 및 서버 접속");

                // 2.1 서비스 클라이언트(serviceClient) 초기화
                this.initialize();

                // 2.2 서비스 클라이언트(serviceClient) 기동
                await this.startClientAsync();

                // 3. 이벤트 호출 열려 있는 CAD 문서 파일을 모두 닫는다.
                OnConnectToServiceServer?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                clogger.Error($"connectToServiceServer Exception", ex);
            }
        }

        private async Task startClientAsync()
        {
            if (this.serviceClient == null) 
            { 
                clogger.Debug("serviceClient is null");
                return;
            }

            // 클라이언트 취소 요청 정보 
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                String message = String.Empty;

                if (PluginApplication.connectStatus == CommonConstants.ConnectStatus.DISCONNECT)
                {
                    // 연결되지 않은 경우 연결 시도

                    try
                    {
                        await this.serviceClient.Connect(CommonConstants.SERVICE_TASK_INTERVAL);
                    }
                    catch (TimeoutException)
                    {
                        clogger.Error("Connection timed out, retrying...");
                    }
                    catch (Exception e)
                    {
                        clogger.Error($"startClientAsync Exception", e);
                    }
                }
                else
                {
                    // 연결된 후 PING 메세지 전송

                    if (PluginApplication.connectStatus == CommonConstants.ConnectStatus.CONNECTED)
                    {
                        message = "PING";
                    }
                    else if (PluginApplication.connectStatus == CommonConstants.ConnectStatus.PAUSED)
                    {
                        message = "PAUSE";
                    }

                    message += $"_{CommonConstants.SERVICE_PIPE_UUID}";

                    try
                    {
                        await this.serviceClient.Send(message);
                    }
                    catch (TaskCanceledException tce)
                    {
                        clogger.Error($" - startClientAsync TaskCanceledException", tce);
                    }
                    catch (Exception e)
                    {
                        clogger.Debug($" - startClientAsync Exception", e);
                    }
                }

                // 서비스 서버 연결 후 1초 대기, 메세지 보낼때마다 1초 대기. 다섯번 대기 후 Cancel 요청 (클라이언트 종료)
                try
                {
                    await Task.Delay(CommonConstants.SERVICE_TASK_INTERVAL, cancellationTokenSource.Token); // 1초 대기

                    if (PluginApplication.connectStatus == CommonConstants.ConnectStatus.CONNECTED)
                    {
                        retryCount++;
                    }
                    else if (PluginApplication.connectStatus == CommonConstants.ConnectStatus.PAUSED)
                    {
                        retryCount = 0;
                    }

                }
                catch (Exception ex)
                {
                    // 작업 취소 예외 처리
                    clogger.Error($" - startClientAsync Exception", ex);
                }

                if (retryCount > CommonConstants.SERVICE_MAX_RETRY_COUNT)
                {
                    PluginApplication.connectStatus = CommonConstants.ConnectStatus.DISCONNECT; // 연결 상태를 DISCONNECT로 설정

                    clogger.Debug($"* Maximum retry count exceeded, stopping client. {PluginApplication.connectStatus}");

                    cancellationTokenSource.Cancel(); // 최대 재시도 횟수 초과 시 작업 취소
                }
            }

            clogger.Debug("StartAsync Closed");
        }

        private void OnClientStarted(Object sender, EventArgs e)
        {
            clogger.Debug($"edian+ service client started with serviceId : {this.serviceId}");
        }

        private void OnConnectedToServer(Object sender, EventArgs e)
        {
            PluginApplication.connectStatus = CommonConstants.ConnectStatus.CONNECTED;

            clogger.Debug($"* Client connected to server. {PluginApplication.connectStatus}");

        }

        private void OnMessageReceived(Object sender, MessageReceivedEventArgs e)
        {
            String msg = e.Message;

            if (msg != null)
            {
                if (msg.CompareTo($"PINGED_{CommonConstants.SERVICE_PIPE_UUID}") == 0 || msg.CompareTo($"PAUSED_{CommonConstants.SERVICE_PIPE_UUID}") == 0)
                {
                    retryCount = 0; // 연결 성공 시 재시도 횟수 초기화

                    PluginApplication.connectStatus = CommonConstants.ConnectStatus.CONNECTED;
                }
            }
        }

        private void OnDisconnected(Object sender, EventArgs e)
        {
            retryCount = 0; // 연결 성공 시 재시도 횟수 초기화

            PluginApplication.connectStatus = CommonConstants.ConnectStatus.DISCONNECT;

            clogger.Debug($"* Server disconnected. {PluginApplication.connectStatus}");
        }
    }
}
