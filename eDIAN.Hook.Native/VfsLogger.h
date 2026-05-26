#pragma once
#include <string>
#include <memory>
#include <mutex>
#include <windows.h>

namespace PhantomVfs {

/**
 * @brief eDIAN VFS 로그 레벨 정의
 */
enum class LogLevel {
    Off = 0,
    Error = 1,
    Info = 2,
    Debug = 3,
    Warn = 4,
    Critical = 5
};

/**
 * @brief VfsLogger - 독립된 고성능 비동기 로그 모듈 클래스
 * spdlog 의존성을 구현부(.cpp) 내부로 완전히 격리하여 컴파일 종속성을 제거합니다.
 */
class VfsLogger {
public:
    /** @brief 싱글톤 인스턴스를 반환합니다. */
    static VfsLogger& Instance() {
        static VfsLogger instance;
        return instance;
    }

    /**
     * @brief 로그 시스템을 초기화합니다.
     * @param lpLogPath 로그 파일이 생성될 디렉터리 경로
     * @param nLogLevel 로그 레벨 (0: Off, 1: Error, 2: Info, 3: Debug, 4: Warn, 5: Critical)
     */
    void Initialize(LPCWSTR lpLogPath, int nLogLevel);

    /** @brief 특정 레벨로 유니코드 로그를 남깁니다. (가변 인자 지원) */
    void Log(LogLevel level, const wchar_t* format, ...);

    /** @brief 기본 Info 레벨로 유니코드 로그를 남깁니다. (가변 인자 지원) */
    void Log(const wchar_t* format, ...);

    /** @brief 현재 설정된 로그 파일 경로를 반환합니다. */
    const std::wstring& GetLogPath() const { return m_logPath; }

private:
    VfsLogger();
    ~VfsLogger();

    // 복사 및 복사 대입 방지
    VfsLogger(const VfsLogger&) = delete;
    VfsLogger& operator=(const VfsLogger&) = delete;

    /** @brief 로그 파일 용량 초과 시 롤링 백업을 수행합니다. */
    void PerformRotation();

    /** @brief 파일명 또는 경로에서 PID를 안전하게 추출합니다. */
    DWORD ExtractPIDFromPath(const std::wstring& path);

    /** @brief 특정 프로세스 ID가 살아있는지 검사합니다. */
    bool IsProcessAlive(DWORD pid);

private:
    std::wstring m_logPath;
    int m_logLevelVal = 2; // 기본 Info
    int m_nextBackupIndex = 1;

    // spdlog 격리를 위한 Pimpl 패턴 정의
    struct Impl;
    std::unique_ptr<Impl> m_impl;

    std::recursive_mutex m_logMutex; // 비동기 작업 및 백업 동기화용 뮤텍스

    // 로그 백업 기준 용량 (5MB)
    static constexpr long long MAX_LOG_FILE_SIZE = 5 * 1024 * 1024;
};

} // namespace PhantomVfs

// --- 편리하고 직관적인 로깅을 위한 전역 매크로 정의 ---
#define VFS_LOG_DEBUG(fmt, ...) PhantomVfs::VfsLogger::Instance().Log(PhantomVfs::LogLevel::Debug, fmt, __VA_ARGS__)
#define VFS_LOG_INFO(fmt, ...)  PhantomVfs::VfsLogger::Instance().Log(PhantomVfs::LogLevel::Info, fmt, __VA_ARGS__)
#define VFS_LOG_WARN(fmt, ...)  PhantomVfs::VfsLogger::Instance().Log(PhantomVfs::LogLevel::Warn, fmt, __VA_ARGS__)
#define VFS_LOG_ERR(fmt, ...)   PhantomVfs::VfsLogger::Instance().Log(PhantomVfs::LogLevel::Error, fmt, __VA_ARGS__)
#define VFS_LOG_CRIT(fmt, ...)  PhantomVfs::VfsLogger::Instance().Log(PhantomVfs::LogLevel::Critical, fmt, __VA_ARGS__)
