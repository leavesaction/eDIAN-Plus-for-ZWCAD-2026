using eDIAN.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace eDIAN.Main.API
{
    internal class MicrosoftService
    {
        private static MicrosoftService _instance;

        public static MicrosoftService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MicrosoftService();
                }
                return _instance;
            }
        }

        private MicrosoftService()
        {

        }

        /// <summary>
        /// 액세스 토큰으로 인증 정보 조회
        /// </summary>
        /// <param name="accessToken">액세스 토큰</param>
        /// <returns>인증 결과 JSON 문자열</returns>
        public async Task<String> callAuthentificationWithToken(string accessToken)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

            using (HttpClient httpClient = new HttpClient())
            {
                try
                {
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, ServiceConstants.MIP_GRAPH_API_ENDPOINT);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    HttpResponseMessage response = await httpClient.SendAsync(request);

                    return await response.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(" - callAuthentificationWithToken" + ex);
                    throw;
                }
            }
        }
    }
}
