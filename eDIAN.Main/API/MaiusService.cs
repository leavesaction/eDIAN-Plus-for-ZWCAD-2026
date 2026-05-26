using eDIAN.Core;
using eDIAN.Data;
using eDIAN.Main.API;
using eDIAN.Main.Data;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace eDIAN.Main.Core
{
    public class MaiusService : IApplicationService
    {
        private static readonly ILog logger = PluginLogger.getLogger("MaiusServiceImpl", "application.log");

        public override async Task<UserLicenseData> CallGetUserLicenseDataAsync(string displayName, string userPrincipalName, CancellationToken cancellationToken = default)
        {
            UserLicenseData userLicenseData = new UserLicenseData()
            {
                userName = displayName,
                userId = userPrincipalName
            };

            String accessToken = PluginApplication.authenticationResult.AccessToken;

            if (String.IsNullOrEmpty(accessToken))
            {
                return userLicenseData;
            }

            ServiceRequestData data = new ServiceRequestData()
            {
                tenantId = ServiceConstants.TENANT_ID,
                clientId = ServiceConstants.CLIENT_ID,
                userId = userPrincipalName
            };

            string jsonData = JsonConvert.SerializeObject(data);
            StringContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");

            using HttpRequestMessage request = CreatePostRequest(
                ServiceConstants.GET_LICNESE_API_ENDPOINT,
                content,
                r =>
                {
                    r.Headers.TryAddWithoutValidation("User-Agent", ServiceConstants.HEADER_USER_AGENT);
                    r.Headers.TryAddWithoutValidation("secret-key", ServiceConstants.HEADER_SECRET_KEY);
                });

            String resultData = await PostForResponseBodyAsync(request, "getUserLicenseData", cancellationToken).ConfigureAwait(false);

            if (resultData != null)
            {
                JObject json = JObject.Parse(resultData);
                bool success = Convert.ToBoolean(json["success"]);

                if (success)
                {
                    JObject licenseData = json["data"] as JObject;

                    if (licenseData != null)
                    {
                        userLicenseData.hasLicense = true;
                        userLicenseData.licenseType = ((licenseData["licTyp"]) ?? "").ToString();
                        userLicenseData.licenseTypeName = ((licenseData["licTypNm"]) ?? "").ToString();
                        userLicenseData.isOnlyView = IsViewOnlyLicense(userLicenseData.licenseType);
                    }
                    else
                    {
                        SetDefaultViewOnlyLicense(userLicenseData);
                        logger.Debug(" - getUserLicenseData : UserLicenseData is null.");
                    }
                }
            }

            return userLicenseData;
        }

        public override async Task<List<UserLicenseData>> CallGetUserLicenseDataListAsync(CancellationToken cancellationToken = default)
        {
            List<UserLicenseData> userLicenseDataList = new List<UserLicenseData>();

            ServiceRequestData data = new ServiceRequestData()
            {
                appType = "CAD",
                tenantId = ServiceConstants.TENANT_ID,
                clientId = ServiceConstants.CLIENT_ID
            };

            string jsonData = JsonConvert.SerializeObject(data);
            StringContent requestContents = new StringContent(jsonData, Encoding.UTF8, "application/json");

            using HttpRequestMessage request = CreatePostRequest(
                ServiceConstants.GET_USER_LICNESE_LIST_API_ENDPOINT,
                requestContents,
                r =>
                {
                    r.Headers.TryAddWithoutValidation("User-Agent", ServiceConstants.HEADER_USER_AGENT);
                    r.Headers.TryAddWithoutValidation("secret-key", ServiceConstants.HEADER_SECRET_KEY);
                });

            String resultData = await PostForResponseBodyAsync(request, "getUserLicenseDataList", cancellationToken).ConfigureAwait(false);

            if (resultData != null)
            {
                logger.Debug($" - getUserLicenseDataList : {resultData}");

                JObject json = JObject.Parse(resultData);
                bool success = Convert.ToBoolean(json["success"]);

                if (success && json["data"] is JArray dataArray)
                {
                    foreach (JObject licenseData in dataArray)
                    {
                        userLicenseDataList.Add(new UserLicenseData()
                        {
                            userName = Convert.ToString(licenseData["userNm"]),
                            userId = Convert.ToString(licenseData["userId"]),
                            licenseType = Convert.ToString(licenseData["licTyp"]),
                            licenseTypeName = Convert.ToString(licenseData["licTypNm"])
                        });
                    }
                }
                else
                {
                    logger.Debug(" - getUserLicenseDataList : Data is null.");
                }
            }

            return userLicenseDataList;
        }

        public override async Task<bool> CallSaveUserActionLogAsync(string contentId, string filePath, string command, string errorCode, CancellationToken cancellationToken = default)
        {
            String accessToken = PluginApplication.authenticationResult.AccessToken;

            if (String.IsNullOrEmpty(accessToken))
            {
                return false;
            }

            ServiceRequestData data = CreateLogRequestData(contentId, filePath, command, errorCode);
            String jsonData = JsonConvert.SerializeObject(data);
            StringContent requestContents = new StringContent(jsonData, Encoding.UTF8, "application/json");

            using HttpRequestMessage request = CreatePostRequest(
                ServiceConstants.SEND_USER_ACTION_LOG_API_ENDPOINT,
                requestContents,
                r =>
                {
                    r.Headers.TryAddWithoutValidation("User-Agent", ServiceConstants.HEADER_USER_AGENT);
                    r.Headers.TryAddWithoutValidation("secret-key", ServiceConstants.HEADER_SECRET_KEY);
                });

            String resultData = await PostForResponseBodyAsync(request, "sendUserActionLog", cancellationToken).ConfigureAwait(false);

            if (resultData == null)
            {
                return false;
            }

            JObject json = JObject.Parse(resultData);
            bool result = Convert.ToBoolean(json["success"]);

            if (result)
            {
                logger.Debug(" - sendUserActionLog : User action log sent successfully.");
            }
            else
            {
                logger.Debug(" - sendUserActionLog : Failed to send user action log.");
            }

            return result;
        }

        public override Task<List<LabelData>> CallGetDefaultLabelListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<List<LabelData>>(null);
        }
    }
}
