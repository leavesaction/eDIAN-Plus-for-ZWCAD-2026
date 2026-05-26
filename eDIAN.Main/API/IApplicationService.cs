using eDIAN.Core;
using eDIAN.Data;
using eDIAN.Main;
using eDIAN.Main.Core;
using eDIAN.Main.Data;
using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace eDIAN.Main.API
{
    /// <summary>
    /// 애플리케이션 서비스 API 호출을 위한 추상 클래스
    /// </summary>
    public abstract class IApplicationService
    {
        protected static readonly HttpClient SharedHttpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(120)
        };

        protected static readonly ILog HttpLogger = PluginLogger.getLogger("ApplicationService", "application.log");

        public static IApplicationService instance;

        static IApplicationService()
        {
            if (ServiceConstants.SERVICE_COMPANY.Equals("MAIUS", StringComparison.OrdinalIgnoreCase))
            {
                instance = new MaiusService();
            }
            else if (ServiceConstants.SERVICE_COMPANY.Equals("ZAISOFT", StringComparison.OrdinalIgnoreCase))
            {
                instance = new ZaisoftService();
            }
        }

        public abstract Task<UserLicenseData> CallGetUserLicenseDataAsync(string displayName, string userPrincipalName, CancellationToken cancellationToken = default);

        public abstract Task<List<UserLicenseData>> CallGetUserLicenseDataListAsync(CancellationToken cancellationToken = default);

        public abstract Task<bool> CallSaveUserActionLogAsync(string contentId, string filePath, string command, string errorCode, CancellationToken cancellationToken = default);

        public abstract Task<List<LabelData>> CallGetDefaultLabelListAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// CAD 동기 이벤트 등에서 감사 로그 전송 (await 없이, 예외는 로그만).
        /// </summary>
        public static void FireAndForgetSaveUserActionLog(string contentId, string filePath, string command, string errorCode)
        {
            if (instance == null)
            {
                return;
            }

            RunFireAndForget(
                () => instance.CallSaveUserActionLogAsync(contentId, filePath, command, errorCode),
                nameof(CallSaveUserActionLogAsync));
        }

        public static void RunFireAndForget(Func<Task> taskFactory, string operationName)
        {
            _ = RunFireAndForgetCore(taskFactory, operationName);
        }

        private static async Task RunFireAndForgetCore(Func<Task> taskFactory, string operationName)
        {
            try
            {
                await taskFactory().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HttpLogger.Error($"{operationName} (fire-and-forget)", ex);
            }
        }

        protected static async Task<string> PostForResponseBodyAsync(
            HttpRequestMessage request,
            string logContext,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using HttpResponseMessage response = await SharedHttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    HttpLogger.Debug($"{logContext}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                    return null;
                }

                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                HttpLogger.Error($"{logContext} TimeoutException", ex);
                return null;
            }
            catch (TaskCanceledException ex)
            {
                HttpLogger.Error($"{logContext} TaskCanceledException", ex);
                return null;
            }
            catch (Exception ex)
            {
                HttpLogger.Error($"{logContext} Exception", ex);
                return null;
            }
        }

        protected static HttpRequestMessage CreatePostRequest(String url, HttpContent content, Action<HttpRequestMessage> configure = null)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url ?? string.Empty);
            if (content != null)
            {
                request.Content = content;
            }

            configure?.Invoke(request);
            return request;
        }

        protected ServiceRequestData CreateLogRequestData(string contentId, string filePath, string command, string errorCode)
        {
            return new ServiceRequestData()
            {
                tenantId = ServiceConstants.TENANT_ID,
                clientId = ServiceConstants.CLIENT_ID,
                userId = PluginApplication.userLicenseData.userId,
                licType = PluginApplication.userLicenseData.licenseType,
                pcIp = GetLocalIPAddress(),
                macAddr = GetMacAddress(),
                appVersion = CommonConstants.INFORMATION_APPLICATION_VERSION ?? "",
                appType = "CAD",
                programVersion = CommonConstants.PARENT_PROGRAM_VERSION ?? "",
                contentId = contentId,
                filePath = filePath,
                command = command,
                errorCode = errorCode,
                errorYn = errorCode == "0" ? "N" : "Y",
                errorOccurrenceYn = errorCode == "0" ? "N" : "Y"
            };
        }

        protected static void SetDefaultViewOnlyLicense(UserLicenseData userLicenseData)
        {
            userLicenseData.hasLicense = false;
            userLicenseData.licenseType = "V";
            userLicenseData.licenseTypeName = "Viewer";
            userLicenseData.isOnlyView = true;
        }

        protected String GetLocalIPAddress()
        {
            try
            {
                foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetLocalIPAddress error: {ex.Message}");
            }

            return "0.0.0.0";
        }

        protected String GetMacAddress()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni == null) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;

                    PhysicalAddress addr = ni.GetPhysicalAddress();

                    if (addr != null && addr.GetAddressBytes().Length > 0)
                    {
                        return string.Join(":", addr.GetAddressBytes().Select(b => b.ToString("X2")));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetMacAddress error: {ex.Message}");
            }

            return string.Empty;
        }

        protected bool IsViewOnlyLicense(string licenseType)
        {
            return licenseType switch
            {
                "F" => false,
                "D" => false,
                "V" => true,
                _ => true
            };
        }

        protected bool IsSuccessErrorCode(string errorCode)
        {
            return errorCode == "0";
        }
    }
}
