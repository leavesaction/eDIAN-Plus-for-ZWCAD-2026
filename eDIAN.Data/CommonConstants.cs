using System;
using System.IO;
using System.Reflection;

namespace eDIAN.Data
{
    public class CommonConstants
    {
        // 부모 프로그램 정보

        public static readonly String PARENT_PROGRAM_VERSION;

        // 플러그인 어셈블리 정보 

        public static readonly String APPLICATION_COMPANY_NAME;

        public static readonly String APPLICATION_NAME;
        public static readonly String APPLICATION_VERSION;

        public static readonly String INFORMATION_APPLICATION_NAME;
        public static readonly String INFORMATION_APPLICATION_VERSION;

        // 플러그인 어플리케이션 관련 경로

        public static readonly String PLUGIN_PATH;
        public static readonly String PLUGIN_LOG_PATH;

        public static readonly int PLUGIN_LOG_LEVEL;
        public static readonly int PLUGIN_LOG_REMAIN_DAY;

        public static readonly String PLUGIN_MIP_PATH;

        public static readonly String PLUGIN_MIP_ROOT_PATH;
        public static readonly String PLUGIN_MIP_LOG_PATH;
        public static readonly String PLUGIN_MIP_TEMP_PATH;

        // 서비스 관련 상수
        public enum ConnectStatus { CONNECTED, PAUSED, DISCONNECT }

        public static readonly String SERVICE_PIPE_UUID;

        public static readonly String SERVICE_ID;                     // 서비스 송수신 메세지

        public static readonly int SERVICE_TASK_INTERVAL = 1000;      // 서비스 대기 시간

        public static readonly int SERVICE_MAX_RETRY_COUNT = 5;       // 최대 재시도 횟수         

        // MIP 관련 상수
        public enum AuthStatus { AUTH, NONE }                         // MIP 인증 여부

        // 파일 ACL 적용 여부
        public static bool IS_FILE_ACL;

        // 화면 캡쳐 방지 적용 여부 
        public static readonly bool IS_SCREEN_PROTECT;

        // AutoCAD 메인 윈도우 핸들
        public static IntPtr CAD_MAIN_WINDOW_HANDLE;

        // 레이블링 전 암호화 된 파일의 최대 크기 (바이트 단위)
        public static long MAX_PROTECTED_FILE_SIZE;

        // 사용 언어
        public static String GLOBAL_KEY;

        public static readonly bool IS_ASC;

        public static String MODE;     // 운영 모드 (PRODUCT, DEVELOP)

        static CommonConstants()
        {
            // 부모 프로그램 정보 초기화

            PARENT_PROGRAM_VERSION = "ZWCAD 2026";

            // 플러그인 어셈블리 정보 초기화

            Assembly assembly = Assembly.GetExecutingAssembly();

            APPLICATION_COMPANY_NAME = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;

            APPLICATION_NAME = "edian+";
            APPLICATION_VERSION = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;

            INFORMATION_APPLICATION_NAME = assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title;
            INFORMATION_APPLICATION_VERSION = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;

            // 플러그인 어플리케이션 관련 경로

            PLUGIN_PATH = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            PLUGIN_LOG_PATH = Path.Combine(PLUGIN_PATH, "logs");
            PLUGIN_LOG_LEVEL = 2;
            PLUGIN_LOG_REMAIN_DAY = 7;

            PLUGIN_MIP_ROOT_PATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "edian+", "mip_data");

            PLUGIN_MIP_PATH = Path.Combine(PLUGIN_MIP_ROOT_PATH, "mip");
            PLUGIN_MIP_LOG_PATH = Path.Combine(PLUGIN_MIP_ROOT_PATH, "mip", "logs");
            PLUGIN_MIP_TEMP_PATH = Path.Combine(PLUGIN_MIP_ROOT_PATH, "mip", "temp");

            // 서비스 관련 상수 초기화

            SERVICE_PIPE_UUID = "9b9b8274-d230-41ce-800e-ac6ed15845f6";

            SERVICE_ID = $"EDIAN_{SERVICE_PIPE_UUID}";

            SERVICE_TASK_INTERVAL = 2000;       // 서비스 대기 시간 (2 Seconds)

            SERVICE_MAX_RETRY_COUNT = 50;        // 최대 재시도 횟수         

            // 파일 보안 적용 관련 상수 초기화 

            IS_FILE_ACL = false;

            // MIP 관련 상수 초기화 

            IS_SCREEN_PROTECT = true;           // 화면 캡쳐 방지 적용 여부

            MAX_PROTECTED_FILE_SIZE = 60L * 1024 * 1024 * 1024;

            GLOBAL_KEY = "ENG";     // 사용 언어 (KOR, ENG)

            MODE = "PRODUCT";     // 운영 모드 (PRODUCT, DEVELOP)

            IS_ASC = false;
        }
    }
}
