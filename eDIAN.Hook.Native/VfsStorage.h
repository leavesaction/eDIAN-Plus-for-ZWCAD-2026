#pragma once
#include "VfsTypes.h"
#include <map>
#include <unordered_set>
#include <shared_mutex>
#include <condition_variable>
#include <memory>
#include <string>

namespace PhantomVfs {

struct CaseInsensitiveLess {
  using is_transparent = void;
  bool operator()(const std::wstring &lhs, const std::wstring &rhs) const {
    return _wcsicmp(lhs.c_str(), rhs.c_str()) < 0;
  }
  bool operator()(const std::wstring &lhs, const wchar_t *rhs) const {
    return _wcsicmp(lhs.c_str(), rhs) < 0;
  }
  bool operator()(const wchar_t *lhs, const std::wstring &rhs) const {
    return _wcsicmp(lhs, rhs.c_str()) < 0;
  }
};

class VfsStorage {
public:
  VfsStorage() = default;
  ~VfsStorage() = default;

  std::map<std::wstring, std::shared_ptr<VirtualFile>>& GetStorage() { return m_vfsStorage; }
  std::map<HANDLE, std::shared_ptr<VirtualHandle>>& GetHandleMap() { return m_handleMap; }
  std::condition_variable_any& GetLoadingCV() { return m_loadingCV; }
  std::unordered_set<std::wstring>& GetLoadingPaths() { return m_loadingPaths; }

  bool LookupProtectedCache(const std::wstring& path, bool& outVal) const;
  void InsertProtectedCache(const std::wstring& path, bool val);
  bool LookupTargetCache(const std::wstring& path, bool& outVal) const;
  void InsertTargetCache(const std::wstring& path, bool val);
  void ClearCache();

private:
  std::map<std::wstring, std::shared_ptr<VirtualFile>> m_vfsStorage;
  std::map<HANDLE, std::shared_ptr<VirtualHandle>> m_handleMap;
  std::condition_variable_any m_loadingCV;
  std::unordered_set<std::wstring> m_loadingPaths;

  mutable std::shared_mutex m_cacheMutex;
  std::map<std::wstring, bool, CaseInsensitiveLess> m_protectedCache;
  std::map<std::wstring, bool, CaseInsensitiveLess> m_targetCache;

  static constexpr size_t PATH_CACHE_SAFETY_CAP = 3000;
};

} // namespace PhantomVfs
