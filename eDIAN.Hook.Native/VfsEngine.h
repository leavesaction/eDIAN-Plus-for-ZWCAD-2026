#pragma once
#include "IVfsEngine.h"
#include "VfsTypes.h"
#include "VfsStorage.h"
#include "AccessGuard.h"
#include "SessionManager.h"
#include <memory>
#include <mutex>
#include <string>
#include <vector>
#include <atomic>

#include "VfsLogger.h"

namespace PhantomVfs {

class VfsEngine : public IVfsEngine {
public:
  static VfsEngine &Instance() {
    static VfsEngine instance;
    return instance;
  }

  // --- 핵심 로직 (Public) ---
  void Initialize(LPCWSTR lpTempPath, LPCWSTR lpLogPath, int nLogLevel,
                  LPCWSTR lpConfigString, ::FnVfsOpenCallback openCb,
                  ::FnVfsCloseCallback closeCb) override;

  bool IsTarget(LPCWSTR lpFileName) override;
  bool IsProtectedPath(LPCWSTR lpFileName) override;
  bool IsManifestingPath(LPCWSTR lpFileName) override;
  bool IsExternalProcess() override;
  bool IsTrustedProcess() override;
  bool CheckAccessPermission(LPCWSTR lpFileName) override;

  // --- 리소스 관리 ---
  void PassiveCleanupKeepers() override;
  void CleanupLegacyGhosts() override;
  void ReleaseKeeperByPath(const std::wstring &path) override;
  void ReleaseAllKeepersInFolder(const std::wstring &folderPath) override;
  void ReleaseAllKeepers() override;
  void VaporizeSessionDir() override;

  // --- 경로 유틸리티 ---
  std::wstring NormalizePath(LPCWSTR lpFileName) override;
  std::wstring GetUniqueGhostPath(const std::wstring &originalPath) override;
  DWORD ExtractPIDFromPath(const std::wstring &path) override;
  bool IsProcessAlive(DWORD pid) override;
  bool IsProcessAcadOrPublish(DWORD pid) override;

  // --- 후킹 레이어용 접근자 ---
  std::recursive_mutex &GetRegistryMutex() override { return m_engineMutex; }
  std::map<std::wstring, std::shared_ptr<VirtualFile>> &GetStorage() override {
    return m_storage->GetStorage();
  }
  std::map<HANDLE, std::shared_ptr<VirtualHandle>> &GetHandleMap() override {
    return m_storage->GetHandleMap();
  }
  std::condition_variable_any &GetLoadingCV() override { return m_storage->GetLoadingCV(); }
  std::unordered_set<std::wstring> &GetLoadingPaths() override { return m_storage->GetLoadingPaths(); }
  std::vector<KeeperInfo> &GetKeeperList() override { return m_session->GetKeeperList(); }

  const std::wstring &GetMipTempPath() const override { return m_session->GetMipTempPath(); }
  const std::wstring &GetLogPath() const override { return m_logPath; }
  const std::wstring &GetGhostSessionDir() const override { return m_session->GetGhostSessionDir(); }
  const std::wstring &GetCurrentProcessName() const override { return m_guard->GetCurrentProcessName(); }

private:
  VfsEngine();
  ~VfsEngine() = default;

  VfsEngine(const VfsEngine &) = delete;
  VfsEngine &operator=(const VfsEngine &) = delete;

  // 내부 상태 데이터
  std::wstring m_logPath;
  ::FnVfsOpenCallback m_openCallback;
  ::FnVfsCloseCallback m_closeCallback;

  // 코어 컴포넌트 격리
  std::unique_ptr<VfsStorage> m_storage;
  std::unique_ptr<SessionManager> m_session;
  std::unique_ptr<AccessGuard> m_guard;

  // 단일 통합 재귀 락
  std::recursive_mutex m_engineMutex;
  bool m_initialized = false;

  // 환경 변수 키
  static constexpr const wchar_t* ENV_VFS_SESSION_DIR = L"EDIAN_VFS_SESSION_DIR";
  static constexpr const wchar_t* ENV_LOCAL_APP_DATA = L"LOCALAPPDATA";
  static constexpr const wchar_t* FALLBACK_MIP_TEMP_SUFFIX = L"\\eDIAN+\\mip_data\\mip\\temp";
  static constexpr const wchar_t* METADATA_FILE_NAME = L"\\.vfs_metadata";
};

} // namespace PhantomVfs
