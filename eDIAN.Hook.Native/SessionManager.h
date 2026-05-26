#pragma once
#include "VfsTypes.h"
#include <string>
#include <vector>
#include <windows.h>

namespace PhantomVfs {

class AccessGuard; // Forward declaration

class SessionManager {
public:
  SessionManager() = default;
  ~SessionManager() = default;

  void Initialize(const std::wstring& mipTempPath, const std::wstring& ghostSessionDir);
  void CleanupLegacyGhosts(const std::wstring& mipTempPath, const std::wstring& currentSessionDir, const AccessGuard& guard);
  void PassiveCleanupKeepers(const AccessGuard& guard);
  
  void ReleaseKeeperByPath(const std::wstring& path);
  void ReleaseAllKeepersInFolder(const std::wstring& folderPath);
  void ReleaseAllKeepers();
  void VaporizeSessionDir();

  const std::wstring& GetGhostSessionDir() const { return m_ghostSessionDir; }
  const std::wstring& GetMipTempPath() const { return m_mipTempPath; }
  std::vector<KeeperInfo>& GetKeeperList() { return m_keeperList; }

  std::wstring GenerateRandomString(size_t length);
  void DeleteDirectoryRecursive(const std::wstring& path);

private:
  std::wstring m_ghostSessionDir;
  std::wstring m_mipTempPath;
  std::vector<KeeperInfo> m_keeperList;
};

} // namespace PhantomVfs
