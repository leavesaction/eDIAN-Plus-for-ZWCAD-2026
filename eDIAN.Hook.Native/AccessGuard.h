#pragma once
#include <string>
#include <vector>
#include <windows.h>

namespace PhantomVfs {

class AccessGuard {
public:
  AccessGuard() = default;
  ~AccessGuard() = default;

  void Initialize(const std::wstring& currentProcessName,
                  const std::wstring& host,
                  const std::vector<std::wstring>& trusted,
                  const std::vector<std::wstring>& patterns);

  bool IsTrustedProcess() const;
  bool IsExternalProcess() const;
  bool CheckAccessPermission(LPCWSTR lpFileName, bool isProtected) const;
  bool IsProtectedPathInternal(const std::wstring& normalizedPath, const std::wstring& ghostSessionDir) const;
  bool IsTargetInternal(const std::wstring& normalizedPath, const std::wstring& ghostSessionDir, LPCWSTR lpOriginalFileName) const;
  bool IsManifestingPath(LPCWSTR lpFileName) const;

  std::wstring NormalizePath(LPCWSTR lpFileName) const;
  DWORD ExtractPIDFromPath(const std::wstring& path) const;
  bool IsProcessAlive(DWORD pid) const;
  bool IsProcessAcadOrPublish(DWORD pid) const;

  const std::wstring& GetCurrentProcessName() const { return m_currentProcessName; }
  const std::wstring& GetHostProcess() const { return m_hostProcess; }
  const std::vector<std::wstring>& GetTrustedProcesses() const { return m_trustedProcesses; }

private:
  std::wstring m_hostProcess;
  std::vector<std::wstring> m_trustedProcesses;
  std::vector<std::wstring> m_tempPatterns;
  std::vector<std::wstring> m_tempPatternKeys;
  std::wstring m_currentProcessName;

  static constexpr const wchar_t* RELATIVE_MIP_TEMP_DIR = L"mip_data\\mip\\temp";
};

} // namespace PhantomVfs
