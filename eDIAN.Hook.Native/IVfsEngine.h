#pragma once
#include <string>
#include <memory>
#include <mutex>
#include <condition_variable>
#include <map>
#include <unordered_set>
#include <vector>
#include <Windows.h>
#include "VfsTypes.h"

namespace PhantomVfs {

class IVfsEngine {
public:
    virtual ~IVfsEngine() = default;

    // --- 초기화 및 제어 ---
    virtual void Initialize(LPCWSTR lpTempPath, LPCWSTR lpLogPath, int nLogLevel,
                            LPCWSTR lpConfigString, ::FnVfsOpenCallback openCb,
                            ::FnVfsCloseCallback closeCb) = 0;
    virtual void VaporizeSessionDir() = 0;

    // --- 경로 판별 및 보안 필터 ---
    virtual bool IsTarget(LPCWSTR lpFileName) = 0;
    virtual bool IsProtectedPath(LPCWSTR lpFileName) = 0;
    virtual bool IsManifestingPath(LPCWSTR lpFileName) = 0;
    virtual bool IsExternalProcess() = 0;
    virtual bool IsTrustedProcess() = 0;
    virtual bool CheckAccessPermission(LPCWSTR lpFileName) = 0;

    // --- 리소스 관리 및 정리 ---
    virtual void PassiveCleanupKeepers() = 0;
    virtual void CleanupLegacyGhosts() = 0;
    virtual void ReleaseKeeperByPath(const std::wstring &path) = 0;
    virtual void ReleaseAllKeepersInFolder(const std::wstring &folderPath) = 0;
    virtual void ReleaseAllKeepers() = 0;

    // --- 경로 유틸리티 ---
    virtual std::wstring NormalizePath(LPCWSTR lpFileName) = 0;
    virtual std::wstring GetUniqueGhostPath(const std::wstring &originalPath) = 0;
    virtual DWORD ExtractPIDFromPath(const std::wstring &path) = 0;
    virtual bool IsProcessAlive(DWORD pid) = 0;
    virtual bool IsProcessAcadOrPublish(DWORD pid) = 0;

    // --- 후킹 레이어용 접근자 ---
    virtual std::recursive_mutex &GetRegistryMutex() = 0;
    virtual std::map<std::wstring, std::shared_ptr<VirtualFile>> &GetStorage() = 0;
    virtual std::map<HANDLE, std::shared_ptr<VirtualHandle>> &GetHandleMap() = 0;
    virtual std::condition_variable_any &GetLoadingCV() = 0;
    virtual std::unordered_set<std::wstring> &GetLoadingPaths() = 0;
    virtual std::vector<KeeperInfo> &GetKeeperList() = 0;
    virtual const std::wstring &GetMipTempPath() const = 0;
    virtual const std::wstring &GetLogPath() const = 0;
    virtual const std::wstring &GetGhostSessionDir() const = 0;
    virtual const std::wstring &GetCurrentProcessName() const = 0;
};

// VfsEngine의 싱글톤 인스턴스를 가져오는 전역 도우미 함수
IVfsEngine& GetVfsEngine();

} // namespace PhantomVfs
