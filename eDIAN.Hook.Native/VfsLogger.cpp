#define SPDLOG_WCHAR_TO_UTF8_SUPPORT
#define SPDLOG_WCHAR_FILENAMES
#include "VfsLogger.h"
#include "spdlog/spdlog.h"
#include "spdlog/async.h"
#include "spdlog/sinks/basic_file_sink.h"
#include <shlwapi.h>
#include <cwctype>
#include <psapi.h>
#include <vector>
#include <stdarg.h>
#include <atomic>
#include <chrono>
#include <algorithm>

#pragma comment(lib, "Shlwapi.lib")

namespace PhantomVfs {

/**
 * @brief spdlog 구체 객체를 격리하는 Impl 구조체
 */
struct VfsLogger::Impl {
    std::shared_ptr<spdlog::logger> logger;
};

VfsLogger::VfsLogger() : m_impl(std::make_unique<Impl>()) {}

VfsLogger::~VfsLogger() {
    std::lock_guard<std::recursive_mutex> lock(m_logMutex);
    try {
        if (m_impl && m_impl->logger) {
            m_impl->logger->flush();
            spdlog::drop("vfs_logger");
        }
    } catch (...) {
        // 소멸자 예외 차단
    }
}

void VfsLogger::Initialize(LPCWSTR lpLogPath, int nLogLevel) {
    std::lock_guard<std::recursive_mutex> lock(m_logMutex);
    m_logPath = lpLogPath ? lpLogPath : L"";
    m_logLevelVal = nLogLevel;

    if (m_logPath.empty() || !PathIsDirectoryW(m_logPath.c_str())) {
        return;
    }

    try {
        // 1. 기존 다른 프로세스의 죽은 로그 파일 정리 (vfs_console_*.log*)
        std::wstring searchPattern = m_logPath + L"\\vfs_console_*.log*";
        WIN32_FIND_DATAW findData;
        HANDLE hFind = FindFirstFileW(searchPattern.c_str(), &findData);
        std::vector<std::wstring> logsToDelete;

        if (hFind != INVALID_HANDLE_VALUE) {
            do {
                std::wstring fileName = findData.cFileName;
                DWORD logPid = ExtractPIDFromPath(fileName);

                if (logPid > 0 && logPid != GetCurrentProcessId()) {
                    if (!IsProcessAlive(logPid)) {
                        logsToDelete.push_back(fileName);
                    }
                }
            } while (FindNextFileW(hFind, &findData));
            FindClose(hFind);
        }

        // 실제 삭제 진행 (공유 잠금 해제 목적)
        for (const auto &file : logsToDelete) {
            std::wstring filePath = m_logPath + L"\\" + file;
            DeleteFileW(filePath.c_str());
        }

        // 2. 현재 프로세스 전용 로그 파일 경로 결정
        DWORD pid = GetCurrentProcessId();
        std::wstring logFilePath = m_logPath + L"\\vfs_console_" + std::to_wstring(pid) + L".log";

        // 비동기 로거 스레드 풀 초기화
        static bool s_tp_initialized = false;
        if (!s_tp_initialized) {
            spdlog::init_thread_pool(8192, 1);
            s_tp_initialized = true;
        }

        auto file_sink = std::make_shared<spdlog::sinks::basic_file_sink_mt>(logFilePath);
        std::vector<spdlog::sink_ptr> sinks{file_sink};
        auto logger = std::make_shared<spdlog::async_logger>(
            "vfs_logger", sinks.begin(), sinks.end(), spdlog::thread_pool(),
            spdlog::async_overflow_policy::block);

        // 로그 레벨 매핑 (C# 0~5 -> spdlog)
        spdlog::level::level_enum targetLevel = spdlog::level::info;
        switch (nLogLevel) {
            case 0: targetLevel = spdlog::level::off; break;
            case 1: targetLevel = spdlog::level::err; break;
            case 2: targetLevel = spdlog::level::info; break;
            case 3: targetLevel = spdlog::level::debug; break;
            case 4: targetLevel = spdlog::level::warn; break;
            case 5: targetLevel = spdlog::level::critical; break;
            default: targetLevel = spdlog::level::info; break;
        }

        logger->set_level(targetLevel);
        logger->set_pattern("[%Y-%m-%d %H:%M:%S.%e] [%l] [PID:%P] %v");
        
        // 실시간성 확보 (info 레벨 이상 즉시 플러시 + 3초 주기 자동 플러시)
        logger->flush_on(spdlog::level::info);
        spdlog::flush_every(std::chrono::seconds(3));
        
        m_impl->logger = logger;
        spdlog::set_default_logger(logger);

        // spdlog 전역 에러 핸들러 등록
        spdlog::set_error_handler([](const std::string &msg) {
            wchar_t buf[256];
            swprintf_s(buf, L"[VfsLogger spdlog Error] %S\n", msg.c_str());
            OutputDebugStringW(buf);
        });

        // 3. 백업 로테이션 인덱스 검출 (기존 01~05 중 미사용 번호 찾기)
        m_nextBackupIndex = 1;
        for (int i = 1; i <= 5; ++i) {
            wchar_t backupFile[MAX_PATH];
            swprintf_s(backupFile, L"%s\\vfs_console_%d.log.%02d", m_logPath.c_str(), pid, i);
            if (GetFileAttributesW(backupFile) == INVALID_FILE_ATTRIBUTES) {
                m_nextBackupIndex = i;
                break;
            }
            if (i == 5) {
                m_nextBackupIndex = 1;
            }
        }

    } catch (const std::exception &e) {
        wchar_t buf[512];
        swprintf_s(buf, L"[VfsLogger Init Exception] %S\n", e.what());
        OutputDebugStringW(buf);
    } catch (...) {
        OutputDebugStringW(L"[VfsLogger Init Exception] Unknown Error\n");
    }
}

void VfsLogger::Log(LogLevel level, const wchar_t* format, ...) {
    if (!m_impl || !m_impl->logger) {
        return;
    }

    // spdlog 레벨 매핑
    spdlog::level::level_enum spdLevel = spdlog::level::info;
    switch (level) {
        case LogLevel::Off: return;
        case LogLevel::Error: spdLevel = spdlog::level::err; break;
        case LogLevel::Info: spdLevel = spdlog::level::info; break;
        case LogLevel::Debug: spdLevel = spdlog::level::debug; break;
        case LogLevel::Warn: spdLevel = spdlog::level::warn; break;
        case LogLevel::Critical: spdLevel = spdlog::level::critical; break;
    }

    if (!m_impl->logger->should_log(spdLevel)) {
        return;
    }

    wchar_t buffer[2048];
    va_list args;
    va_start(args, format);
    vswprintf_s(buffer, format, args);
    va_end(args);

    std::lock_guard<std::recursive_mutex> lock(m_logMutex);
    if (!m_impl || !m_impl->logger) {
        return;
    }

    // 10회 출력 주기마다 로그 파일 크기 체크 후 롤링 로테이션 검사
    static std::atomic<int> s_logCallCount{0};
    if (++s_logCallCount >= 10) {
        s_logCallCount = 0;
        PerformRotation();
    }

    if (m_impl && m_impl->logger) {
        m_impl->logger->log(spdLevel, L"{}", buffer);
    }
}

void VfsLogger::Log(const wchar_t* format, ...) {
    if (!m_impl || !m_impl->logger) {
        return;
    }

    if (!m_impl->logger->should_log(spdlog::level::info)) {
        return;
    }

    wchar_t buffer[2048];
    va_list args;
    va_start(args, format);
    vswprintf_s(buffer, format, args);
    va_end(args);

    std::lock_guard<std::recursive_mutex> lock(m_logMutex);
    if (!m_impl || !m_impl->logger) {
        return;
    }

    static std::atomic<int> s_logCallCount{0};
    if (++s_logCallCount >= 10) {
        s_logCallCount = 0;
        PerformRotation();
    }

    if (m_impl && m_impl->logger) {
        m_impl->logger->log(spdlog::level::info, L"{}", buffer);
    }
}

void VfsLogger::PerformRotation() {
    if (!m_impl || !m_impl->logger) {
        return;
    }

    DWORD pid = GetCurrentProcessId();
    wchar_t currentLog[MAX_PATH];
    swprintf_s(currentLog, L"%s\\vfs_console_%d.log", m_logPath.c_str(), pid);

    WIN32_FILE_ATTRIBUTE_DATA attr;
    if (!GetFileAttributesExW(currentLog, GetFileExInfoStandard, &attr)) {
        return;
    }

    LARGE_INTEGER fileSize;
    fileSize.LowPart = attr.nFileSizeLow;
    fileSize.HighPart = attr.nFileSizeHigh;

    // 백업 용량 미만이면 리턴
    if (fileSize.QuadPart < MAX_LOG_FILE_SIZE) {
        return;
    }

    try {
        wchar_t backupLog[MAX_PATH];
        swprintf_s(backupLog, L"%s\\vfs_console_%d.log.%02d", m_logPath.c_str(), pid, m_nextBackupIndex);

        // 기존 로거 일시 중단 및 핸들 해제
        m_impl->logger->flush();
        spdlog::drop("vfs_logger");

        // 파일 복사 및 원본 파일 비우기 (Copy & Truncate)
        if (CopyFileW(currentLog, backupLog, FALSE)) {
            HANDLE hFile = CreateFileW(currentLog, GENERIC_WRITE,
                                       FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
                                       TRUNCATE_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
            if (hFile != INVALID_HANDLE_VALUE) {
                CloseHandle(hFile);
            }
            m_nextBackupIndex = (m_nextBackupIndex % 5) + 1;
        }

        // 로거 재기동
        auto sink = std::make_shared<spdlog::sinks::basic_file_sink_mt>(currentLog, false);
        auto logger = std::make_shared<spdlog::async_logger>(
            "vfs_logger", sink, spdlog::thread_pool(),
            spdlog::async_overflow_policy::block);

        // 이전 설정 적용
        spdlog::level::level_enum currentLevel = spdlog::level::info;
        switch (m_logLevelVal) {
            case 0: currentLevel = spdlog::level::off; break;
            case 1: currentLevel = spdlog::level::err; break;
            case 2: currentLevel = spdlog::level::info; break;
            case 3: currentLevel = spdlog::level::debug; break;
            case 4: currentLevel = spdlog::level::warn; break;
            case 5: currentLevel = spdlog::level::critical; break;
        }

        logger->set_level(currentLevel);
        logger->set_pattern("[%Y-%m-%d %H:%M:%S.%e] [%l] [PID:%P] %v");
        logger->flush_on(spdlog::level::info);

        m_impl->logger = logger;
        spdlog::set_default_logger(m_impl->logger);

        m_impl->logger->info(L"[Rotation] 로그 용량 초과(5MB)로 인한 백업 완료: {}", backupLog);

    } catch (...) {
        // 로테이션 예외로 인한 전체 동작 방해 차단
    }
}

DWORD VfsLogger::ExtractPIDFromPath(const std::wstring& path) {
    if (path.empty()) return 0;

    PCWSTR pos = StrStrIW(path.c_str(), L"vfs_console_");
    if (!pos) {
        // 기본 헬퍼 패턴들도 체크
        static const std::vector<std::wstring> keys = { L"acpublish_", L"plot_", L"zwpublish_", L"zwplot_" };
        for (const auto &key : keys) {
            pos = StrStrIW(path.c_str(), key.c_str());
            if (pos) break;
        }
    }

    if (!pos) return 0;

    // 숫자 시작 부분 검색
    while (*pos && !iswdigit(*pos)) {
        pos++;
    }

    std::wstring pidStr;
    while (*pos && iswdigit(*pos)) {
        pidStr += *pos;
        pos++;
    }

    return pidStr.empty() ? 0 : (DWORD)_wtoi(pidStr.c_str());
}

bool VfsLogger::IsProcessAlive(DWORD pid) {
    if (pid == 0) return false;
    HANDLE hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!hProcess) return false;
    
    DWORD exitCode = 0;
    BOOL res = GetExitCodeProcess(hProcess, &exitCode);
    CloseHandle(hProcess);
    return res && (exitCode == STILL_ACTIVE);
}

} // namespace PhantomVfs
