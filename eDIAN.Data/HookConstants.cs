using System;
using System.Collections.Generic;

namespace eDIAN.Data
{
    /// <summary>
    /// VFS 후킹 엔진(Native) 및 관련 서비스에서 사용하는 상수 정의 클래스입니다.
    /// 멀티 CAD(AutoCAD, ZWCAD 등) 대응을 위해 호스트 및 신뢰 프로세스 명세를 관리합니다.
    /// </summary>
    public class HookConstants
    {
        // ============================================================================
        // [AutoCAD 플랫폼 정의]
        // ============================================================================
        
        /// <summary>AutoCAD 메인 실행 파일명</summary>
        public const string ACAD_HOST_PROCESS = "acad.exe";

        /// <summary>
        /// AutoCAD에서 실물 파일 접근이 허용되는 신뢰된 부속 프로세스 리스트
        /// </summary>
        public static readonly List<string> ACAD_TRUSTED_PROCESSES = new List<string>
        {
            "AcPublish.exe",     // 출력(Publish) 엔진
            "AcCoreConsole.exe", // 백그라운드 코어 콘솔
            "AcPlotters.exe",    // 플로터 엔진
            "AcSignApply.exe",   // 디지털 서명 적용 도구
            "AcTranslators.exe", // 포맷 변환기 (STEP, IGES 등)
            "AcSettingSync.exe", // 설정 동기화
            "AcWebPublish.exe",  // 웹 게시 엔진
            "AdskIdentityManager.exe", // 오토데스크 계정 관리
            "AdSSO.exe",         // 오토데스크 싱글 사인온
            "accore.dll",        // 내부 로직용
            "senddmp.exe"        // 에러 보고 도구
        };

        // ============================================================================
        // [ZWCAD 플랫폼 정의]
        // ============================================================================
        
        /// <summary>ZWCAD 메인 실행 파일명</summary>
        public const string ZWCAD_HOST_PROCESS = "ZWCAD.exe";

        /// <summary>
        /// ZWCAD에서 실물 파일 접근이 허용되는 신뢰된 부속 프로세스 리스트
        /// </summary>
        public static readonly List<string> ZWCAD_TRUSTED_PROCESSES = new List<string>
        {
            "ZwPublish.exe",     // 출력 엔진
            "ZwConsole.exe",     // 콘솔 엔진 (ZwCoreConsole)
            "ZwPlotters.exe",    // 플로터 엔진
            "ZwTranslators.exe", // 포맷 변환기
            "ZwSettingSync.exe", // 설정 동기화
            "ZWCAD.dll",         // 핵심 모듈
            "CrashReport.exe"    // 오류 보고
        };

        // ============================================================================
        // [CAD 플랫폼별 임시/플로팅 디렉터리 경로 패턴]
        // ============================================================================

        /// <summary>
        /// AutoCAD에서 사용하는 임시/플로팅 디렉터리 경로 패턴 리스트
        /// </summary>
        public static readonly List<string> ACAD_TEMP_PATTERNS = new List<string>
        {
            @"\temp\acpubl~",
            @"\temp\plot~",
            @"\temp\acpublish_",
            @"\temp\plot_"
        };

        /// <summary>
        /// ZWCAD에서 사용하는 임시/플로팅 디렉터리 경로 패턴 리스트
        /// </summary>
        public static readonly List<string> ZWCAD_TEMP_PATTERNS = new List<string>
        {
            @"\temp\zwpubl~",
            @"\temp\zwplot~",
            @"\temp\zwpublish_",
            @"\temp\zwplot_",
            // MIP temp — ZWCAD QSAVE sidecar / rename 마무리 (L3, passthrough only)
            @"\mip_data\mip\temp\zws",
            @"\mip_data\mip\temp\zwTm",
            @"\mip_data\mip\temp\zwsv",
        };

        // ============================================================================
        // [VFS 엔진 제어 상수]
        // ============================================================================

        /// <summary>
        /// [레거시] Stealth VFS 이전 고정 고스트 경로. 런타임은 Native <c>SessionManager</c> 난수 세션 + <c>GHT</c> temp만 사용.
        /// </summary>
        [Obsolete("Stealth VFS: fixed vfs_ghosts path is unused; Native uses random session dir. Do not reference.")]
        public const string VFS_GHOST_DIR_REL = @"\edian+\vfs_ghosts";

        /// <summary>JIT 실체화(Manifestation) 유지 시간 (ms) - 기본값</summary>
        public const int DEFAULT_JIT_COOLING_TIME_MS = 500;

        /// <summary>
        /// 현재 부모 프로그램 버전 문자열을 기반으로 ZWCAD 여부를 판별합니다.
        /// </summary>
        public static bool IsZWCAD()
        {
            if (string.IsNullOrEmpty(CommonConstants.PARENT_PROGRAM_VERSION))
                return false;

            return CommonConstants.PARENT_PROGRAM_VERSION.Contains("ZWCAD");
        }

        /// <summary>
        /// 현재 실행 중인 플랫폼에 맞는 직렬화된 설정 문자열을 생성합니다.
        /// 형식: [HostName]|[Trusted1];[Trusted2];...|[TempPattern1];[TempPattern2];...
        /// </summary>
        public static string GetNativeConfigString()
        {
            bool isZw = IsZWCAD();
            string host = isZw ? ZWCAD_HOST_PROCESS : ACAD_HOST_PROCESS;
            List<string> trusted = isZw ? ZWCAD_TRUSTED_PROCESSES : ACAD_TRUSTED_PROCESSES;
            List<string> tempPatterns = isZw ? ZWCAD_TEMP_PATTERNS : ACAD_TEMP_PATTERNS;
            return $"{host}|{string.Join(";", trusted)}|{string.Join(";", tempPatterns)}";
        }

        /// <summary>
        /// 특정 플랫폼을 명시하여 직렬화된 설정 문자열을 생성합니다.
        /// </summary>
        public static string GetNativeConfigString(bool isZWCAD)
        {
            string host = isZWCAD ? ZWCAD_HOST_PROCESS : ACAD_HOST_PROCESS;
            List<string> trusted = isZWCAD ? ZWCAD_TRUSTED_PROCESSES : ACAD_TRUSTED_PROCESSES;
            List<string> tempPatterns = isZWCAD ? ZWCAD_TEMP_PATTERNS : ACAD_TEMP_PATTERNS;
            return $"{host}|{string.Join(";", trusted)}|{string.Join(";", tempPatterns)}";
        }
    }
}