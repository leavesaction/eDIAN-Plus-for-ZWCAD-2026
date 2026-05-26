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
    public class ZaisoftService : IApplicationService
    {
        private static readonly ILog logger = PluginLogger.getLogger("ZaisoftServiceImpl", "application.log");

        public override async Task<UserLicenseData> CallGetUserLicenseDataAsync(string displayName, string userPrincipalName, CancellationToken cancellationToken = default)
        {
            UserLicenseData userLicenseData = new UserLicenseData()
            {
                userName = displayName,
                userId = userPrincipalName
            };

            String accessToken = PluginApplication.authenticationResult?.AccessToken;

            if (string.IsNullOrEmpty(accessToken))
            {
                return userLicenseData;
            }

            using HttpRequestMessage request = CreatePostRequest(
                ServiceConstants.GET_LICNESE_API_ENDPOINT,
                null,
                r => r.Headers.TryAddWithoutValidation("authorization", $"Bearer {accessToken}"));

            String resultData = await PostForResponseBodyAsync(request, "callGetUserData", cancellationToken).ConfigureAwait(false);

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
                        logger.Debug(" - callGetUserData : UserLicenseData is null.");
                    }
                }
            }

            return userLicenseData;
        }

        public override async Task<List<UserLicenseData>> CallGetUserLicenseDataListAsync(CancellationToken cancellationToken = default)
        {
            List<UserLicenseData> userLicenseDataList = new List<UserLicenseData>();

            String accessToken = PluginApplication.authenticationResult?.AccessToken;

            if (string.IsNullOrEmpty(accessToken))
            {
                return userLicenseDataList;
            }

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
                r => r.Headers.TryAddWithoutValidation("authorization", $"Bearer {accessToken}"));

            String resultData = await PostForResponseBodyAsync(request, "callGetUserDataList", cancellationToken).ConfigureAwait(false);

            if (resultData != null)
            {
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
                    logger.Debug(" - callGetUserDataList : Data is null.");
                }
            }

            return userLicenseDataList;
        }

        public override async Task<bool> CallSaveUserActionLogAsync(string contentId, string filePath, string command, string errorCode, CancellationToken cancellationToken = default)
        {
            String accessToken = PluginApplication.authenticationResult?.AccessToken;

            if (string.IsNullOrEmpty(accessToken))
            {
                return false;
            }

            ServiceRequestData data = CreateLogRequestData(contentId, filePath, command, errorCode);
            string jsonData = JsonConvert.SerializeObject(data);
            StringContent requestContents = new StringContent(jsonData, Encoding.UTF8, "application/json");

            using HttpRequestMessage request = CreatePostRequest(
                ServiceConstants.SEND_USER_ACTION_LOG_API_ENDPOINT,
                requestContents,
                r => r.Headers.TryAddWithoutValidation("authorization", $"Bearer {accessToken}"));

            String resultData = await PostForResponseBodyAsync(request, "callSaveClientLog", cancellationToken).ConfigureAwait(false);

            if (resultData == null)
            {
                return false;
            }

            JObject json = JObject.Parse(resultData);
            bool result = Convert.ToBoolean(json["success"]);

            if (result)
            {
                logger.Debug(" - callSaveClientLog : User action log sent successfully.");
            }
            else
            {
                logger.Debug(" - callSaveClientLog : Failed to send user action log.");
            }

            return result;
        }

        public override async Task<List<LabelData>> CallGetDefaultLabelListAsync(CancellationToken cancellationToken = default)
        {
            String accessToken = PluginApplication.authenticationResult?.AccessToken;

            if (string.IsNullOrEmpty(accessToken))
            {
                return null;
            }

            ServiceRequestData data = new ServiceRequestData()
            {
                appType = "CAD",
                tenantId = ServiceConstants.TENANT_ID,
                clientId = ServiceConstants.CLIENT_ID
            };

            string jsonData = JsonConvert.SerializeObject(data);
            StringContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");

            using HttpRequestMessage request = CreatePostRequest(
                ServiceConstants.GET_DEFAULT_LABEL_ID_API_ENDPOINT,
                content,
                r => r.Headers.TryAddWithoutValidation("authorization", $"Bearer {accessToken}"));

            String resultData = await PostForResponseBodyAsync(request, "callGetDefaultLabelList", cancellationToken).ConfigureAwait(false);

            if (resultData == null)
            {
                return null;
            }

            JObject json = JObject.Parse(resultData);
            bool success = Convert.ToBoolean(json["success"]);

            if (!success)
            {
                return null;
            }

            List<LabelData> result = new List<LabelData>();

            if (json["data"] is JArray dataArray)
            {
                foreach (JObject defaultLabelData in dataArray!)
                {
                    result.Add(new LabelData()
                    {
                        targetType = Convert.ToString(defaultLabelData["targetType"]) ?? "",
                        targetId = Convert.ToString(defaultLabelData["targetId"]) ?? "",
                        labelId = Convert.ToString(defaultLabelData["labelId"]) ?? "",
                        labelName = Convert.ToString(defaultLabelData["labelName"]) ?? "",
                        displayName = Convert.ToString(defaultLabelData["displayName"]) ?? "",
                        isDefaultLabel = Convert.ToBoolean(defaultLabelData["isDefault"])
                    });
                }
            }
            else
            {
                logger.Debug(" - callGetDefaultLabelList : Data is null.");
            }

            return result;
        }
    }
}
