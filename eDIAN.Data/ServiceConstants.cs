using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eDIAN.Data
{
    public class ServiceConstants
    {
        // 관리자 사이트 API 호출 관련 정보 (추후 accessToken으로 교체 예정)

        public static readonly String SERVICE_COMPANY = "MAIUS";     // 서비스 제공 회사명 (MAIUS, ZAISOFT) - 향후 환경 변수로 대체 예정

        // MIP 인증 관련 정보

        public static String CLIENT_ID;
        public static String TENANT_ID;

        public static String SERVICE_HOST;

        public static String HEADER_USER_AGENT;
        public static String HEADER_SECRET_KEY;

        public static String GET_LICNESE_API_ENDPOINT;
        public static String GET_USER_LICNESE_LIST_API_ENDPOINT;
        public static String GET_DEFAULT_LABEL_ID_API_ENDPOINT;
        public static String SEND_USER_ACTION_LOG_API_ENDPOINT;

        // MIP 로그인 URL
        public static String MIP_LOGIN_FORM_URL;

        // API 엔드포인트를 Graph 'me' 엔드포인트로 설정.
        // (Microsoft 퍼블릭 클라우드에서 국가별 클라우드로 변경하려면 graphAPIEndpoint의 다른 값을 사용)
        // Reference with Graph endpoints here : docs.microsoft.com/graph/deployments#microsoft-graph-and-graph-explorer-service-root-endpoints
        public static String MIP_GRAPH_API_ENDPOINT;

        static ServiceConstants()
        {
            if (SERVICE_COMPANY.Equals("MAIUS", StringComparison.OrdinalIgnoreCase))
            {
                CLIENT_ID = "cf35e6da-6ad3-4b08-b0b9-77f176482e06";
                TENANT_ID = "5f40b272-74e4-455f-8481-f56fe70f3c97";

                // 관리자 사이트 API 호출 관련 정보 초기화 (내부 서버용)

                HEADER_USER_AGENT = "eDIAN+";
                HEADER_SECRET_KEY = "4b161a67-2086-4013-8001-53a3a2aed1bb";

                SERVICE_HOST = "http://edian.maius.co.kr:8848";

                if (CommonConstants.MODE.Equals("DEVELOP"))
                {
                    // 내부 IP 테스트 용
                    SERVICE_HOST = "http://192.168.0.94:8848";
                }

                GET_LICNESE_API_ENDPOINT = SERVICE_HOST + "/edian/api/checkLicense.do";
                GET_USER_LICNESE_LIST_API_ENDPOINT = SERVICE_HOST + "/edian/api/getUserLicenseList.do";
                GET_DEFAULT_LABEL_ID_API_ENDPOINT = "";     // 관리자 사이트에서 기본 라벨 ID를 제공하지 않음 (향후 API 추가 예정)
                SEND_USER_ACTION_LOG_API_ENDPOINT = SERVICE_HOST + "/edian/api/saveUserActionLog.do";
            }
            else if (SERVICE_COMPANY.Equals("ZAISOFT", StringComparison.OrdinalIgnoreCase))
            {
                CLIENT_ID = "84aefa7a-d7f2-41d9-b8b3-36264d56e722";
                TENANT_ID = "4abf3d13-08ae-436a-97ce-9684ca3f4db5";

                SERVICE_HOST = "https://mipgateway.zaisoft.co.kr/";

                if (CommonConstants.MODE.Equals("DEVELOP"))
                {
                    // 내부 IP 테스트 용
                    SERVICE_HOST = "https://mipgateway.zaisoft.co.kr/";
                }

                GET_LICNESE_API_ENDPOINT = SERVICE_HOST + "/MIPAdminV2/API/checkLicense";
                GET_USER_LICNESE_LIST_API_ENDPOINT = SERVICE_HOST + "/MIPAdminV2/API/getUserLicenseList";
                GET_DEFAULT_LABEL_ID_API_ENDPOINT = SERVICE_HOST + "/MIPAdminV2/API/getDefaultLabel";
                SEND_USER_ACTION_LOG_API_ENDPOINT = SERVICE_HOST + "/MIPAdminV2/API/saveUserActionLog";
            }
            else 
            {
                CLIENT_ID = "cf35e6da-6ad3-4b08-b0b9-77f176482e06";
            }

            MIP_LOGIN_FORM_URL     = "https://login.microsoftonline.com/";
            MIP_GRAPH_API_ENDPOINT = "https://graph.microsoft.com/v1.0/me";
        }
    }
}
