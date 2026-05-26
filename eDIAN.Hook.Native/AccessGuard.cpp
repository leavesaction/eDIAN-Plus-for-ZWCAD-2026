#include "AccessGuard.h"
#include "VfsLogger.h"
#include <shlwapi.h>
#include <psapi.h>
#include <algorithm>
#include <cwctype>

namespace PhantomVfs {

void AccessGuard::Initialize(const std::wstring& currentProcessName,
                             const std::wstring& host,
                             const std::vector<std::wstring>& trusted,
                             const std::vector<std::wstring>& patterns) {
  m_currentProcessName = currentProcessName;
  m_hostProcess = host;
  m_trustedProcesses = trusted;
  m_tempPatterns = patterns;

  m_tempPatternKeys.clear();
  for (const auto &pattern : m_tempPatterns) {
    std::wstring key = pattern;
    size_t lastSlash = key.find_last_of(L"\\/");
    if (lastSlash != std::wstring::npos) {
      key = key.substr(lastSlash + 1);
    }
    std::transform(key.begin(), key.end(), key.begin(), ::towlower);
    if (!key.empty()) {
      m_tempPatternKeys.push_back(key);
    }
  }
}

bool AccessGuard::IsTrustedProcess() const {
  if (m_currentProcessName.empty())
    return false;

  if (_wcsicmp(m_currentProcessName.c_str(), m_hostProcess.c_str()) == 0) {
    return true;
  }

  for (const auto &trusted : m_trustedProcesses) {
    if (_wcsicmp(m_currentProcessName.c_str(), trusted.c_str()) == 0) {
      return true;
    }
  }
  return false;
}

bool AccessGuard::IsExternalProcess() const {
  if (m_currentProcessName.empty())
    return false;
  if (_wcsicmp(m_currentProcessName.c_str(), m_hostProcess.c_str()) != 0 &&
      IsTrustedProcess()) {
    return true;
  }
  return false;
}

bool AccessGuard::CheckAccessPermission(LPCWSTR lpFileName, bool isProtected) const {
  if (!lpFileName || !lpFileName[0])
    return true;
  if (IsTrustedProcess())
    return true;
  if (isProtected) {
    VFS_LOG_WARN(L"[Security Guard] ACCESS DENIED (External Process) Path: %s",
        lpFileName);
    return false;
  }
  return true;
}

bool AccessGuard::IsProtectedPathInternal(const std::wstring& normalizedPath, const std::wstring& ghostSessionDir) const {
  LPCWSTR target = normalizedPath.c_str();
  if (!ghostSessionDir.empty() && StrStrIW(target, ghostSessionDir.c_str()) != nullptr) {
    return true;
  } else if (StrStrIW(target, RELATIVE_MIP_TEMP_DIR) != nullptr) {
    if (StrStrIW(target, L".dwg") != nullptr ||
        StrStrIW(target, L".tmp") != nullptr ||
        StrStrIW(target, L".ac$") != nullptr ||
        StrStrIW(target, L".bak") != nullptr ||
        StrStrIW(target, L".dwl") != nullptr ||
        StrStrIW(target, L".dwl2") != nullptr ||
        StrStrIW(target, L".sv$") != nullptr ||
        StrStrIW(target, L".zw$") != nullptr ||
        StrStrIW(target, L".zs$") != nullptr) {
      return true;
    }
  }
  return false;
}

bool AccessGuard::IsTargetInternal(const std::wstring& normalizedPath, const std::wstring& ghostSessionDir, LPCWSTR lpOriginalFileName) const {
  LPCWSTR rawExt = PathFindExtensionW(lpOriginalFileName);
  bool needsDeepAnalysis = (wcschr(lpOriginalFileName, L'~') != nullptr) ||
                           (wcsstr(lpOriginalFileName, L"vfs") != nullptr);

  bool isTarget = false;
  bool skipRest = false;

  if (!needsDeepAnalysis) {
    if (!rawExt || !rawExt[0]) {
      isTarget = false;
      skipRest = true;
    } else if (_wcsicmp(rawExt, L".dwg") != 0 && _wcsicmp(rawExt, L".dwl") != 0 &&
               _wcsicmp(rawExt, L".dwl2") != 0 && _wcsicmp(rawExt, L".tmp") != 0 &&
               _wcsicmp(rawExt, L".bak") != 0 && _wcsicmp(rawExt, L".$") != 0 &&
               _wcsicmp(rawExt, L".ac$") != 0 && _wcsicmp(rawExt, L".sv$") != 0 &&
               _wcsicmp(rawExt, L".zw$") != 0 && _wcsicmp(rawExt, L".zs$") != 0) {
      isTarget = false;
      skipRest = true;
    }
  }

  if (!skipRest) {
    if (normalizedPath.find(RELATIVE_MIP_TEMP_DIR) != std::wstring::npos ||
        (!ghostSessionDir.empty() && normalizedPath.find(ghostSessionDir) != std::wstring::npos)) {
      isTarget = true;
    } else {
      LPCWSTR ext = PathFindExtensionW(normalizedPath.c_str());
      if (_wcsicmp(ext, L".$") == 0 || _wcsicmp(ext, L".ac$") == 0 ||
          _wcsicmp(ext, L".sv$") == 0 || _wcsicmp(ext, L".zw$") == 0 ||
          _wcsicmp(ext, L".zs$") == 0) {
        isTarget = true;
      }
    }
  }

  return isTarget;
}

bool AccessGuard::IsManifestingPath(LPCWSTR lpFileName) const {
  if (!lpFileName || !lpFileName[0])
    return false;

  // [Fast-Path] 정규화 작업 없이 원본 파일명에서 임시 패턴 매칭을 먼저 체크
  for (const auto &pattern : m_tempPatterns) {
    if (StrStrIW(lpFileName, pattern.c_str()) != nullptr) {
      return true;
    }
  }

  // [Slow-Path] 매칭되지 않은 경우에만 경로를 정규화하여 최종 대조
  std::wstring path = NormalizePath(lpFileName);
  for (const auto &pattern : m_tempPatterns) {
    if (StrStrIW(path.c_str(), pattern.c_str()) != nullptr) {
      return true;
    }
  }

  return false;
}

std::wstring AccessGuard::NormalizePath(LPCWSTR lpFileName) const {
  if (!lpFileName || !lpFileName[0])
    return L"";

  std::wstring path(lpFileName);
  if (path.compare(0, 4, L"\\\\?\\") == 0 ||
      path.compare(0, 4, L"\\\\.\\") == 0) {
    path.erase(0, 4);
  }
  if (path.find(L'~') != std::wstring::npos) {
    wchar_t longPath[MAX_PATH];
    if (GetLongPathNameW(path.c_str(), longPath, MAX_PATH) > 0) {
      path = longPath;
    }
  }
  std::transform(path.begin(), path.end(), path.begin(), ::towlower);
  return path;
}

DWORD AccessGuard::ExtractPIDFromPath(const std::wstring &path) const {
  if (path.empty())
    return 0;

  PCWSTR pos = nullptr;
  
  for (const auto &key : m_tempPatternKeys) {
    pos = StrStrIW(path.c_str(), key.c_str());
    if (pos) {
      break;
    }
  }

  if (!pos) {
    pos = StrStrIW(path.c_str(), L"vfs_console_");
  }

  if (!pos) {
    static const std::vector<std::wstring> fallbackKeys = {
      L"acpublish_", L"plot_", L"zwpublish_", L"zwplot_"
    };
    for (const auto &fallbackKey : fallbackKeys) {
      pos = StrStrIW(path.c_str(), fallbackKey.c_str());
      if (pos) {
        break;
      }
    }
  }

  if (!pos)
    return 0;

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

bool AccessGuard::IsProcessAlive(DWORD pid) const {
  if (pid == 0)
    return false;
  HANDLE hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
  if (!hProcess)
    return false;
  DWORD exitCode = 0;
  BOOL res = GetExitCodeProcess(hProcess, &exitCode);
  CloseHandle(hProcess);
  return res && (exitCode == STILL_ACTIVE);
}

bool AccessGuard::IsProcessAcadOrPublish(DWORD pid) const {
  if (pid == 0)
    return false;
  HANDLE hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
  if (!hProcess)
    return false;
  wchar_t processName[MAX_PATH] = { 0 };
  DWORD size = MAX_PATH;
  bool isAcad = false;
  if (QueryFullProcessImageNameW(hProcess, 0, processName, &size)) {
    std::wstring wsPath(processName);
    size_t lastSlash = wsPath.find_last_of(L"\\/");
    std::wstring wsName = (lastSlash == std::wstring::npos) ? wsPath : wsPath.substr(lastSlash + 1);

    if (!m_hostProcess.empty() && _wcsicmp(wsName.c_str(), m_hostProcess.c_str()) == 0) {
      isAcad = true;
    } else {
      for (const auto &trusted : m_trustedProcesses) {
        if (!trusted.empty() && _wcsicmp(wsName.c_str(), trusted.c_str()) == 0) {
          isAcad = true;
          break;
        }
      }
    }

    if (!isAcad && m_hostProcess.empty()) {
      if (StrStrIW(wsName.c_str(), L"acad.exe") != nullptr ||
          StrStrIW(wsName.c_str(), L"acpublish.exe") != nullptr ||
          StrStrIW(wsName.c_str(), L"zwcad.exe") != nullptr ||
          StrStrIW(wsName.c_str(), L"zwpublish.exe") != nullptr) {
        isAcad = true;
      }
    }
  }
  CloseHandle(hProcess);
  return isAcad;
}

} // namespace PhantomVfs
