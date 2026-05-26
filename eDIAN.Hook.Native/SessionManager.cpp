#include "SessionManager.h"
#include "AccessGuard.h"
#include "VfsLogger.h"
#include <shlwapi.h>
#include <shlobj.h>
#include <random>
#include <atomic>

namespace PhantomVfs {

void SessionManager::Initialize(const std::wstring& mipTempPath, const std::wstring& ghostSessionDir) {
  m_mipTempPath = mipTempPath;
  m_ghostSessionDir = ghostSessionDir;
}

void SessionManager::CleanupLegacyGhosts(const std::wstring& mipTempPath, const std::wstring& currentSessionDir, const AccessGuard& guard) {
  if (mipTempPath.empty())
    return;

  std::wstring tempSearchPattern = mipTempPath + L"\\*";
  WIN32_FIND_DATAW findData;
  HANDLE hTempFind = FindFirstFileW(tempSearchPattern.c_str(), &findData);
  if (hTempFind != INVALID_HANDLE_VALUE) {
    VFS_LOG_INFO(L"[Cleanup] 좀비 세션 디렉터리 검색을 시작합니다: %s", mipTempPath.c_str());
    int zombieCount = 0;
    do {
      if (wcscmp(findData.cFileName, L".") == 0 || wcscmp(findData.cFileName, L"..") == 0)
        continue;

      if (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
        std::wstring dirPath = mipTempPath + L"\\" + findData.cFileName;

        if (_wcsicmp(dirPath.c_str(), currentSessionDir.c_str()) == 0) {
          continue;
        }

        std::wstring metadataPath = dirPath + L"\\.vfs_metadata";
        if (PathFileExistsW(metadataPath.c_str())) {
          HANDLE hMetaFile = CreateFileW(metadataPath.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
          if (hMetaFile != INVALID_HANDLE_VALUE) {
            char buffer[512] = { 0 };
            DWORD readBytes = 0;
            if (ReadFile(hMetaFile, buffer, sizeof(buffer) - 1, &readBytes, NULL)) {
              std::string content(buffer);
              size_t sigPos = content.find("Signature: EDIAN_VFS_GHOST_SANDBOX_V1");
              size_t pidPos = content.find("PID: ");
              if (sigPos != std::string::npos && pidPos != std::string::npos) {
                DWORD creatorPID = (DWORD)atoi(content.c_str() + pidPos + 5);

                bool isAlive = guard.IsProcessAlive(creatorPID);
                bool isAcad = isAlive ? guard.IsProcessAcadOrPublish(creatorPID) : false;

                if (!isAlive || !isAcad) {
                  CloseHandle(hMetaFile);
                  hMetaFile = INVALID_HANDLE_VALUE;

                  VFS_LOG_INFO(L"[Cleanup] 좀비 세션 디렉터리 감지 (PID:%d 소유): %s", creatorPID, dirPath.c_str());
                  DeleteDirectoryRecursive(dirPath);
                  zombieCount++;
                  continue;
                }
              }
            }
            if (hMetaFile != INVALID_HANDLE_VALUE) {
              CloseHandle(hMetaFile);
            }
          }
        }
      }
    } while (FindNextFileW(hTempFind, &findData));
    FindClose(hTempFind);
    if (zombieCount > 0) {
      VFS_LOG_INFO(L"[Cleanup] 총 %d개의 좀비 세션 디렉터리를 소거했습니다.", zombieCount);
    }
  }
}

void SessionManager::PassiveCleanupKeepers(const AccessGuard& guard) {
  ULONGLONG now = GetTickCount64();
  auto it = m_keeperList.begin();
  while (it != m_keeperList.end()) {
    bool isOldEnough = (now - it->createdAt > 60000);
    if (isOldEnough && !guard.IsProcessAlive(it->ownerPID)) {
      VFS_LOG_INFO(L"Keeper 자원 정리 (PID:%d): %s", it->ownerPID, it->path.c_str());
      CloseHandle(it->hKeeper);
      DeleteFileW(it->path.c_str());
      it = m_keeperList.erase(it);
    } else {
      ++it;
    }
  }
}

void SessionManager::ReleaseKeeperByPath(const std::wstring &path) {
  auto it = m_keeperList.begin();
  while (it != m_keeperList.end()) {
    if (_wcsicmp(it->path.c_str(), path.c_str()) == 0) {
      CloseHandle(it->hKeeper);
      it = m_keeperList.erase(it);
      return;
    } else {
      ++it;
    }
  }
}

void SessionManager::ReleaseAllKeepersInFolder(const std::wstring &folderPath) {
  std::wstring folder = folderPath;
  if (!folder.empty() && folder.back() != L'\\')
    folder += L'\\';

  auto it = m_keeperList.begin();
  int count = 0;
  while (it != m_keeperList.end()) {
    if (_wcsnicmp(it->path.c_str(), folder.c_str(), (DWORD)folder.length()) == 0) {
      CloseHandle(it->hKeeper);
      it = m_keeperList.erase(it);
      count++;
    } else {
      ++it;
    }
  }
  if (count > 0)
    VFS_LOG_INFO(L"폴더 내 Keeper %d개 해제: %s", count, folder.c_str());
}

void SessionManager::ReleaseAllKeepers() {
  VFS_LOG_INFO(L"[Vaporize] 모든 가상 파일 보호 핸들 강제 폐쇄를 개시합니다. (수량: %d)", (int)m_keeperList.size());
  
  for (auto &keeper : m_keeperList) {
    if (keeper.hKeeper != INVALID_HANDLE_VALUE) {
      CloseHandle(keeper.hKeeper);
      VFS_LOG_INFO(L"[Vaporize] Keeper 핸들 폐쇄 완료: %s", keeper.path.c_str());
    }
  }
  m_keeperList.clear();
}

void SessionManager::VaporizeSessionDir() {
  static std::atomic<bool> isVaporized(false);
  if (isVaporized.exchange(true)) return;

  if (m_ghostSessionDir.empty()) return;

  VFS_LOG_INFO(L"[Vaporize] 가상 세션 폴더 소거 작동 개시: %s", m_ghostSessionDir.c_str());

  if (m_ghostSessionDir.length() < 15 || m_ghostSessionDir.find(L"mip_data\\mip\\temp") == std::wstring::npos) {
    VFS_LOG_WARN(L"[Vaporize] [Guard] 안전하지 않은 경로명이 감지되어 소거를 차단합니다.");
    return;
  }

  std::wstring metaPath = m_ghostSessionDir + L"\\.vfs_metadata";
  if (GetFileAttributesW(metaPath.c_str()) == INVALID_FILE_ATTRIBUTES) {
    VFS_LOG_WARN(L"[Vaporize] [Guard] 세션 메타데이터 서명이 식별되지 않아 안전을 위해 소거를 건너뜁니다.");
    return;
  }

  ReleaseAllKeepers();
  DeleteDirectoryRecursive(m_ghostSessionDir);
  
  VFS_LOG_INFO(L"[Vaporize] 가상 세션 폴더 및 내부 임시 고스트 파일 완전 기화 완료.");
}

std::wstring SessionManager::GenerateRandomString(size_t length) {
  static const wchar_t alphabet[] = L"abcdefghijklmnopqrstuvwxyz0123456789";
  std::random_device rd;
  std::mt19937 generator(rd());
  std::uniform_int_distribution<size_t> distribution(0, (sizeof(alphabet) / sizeof(wchar_t)) - 2);

  std::wstring result;
  result.reserve(length);
  for (size_t i = 0; i < length; ++i) {
    result += alphabet[distribution(generator)];
  }
  return result;
}

void SessionManager::DeleteDirectoryRecursive(const std::wstring &path) {
  std::wstring searchPattern = path + L"\\*";
  WIN32_FIND_DATAW fd;
  HANDLE hFind = FindFirstFileW(searchPattern.c_str(), &fd);
  if (hFind != INVALID_HANDLE_VALUE) {
    do {
      if (wcscmp(fd.cFileName, L".") == 0 || wcscmp(fd.cFileName, L"..") == 0)
        continue;
      std::wstring itemPath = path + L"\\" + fd.cFileName;
      if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
        DeleteDirectoryRecursive(itemPath);
      } else {
        SetFileAttributesW(itemPath.c_str(), FILE_ATTRIBUTE_NORMAL);
        DeleteFileW(itemPath.c_str());
      }
    } while (FindNextFileW(hFind, &fd));
    FindClose(hFind);
  }
  SetFileAttributesW(path.c_str(), FILE_ATTRIBUTE_NORMAL);
  RemoveDirectoryW(path.c_str());
}

} // namespace PhantomVfs
