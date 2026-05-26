#include "VfsStorage.h"

namespace PhantomVfs {

bool VfsStorage::LookupProtectedCache(const std::wstring& path, bool& outVal) const {
  std::shared_lock<std::shared_mutex> lock(m_cacheMutex);
  auto it = m_protectedCache.find(path);
  if (it != m_protectedCache.end()) {
    outVal = it->second;
    return true;
  }
  return false;
}

void VfsStorage::InsertProtectedCache(const std::wstring& path, bool val) {
  std::unique_lock<std::shared_mutex> lock(m_cacheMutex);
  if (m_protectedCache.size() >= PATH_CACHE_SAFETY_CAP) {
    m_protectedCache.clear();
  }
  m_protectedCache[path] = val;
}

bool VfsStorage::LookupTargetCache(const std::wstring& path, bool& outVal) const {
  std::shared_lock<std::shared_mutex> lock(m_cacheMutex);
  auto it = m_targetCache.find(path);
  if (it != m_targetCache.end()) {
    outVal = it->second;
    return true;
  }
  return false;
}

void VfsStorage::InsertTargetCache(const std::wstring& path, bool val) {
  std::unique_lock<std::shared_mutex> lock(m_cacheMutex);
  if (m_targetCache.size() >= PATH_CACHE_SAFETY_CAP) {
    m_targetCache.clear();
  }
  m_targetCache[path] = val;
}

void VfsStorage::ClearCache() {
  std::unique_lock<std::shared_mutex> lock(m_cacheMutex);
  m_protectedCache.clear();
  m_targetCache.clear();
}

} // namespace PhantomVfs
