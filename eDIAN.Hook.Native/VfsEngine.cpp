#include "VfsEngine.h"
#include <algorithm>
#include <cwctype>
#include <psapi.h>
#include <random>
#include <shlobj.h>
#include <shlwapi.h>
#include <sstream>
#include <stdarg.h>

namespace PhantomVfs {

VfsEngine::VfsEngine()
    : m_openCallback(NULL),
      m_closeCallback(NULL),
      m_initialized(false) {
  // 생성자에서는 컴포넌트를 초기화하지 않고 Initialize에서 동적으로 할당함.
}

void VfsEngine::Initialize(LPCWSTR lpTempPath, LPCWSTR lpLogPath, int nLogLevel,
                           LPCWSTR lpConfigString, ::FnVfsOpenCallback openCb,
                           ::FnVfsCloseCallback closeCb) {
  std::lock_guard<std::recursive_mutex> lock(m_engineMutex);
  if (m_initialized)
    return;

  m_logPath = lpLogPath ? lpLogPath : L"";
  m_openCallback = openCb;
  m_closeCallback = closeCb;

  // 신규 독립 로그 모듈 초기화
  VfsLogger::Instance().Initialize(m_logPath.c_str(), nLogLevel);

  // 3대 핵심 컴포넌트 동적 할당
  m_storage = std::make_unique<VfsStorage>();
  m_guard = std::make_unique<AccessGuard>();
  m_session = std::make_unique<SessionManager>();

  // 현재 프로세스 정보 캐싱 및 로그 출력
  std::wstring currentProcessName;
  wchar_t szExePath[MAX_PATH];
  if (GetModuleFileNameW(NULL, szExePath, MAX_PATH)) {
    currentProcessName = PathFindFileNameW(szExePath);
  }

  VFS_LOG_CRIT(L"==================================================");
  VFS_LOG_CRIT(L"VFS Engine Restructured (3rd Phase Core Isolation)");
  VFS_LOG_CRIT(L"Process: %s", currentProcessName.c_str());
  VFS_LOG_CRIT(L"PID: %d", GetCurrentProcessId());
  VFS_LOG_CRIT(L"Log Level: %d", nLogLevel);
  VFS_LOG_CRIT(L"==================================================");

  // 설정 파싱 및 AccessGuard 초기화
  std::wstring hostProcess = L"acad.exe";
  std::vector<std::wstring> trustedProcesses;
  std::vector<std::wstring> tempPatterns;

  if (lpConfigString && wcslen(lpConfigString) > 0) {
    std::wstring config(lpConfigString);
    std::vector<std::wstring> sections;
    std::wstringstream ss(config);
    std::wstring section;
    while (std::getline(ss, section, L'|')) {
      sections.push_back(section);
    }

    if (sections.size() > 0) {
      hostProcess = sections[0];
    }
    if (sections.size() > 1) {
      std::wstringstream ssTrusted(sections[1]);
      std::wstring item;
      while (std::getline(ssTrusted, item, L';')) {
        if (!item.empty())
          trustedProcesses.push_back(item);
      }
    }
    if (sections.size() > 2) {
      std::wstringstream ssPatterns(sections[2]);
      std::wstring pattern;
      while (std::getline(ssPatterns, pattern, L';')) {
        if (!pattern.empty()) {
          tempPatterns.push_back(pattern);
        }
      }
    }
  }

  m_guard->Initialize(currentProcessName, hostProcess, trustedProcesses, tempPatterns);

  // 동적 세션 경로 결정
  std::wstring mipTempPath = lpTempPath ? lpTempPath : L"";
  std::wstring ghostSessionDir;

  wchar_t envSessionDir[MAX_PATH] = { 0 };
  DWORD envLen = GetEnvironmentVariableW(ENV_VFS_SESSION_DIR, envSessionDir, MAX_PATH);
  if (envLen > 0 && envLen < MAX_PATH) {
    ghostSessionDir = envSessionDir;
    VFS_LOG_INFO(L"[VfsEngine] Inherited Dynamic Session Sandbox from Environment: %s", ghostSessionDir.c_str());
  } else {
    if (mipTempPath.empty()) {
      wchar_t appData[MAX_PATH];
      GetEnvironmentVariableW(ENV_LOCAL_APP_DATA, appData, MAX_PATH);
      mipTempPath = std::wstring(appData) + FALLBACK_MIP_TEMP_SUFFIX;
    }
    std::wstring randomDirName = m_session->GenerateRandomString(10);
    ghostSessionDir = mipTempPath + L"\\" + randomDirName;
    SetEnvironmentVariableW(ENV_VFS_SESSION_DIR, ghostSessionDir.c_str());
    VFS_LOG_INFO(L"[VfsEngine] Generated New Dynamic Session Sandbox: %s", ghostSessionDir.c_str());
  }

  m_session->Initialize(mipTempPath, ghostSessionDir);

  if (!PathFileExistsW(ghostSessionDir.c_str())) {
    SHCreateDirectoryExW(NULL, ghostSessionDir.c_str(), NULL);
    SetFileAttributesW(ghostSessionDir.c_str(),
                       FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM);
  }

  // .vfs_metadata 생성
  if (envLen == 0) {
    std::wstring metadataPath = ghostSessionDir + METADATA_FILE_NAME;
    HANDLE hMeta = CreateFileW(metadataPath.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS,
                               FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM, NULL);
    if (hMeta != INVALID_HANDLE_VALUE) {
      char buffer[256];
      sprintf_s(buffer, "Signature: EDIAN_VFS_GHOST_SANDBOX_V1\nPID: %d\n", GetCurrentProcessId());
      DWORD written = 0;
      WriteFile(hMeta, buffer, (DWORD)strlen(buffer), &written, NULL);
      CloseHandle(hMeta);
    }
  }

  // 레거시 좀비 소거
  m_session->CleanupLegacyGhosts(mipTempPath, ghostSessionDir, *m_guard);

  m_initialized = true;
}

bool VfsEngine::IsTrustedProcess() {
  if (!m_initialized) return false;
  return m_guard->IsTrustedProcess();
}

bool VfsEngine::IsExternalProcess() {
  if (!m_initialized) return false;
  return m_guard->IsExternalProcess();
}

bool VfsEngine::CheckAccessPermission(LPCWSTR lpFileName) {
  if (!m_initialized || !lpFileName || !lpFileName[0]) return true;
  if (m_guard->IsTrustedProcess()) return true;

  if (IsProtectedPath(lpFileName)) {
    VFS_LOG_WARN(L"[Security Guard] ACCESS DENIED (External Process) Path: %s", lpFileName);
    return false;
  }
  return true;
}

bool VfsEngine::IsProtectedPath(LPCWSTR lpFileName) {
  if (!m_initialized || !lpFileName || !lpFileName[0]) return false;

  bool cachedVal = false;
  if (m_storage->LookupProtectedCache(lpFileName, cachedVal)) {
    return cachedVal;
  }

  std::wstring normalized = m_guard->NormalizePath(lpFileName);
  bool isProtected = m_guard->IsProtectedPathInternal(normalized, m_session->GetGhostSessionDir());

  m_storage->InsertProtectedCache(lpFileName, isProtected);
  return isProtected;
}

bool VfsEngine::IsManifestingPath(LPCWSTR lpFileName) {
  if (!m_initialized) return false;
  return m_guard->IsManifestingPath(lpFileName);
}

std::wstring VfsEngine::NormalizePath(LPCWSTR lpFileName) {
  if (!m_initialized) return lpFileName ? lpFileName : L"";
  return m_guard->NormalizePath(lpFileName);
}

bool VfsEngine::IsTarget(LPCWSTR lpFileName) {
  if (!m_initialized || !lpFileName || !lpFileName[0]) return false;

  bool cachedVal = false;
  if (m_storage->LookupTargetCache(lpFileName, cachedVal)) {
    return cachedVal;
  }

  std::wstring normalized = m_guard->NormalizePath(lpFileName);
  bool isTarget = m_guard->IsTargetInternal(normalized, m_session->GetGhostSessionDir(), lpFileName);

  m_storage->InsertTargetCache(lpFileName, isTarget);
  return isTarget;
}

DWORD VfsEngine::ExtractPIDFromPath(const std::wstring &path) {
  if (!m_initialized) return 0;
  return m_guard->ExtractPIDFromPath(path);
}

bool VfsEngine::IsProcessAlive(DWORD pid) {
  if (!m_initialized) return false;
  return m_guard->IsProcessAlive(pid);
}

bool VfsEngine::IsProcessAcadOrPublish(DWORD pid) {
  if (!m_initialized) return false;
  return m_guard->IsProcessAcadOrPublish(pid);
}

std::wstring VfsEngine::GetUniqueGhostPath(const std::wstring &originalPath) {
  if (!m_initialized) return L"";
  std::wstring sessionDir = m_session->GetGhostSessionDir();
  if (sessionDir.empty()) {
    return L"";
  }

  if (!PathFileExistsW(sessionDir.c_str())) {
    SHCreateDirectoryExW(NULL, sessionDir.c_str(), NULL);
    SetFileAttributesW(sessionDir.c_str(),
                       FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM);
  }
  wchar_t ghostPath[MAX_PATH];
  GetTempFileNameW(sessionDir.c_str(), L"GHT", 0, ghostPath);
  return std::wstring(ghostPath);
}

void VfsEngine::CleanupLegacyGhosts() {
  if (!m_initialized) return;
  std::lock_guard<std::recursive_mutex> lock(m_engineMutex);
  m_session->CleanupLegacyGhosts(m_session->GetMipTempPath(), m_session->GetGhostSessionDir(), *m_guard);
}

void VfsEngine::PassiveCleanupKeepers() {
  if (!m_initialized) return;
  std::lock_guard<std::recursive_mutex> lock(m_engineMutex);
  m_session->PassiveCleanupKeepers(*m_guard);
}

void VfsEngine::ReleaseKeeperByPath(const std::wstring &path) {
  if (!m_initialized) return;
  std::lock_guard<std::recursive_mutex> lock(m_engineMutex);
  m_session->ReleaseKeeperByPath(path);
}

void VfsEngine::ReleaseAllKeepersInFolder(const std::wstring &folderPath) {
  if (!m_initialized) return;
  std::lock_guard<std::recursive_mutex> lock(m_engineMutex);
  m_session->ReleaseAllKeepersInFolder(folderPath);
}

void VfsEngine::ReleaseAllKeepers() {
  if (!m_initialized) return;
  std::lock_guard<std::recursive_mutex> lock(m_engineMutex);
  m_session->ReleaseAllKeepers();
}

void VfsEngine::VaporizeSessionDir() {
  if (!m_initialized) return;
  std::lock_guard<std::recursive_mutex> lock(m_engineMutex);
  m_session->VaporizeSessionDir();
}

IVfsEngine& GetVfsEngine() {
  return VfsEngine::Instance();
}

} // namespace PhantomVfs
