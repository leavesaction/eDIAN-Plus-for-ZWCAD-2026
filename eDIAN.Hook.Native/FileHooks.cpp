#include "DetoursInterceptor.h"
#include "IVfsEngine.h"
#include "VfsLogger.h"
#include <algorithm>
#include <atomic>
#include <exception>
#include <shlwapi.h>

#pragma comment(lib, "Shlwapi.lib")

// SEH는 C++ 스택 언와인딩(소멸자 호출)과 충돌할 수 있으므로,
// SEH는 "소멸자 없는" 작은 헬퍼 함수에서만 사용한다.
static BOOL WINAPI TryTrueCloseHandleNoThrow(HANDLE h) {
    __try {
        return TrueCloseHandle(h);
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        return FALSE;
    }
}

// 훅 재진입: TrueCreateFilePassthrough 호출 시 Internal_CreateFileW를 다시 타면 mutex deadlock 발생
static thread_local int g_hookPassthroughDepth = 0;

struct HookPassthroughScope {
    HookPassthroughScope() { ++g_hookPassthroughDepth; }
    ~HookPassthroughScope() { --g_hookPassthroughDepth; }
};

static HANDLE TrueCreateFilePassthrough(LPCWSTR lpFileName, DWORD dwAccess,
                                        DWORD dwShare, LPSECURITY_ATTRIBUTES lpSA,
                                        DWORD dwCreation, DWORD dwFlags,
                                        HANDLE hTemplate) {
    HookPassthroughScope scope;
    return TrueCreateFileW(lpFileName, dwAccess, dwShare, lpSA, dwCreation, dwFlags,
                           hTemplate);
}

// L3 SaveExposed + ZWCAD QSAVE sidecar 감지 (Lifecycle §3.1 저장 행)
static std::atomic<ULONGLONG> g_lastZwcadSaveSidecarTick{ 0 };
static std::atomic<ULONGLONG> g_mipCloseCommitUntil{ 0 };
static std::wstring g_mipCloseCommitPathNormalized;
static constexpr DWORD ZWCAD_SAVE_EXPOSED_WINDOW_MS = 8000;
static constexpr DWORD MIP_CLOSE_COMMIT_WINDOW_MS = 120000;

static bool IsInZwcadSaveWindow() {
    ULONGLONG last = g_lastZwcadSaveSidecarTick.load(std::memory_order_relaxed);
    if (last == 0)
        return false;
    return (GetTickCount64() - last) < ZWCAD_SAVE_EXPOSED_WINDOW_MS;
}

static bool IsZwcadSaveSidecarPath(LPCWSTR lpFileName) {
    if (!lpFileName) return false;
    return (StrStrIW(lpFileName, L"\\mip_data\\mip\\temp\\zws") != NULL) &&
           (_wcsicmp(PathFindExtensionW(lpFileName), L".tmp") == 0);
}

static bool IsMipTempUuidDwgPath(LPCWSTR lpFileName) {
    if (!lpFileName || !lpFileName[0])
        return false;
    if (StrStrIW(lpFileName, L"\\mip_data\\mip\\temp\\") == nullptr)
        return false;
    LPCWSTR name = PathFindFileNameW(lpFileName);
    if (!name || _wcsicmp(PathFindExtensionW(name), L".dwg") != 0)
        return false;
    LPCWSTR id = (name[0] == L'_') ? name + 1 : name;
    return wcslen(id) >= 36 && id[8] == L'-' && id[13] == L'-';
}

// MIP GetDecryptedTemporaryFile: output stream이 canonical _uuid.dwg를 최초 생성할 때
static bool IsMipUuidDecryptBootstrapWrite(LPCWSTR lpFileName, DWORD dwAccess,
                                             DWORD dwCreation) {
    if (!IsMipTempUuidDwgPath(lpFileName))
        return false;
    if (TrueGetFileAttributesW(lpFileName) != INVALID_FILE_ATTRIBUTES)
        return false;
    if ((dwAccess & (GENERIC_WRITE | FILE_WRITE_DATA)) == 0)
        return false;
    return dwCreation == CREATE_NEW || dwCreation == CREATE_ALWAYS ||
           dwCreation == OPEN_ALWAYS;
}

static HANDLE PassthroughCreateFileTrusted(PhantomVfs::IVfsEngine& engine,
                                           LPCWSTR lpFileName, DWORD dwAccess,
                                           DWORD dwShare, LPSECURITY_ATTRIBUTES lpSA,
                                           DWORD dwCreation, DWORD dwFlags,
                                           HANDLE hTemplate) {
    DWORD finalShare = dwShare;
    if (engine.IsTrustedProcess() && engine.IsProtectedPath(lpFileName))
        finalShare |= (FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE);
    return TrueCreateFilePassthrough(lpFileName, dwAccess, finalShare, lpSA, dwCreation,
                                     dwFlags, hTemplate);
}

static bool IsZwcadSaveManifestingPath(LPCWSTR lpFileName) {
    if (IsZwcadSaveSidecarPath(lpFileName))
        return true;
    if (!lpFileName || !lpFileName[0])
        return false;
    if (StrStrIW(lpFileName, L"\\mip_data\\mip\\temp\\") == nullptr)
        return false;
    LPCWSTR name = PathFindFileNameW(lpFileName);
    if (!name)
        return false;
    if (wcsncmp(name, L"zwTm", 4) == 0)
        return true;
    if (wcsncmp(name, L"zwsv", 4) == 0)
        return true;
    return false;
}

static bool IsMipTempUuidBakPath(LPCWSTR lpFileName) {
    if (!lpFileName || !lpFileName[0])
        return false;
    if (StrStrIW(lpFileName, L"\\mip_data\\mip\\temp\\") == nullptr)
        return false;
    LPCWSTR name = PathFindFileNameW(lpFileName);
    return name && name[0] == L'_' && _wcsicmp(PathFindExtensionW(name), L".bak") == 0;
}

// L3b: QSAVE rename/move/replace 대상 (mip temp 실물 I/O)
static bool IsMipTempSavePhysicalPath(LPCWSTR lpFileName) {
    return IsMipTempUuidDwgPath(lpFileName) || IsMipTempUuidBakPath(lpFileName) ||
           IsZwcadSaveSidecarPath(lpFileName) || IsZwcadSaveManifestingPath(lpFileName);
}

static std::wstring MipUuidDwgToBakPath(LPCWSTR dwgPath) {
    std::wstring bak = dwgPath ? dwgPath : L"";
    const size_t dot = bak.rfind(L'.');
    if (dot != std::wstring::npos)
        bak = bak.substr(0, dot) + L".bak";
    return bak;
}

static void UpdateStorageKeyAfterPhysicalMove(PhantomVfs::IVfsEngine& engine,
                                            const std::wstring& srcPath,
                                            const std::wstring& dstPath) {
    std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
    auto& storage = engine.GetStorage();
    auto it = storage.find(srcPath);
    if (it == storage.end())
        return;
    storage[dstPath] = it->second;
    storage[dstPath]->path = dstPath;
    storage.erase(it);
}

static void SyncVirtualFileFromDiskIfPresent(const std::shared_ptr<PhantomVfs::VirtualFile>& vf) {
    if (!vf)
        return;
    std::lock_guard<std::recursive_mutex> lock(vf->fileMtx);
    LPCWSTR path = vf->path.c_str();
    if (TrueGetFileAttributesW(path) == INVALID_FILE_ATTRIBUTES)
        return;
    HANDLE hReal = TrueCreateFilePassthrough(path, GENERIC_READ,
                                   FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                                   NULL, OPEN_EXISTING, 0, NULL);
    if (hReal == INVALID_HANDLE_VALUE)
        return;
    LARGE_INTEGER sz = {};
    if (TrueGetFileSizeEx(hReal, &sz) && sz.QuadPart > 0 &&
        vf->EnsureCapacity((size_t)sz.QuadPart)) {
        DWORD r = 0;
        TrueReadFile(hReal, vf->pBase, (DWORD)sz.QuadPart, &r, NULL);
        vf->currentSize.store((size_t)sz.QuadPart);
        vf->isModified = false;
    }
    TrueCloseHandle(hReal);
}

static void PhysicalVaporizeDiskOnly(const std::shared_ptr<PhantomVfs::VirtualFile>& vf) {
    if (!vf || !vf->saveExposed.load(std::memory_order_relaxed))
        return;
    SyncVirtualFileFromDiskIfPresent(vf);
    vf->lastVaporizedTime = GetTickCount64();

    // L2: mip temp _uuid.dwg — storage만 동기화, 디스크는 닫기/MIP commit까지 유지
    if (IsMipTempUuidDwgPath(vf->path.c_str())) {
        VFS_LOG_INFO(L"[SAVE-IO] PhysicalVaporize sync-only (mip uuid, disk retained): %s",
                     vf->path.c_str());
        vf->saveExposed.store(false, std::memory_order_release);
        return;
    }

    vf->saveExposed.store(false, std::memory_order_release);

    if (vf->refCount.load(std::memory_order_relaxed) > 0) {
        VFS_LOG_INFO(L"[SAVE-IO] PhysicalVaporizeDiskOnly (storage sync only, refs=%d): %s",
                     vf->refCount.load(), vf->path.c_str());
        return;
    }

    LPCWSTR path = vf->path.c_str();
    if (TrueDeleteFileW(path)) {
        VFS_LOG_INFO(L"[SAVE-IO] PhysicalVaporizeDiskOnly (deleted): %s", path);
    } else if (TrueGetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES) {
        TrueSetFileAttributesW(path, FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM);
        VFS_LOG_INFO(L"[SAVE-IO] PhysicalVaporizeDiskOnly (hidden): %s", path);
    }
}

static void EndSaveExposedIfExpired(PhantomVfs::IVfsEngine& engine) {
    if (IsInZwcadSaveWindow())
        return;
    std::vector<std::shared_ptr<PhantomVfs::VirtualFile>> toVaporize;
    {
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        for (auto& kv : engine.GetStorage()) {
            if (kv.second && kv.second->saveExposed.load(std::memory_order_relaxed))
                toVaporize.push_back(kv.second);
        }
    }
    for (auto& vf : toVaporize)
        PhysicalVaporizeDiskOnly(vf);
}

static void MarkSaveExposedForPath(PhantomVfs::IVfsEngine& engine, LPCWSTR lpFileName) {
    if (!IsMipTempUuidDwgPath(lpFileName))
        return;
    std::wstring path = engine.NormalizePath(lpFileName);
    std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
    auto it = engine.GetStorage().find(path);
    if (it != engine.GetStorage().end() && it->second)
        it->second->saveExposed.store(true, std::memory_order_release);
}

// L3a: QSAVE 시 ZWCAD가 _uuid.dwg 실물을 요구하기 전에 storage → 디스크 (§4.17)
static bool EnsureMipUuidDwgOnDiskForSave(const std::shared_ptr<PhantomVfs::VirtualFile>& vf) {
    if (!vf)
        return false;
    LPCWSTR path = vf->path.c_str();
    if (TrueGetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES) {
        vf->saveExposed.store(true, std::memory_order_release);
        return true;
    }

    std::lock_guard<std::recursive_mutex> lock(vf->fileMtx);
    TrueSetFileAttributesW(path, FILE_ATTRIBUTE_NORMAL);
    HANDLE hOrig = TrueCreateFilePassthrough(
        path, GENERIC_WRITE | GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hOrig == INVALID_HANDLE_VALUE) {
        VFS_LOG_ERR(L"!!! [SAVE-IO] SaveExposed pre-materialize failed: %s", path);
        return false;
    }
    size_t sizeToSave = vf->currentSize.load();
    bool writeSuccess = true;
    if (sizeToSave > 0) {
        DWORD written = 0;
        writeSuccess = TrueWriteFile(hOrig, vf->pBase, (DWORD)sizeToSave, &written, NULL) &&
                       written == sizeToSave;
    }
    TryTrueCloseHandleNoThrow(hOrig);
    if (!writeSuccess) {
        VFS_LOG_ERR(L"!!! [SAVE-IO] SaveExposed pre-materialize write failed: %s", path);
        return false;
    }
    vf->isModified = false;
    vf->saveExposed.store(true, std::memory_order_release);
    vf->lastVaporizedTime = GetTickCount64();
    VFS_LOG_INFO(L"[SAVE-IO] SaveExposed pre-materialize (%llu bytes): %s",
                 (unsigned long long)sizeToSave, path);
    return true;
}

static void PreMaterializeMipUuidDwgForPath(PhantomVfs::IVfsEngine& engine, LPCWSTR lpFileName) {
    if (!IsMipTempUuidDwgPath(lpFileName))
        return;
    const std::wstring path = engine.NormalizePath(lpFileName);
    std::shared_ptr<PhantomVfs::VirtualFile> vf;
    {
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        auto it = engine.GetStorage().find(path);
        if (it != engine.GetStorage().end())
            vf = it->second;
    }
    if (vf)
        EnsureMipUuidDwgOnDiskForSave(vf);
}

static bool IsMipCloseCommitForPath(LPCWSTR lpFileName) {
    const ULONGLONG until = g_mipCloseCommitUntil.load(std::memory_order_relaxed);
    if (until == 0 || GetTickCount64() > until)
        return false;
    if (!lpFileName || !lpFileName[0])
        return false;
    if (g_mipCloseCommitPathNormalized.empty())
        return IsMipTempUuidDwgPath(lpFileName);
    auto& engine = PhantomVfs::GetVfsEngine();
    const std::wstring path = engine.NormalizePath(lpFileName);
    return _wcsicmp(path.c_str(), g_mipCloseCommitPathNormalized.c_str()) == 0;
}

// MIP 복호화·CAD Open 직전: 디스크 실물을 storage로 흡수·삭제하면 MIP/CAD가 실패함
static bool ShouldSkipMipUuidDiskAbsorb(LPCWSTR lpFileName) {
    if (!IsMipTempUuidDwgPath(lpFileName))
        return false;
    if (IsInZwcadSaveWindow())
        return false;
    if (IsMipCloseCommitForPath(lpFileName))
        return false;
    return true;
}

static bool ShouldMipUuidPhysicalPassthrough(LPCWSTR lpFileName,
                                             const std::shared_ptr<PhantomVfs::VirtualFile>& vf) {
    if (!IsMipTempUuidDwgPath(lpFileName))
        return false;
    // 전역 QSAVE 창(8s)만으로는 새 문서 Open에 passthrough 금지 — 이전 도면 닫기 직후 재오픈 시
    // temp가 디스크에 남는 현상 방지 (vf->saveExposed는 해당 vf의 실제 QSAVE에서만 설정)
    if (vf && vf->saveExposed.load(std::memory_order_relaxed))
        return true;
    if (IsMipCloseCommitForPath(lpFileName))
        return true;
    return false;
}

// MIP 오픈 복호화: storage→디스크 1회 materialize 후 READ passthrough (storage 유지)
static bool ShouldMipUuidOpenDecryptPassthrough(LPCWSTR lpFileName, DWORD dwAccess,
                                                const std::shared_ptr<PhantomVfs::VirtualFile>& vf) {
    if (!vf || !IsMipTempUuidDwgPath(lpFileName))
        return false;
    if (ShouldMipUuidPhysicalPassthrough(lpFileName, vf))
        return false;
    if ((dwAccess & GENERIC_READ) == 0)
        return false;
    return TrueGetFileAttributesW(lpFileName) != INVALID_FILE_ATTRIBUTES;
}

static void TouchZwcadSaveSidecarTick(PhantomVfs::IVfsEngine& engine, LPCWSTR lpFileName) {
    if (!IsZwcadSaveSidecarPath(lpFileName) && !IsZwcadSaveManifestingPath(lpFileName))
        return;
    g_lastZwcadSaveSidecarTick.store(GetTickCount64(), std::memory_order_relaxed);
    (void)engine;
}

static bool MaterializeMipUuidDwgForClose(const std::shared_ptr<PhantomVfs::VirtualFile>& vf);

// L1: canonical _uuid.dwg 디스크만 제거 (storage·고스트·Keeper 유지)
static bool VaporizeMipUuidDwgCanonicalDisk(PhantomVfs::IVfsEngine& engine,
                                            const std::shared_ptr<PhantomVfs::VirtualFile>& vf,
                                            LPCWSTR logContext,
                                            bool forceAfterCadOpen = false) {
    if (!vf || !IsMipTempUuidDwgPath(vf->path.c_str()))
        return false;
    if (!forceAfterCadOpen) {
        if (IsInZwcadSaveWindow() ||
            vf->saveExposed.load(std::memory_order_relaxed) ||
            IsMipCloseCommitForPath(vf->path.c_str()))
            return false;
    } else if (IsMipCloseCommitForPath(vf->path.c_str())) {
        return false;
    }

    SyncVirtualFileFromDiskIfPresent(vf);
    LPCWSTR path = vf->path.c_str();
    if (TrueGetFileAttributesW(path) == INVALID_FILE_ATTRIBUTES) {
        vf->lastVaporizedTime = GetTickCount64();
        return true;
    }

    TrueSetFileAttributesW(path, FILE_ATTRIBUTE_NORMAL);
    if (TrueDeleteFileW(path)) {
        VFS_LOG_INFO(L"[OPEN] L1 canonical disk vaporized (%s): %s",
                     logContext ? logContext : L"", path);
    } else {
        TrueSetFileAttributesW(path, FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM);
        VFS_LOG_INFO(L"[OPEN] L1 canonical disk hidden (%s): %s",
                     logContext ? logContext : L"", path);
    }
    vf->lastVaporizedTime = GetTickCount64();
    if (forceAfterCadOpen)
        vf->saveExposed.store(false, std::memory_order_release);
    (void)engine;
    return true;
}

static bool VaporizeMipUuidDwgCanonicalDiskByPath(PhantomVfs::IVfsEngine& engine,
                                                  LPCWSTR lpFileName,
                                                  LPCWSTR logContext,
                                                  bool forceAfterCadOpen = false) {
    if (!IsMipTempUuidDwgPath(lpFileName))
        return false;
    const std::wstring path = engine.NormalizePath(lpFileName);
    std::shared_ptr<PhantomVfs::VirtualFile> vf;
    {
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        auto it = engine.GetStorage().find(path);
        if (it != engine.GetStorage().end())
            vf = it->second;
    }
    if (!vf)
        return false;
    return VaporizeMipUuidDwgCanonicalDisk(engine, vf, logContext, forceAfterCadOpen);
}

// L2/L3b: MIP commit용 — 디스크 dwg 없으면 동일 UUID .bak 에서 복원
static bool EnsureMipCommitFileFromDiskOrBak(const std::shared_ptr<PhantomVfs::VirtualFile>& vf) {
    if (!vf)
        return false;
    LPCWSTR path = vf->path.c_str();
    if (TrueGetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES) {
        SyncVirtualFileFromDiskIfPresent(vf);
        return true;
    }
    const std::wstring bak = MipUuidDwgToBakPath(path);
    if (!bak.empty() && TrueGetFileAttributesW(bak.c_str()) != INVALID_FILE_ATTRIBUTES) {
        TrueSetFileAttributesW(bak.c_str(), FILE_ATTRIBUTE_NORMAL);
        TrueSetFileAttributesW(path, FILE_ATTRIBUTE_NORMAL);
        if (TrueCopyFileW(bak.c_str(), path, FALSE)) {
            VFS_LOG_INFO(L"[CLOSE] materialize from .bak sibling: %s -> %s",
                         bak.c_str(), path);
            SyncVirtualFileFromDiskIfPresent(vf);
            if (TrueGetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES)
                return true;
        }
    }
    return MaterializeMipUuidDwgForClose(vf);
}

static bool EnsureMipCommitPathOnDisk(PhantomVfs::IVfsEngine& engine, LPCWSTR lpFileName) {
    if (!IsMipTempUuidDwgPath(lpFileName))
        return false;
    if (TrueGetFileAttributesW(lpFileName) != INVALID_FILE_ATTRIBUTES)
        return true;
    const std::wstring path = engine.NormalizePath(lpFileName);
    std::shared_ptr<PhantomVfs::VirtualFile> vf;
    {
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        auto it = engine.GetStorage().find(path);
        if (it != engine.GetStorage().end())
            vf = it->second;
    }
    if (vf)
        return EnsureMipCommitFileFromDiskOrBak(vf);
    const std::wstring bak = MipUuidDwgToBakPath(lpFileName);
    if (bak.empty() || TrueGetFileAttributesW(bak.c_str()) == INVALID_FILE_ATTRIBUTES)
        return false;
    TrueSetFileAttributesW(bak.c_str(), FILE_ATTRIBUTE_NORMAL);
    TrueSetFileAttributesW(lpFileName, FILE_ATTRIBUTE_NORMAL);
    if (!TrueCopyFileW(bak.c_str(), lpFileName, FALSE))
        return false;
    VFS_LOG_INFO(L"[CLOSE] materialize from .bak (no storage): %s", lpFileName);
    return TrueGetFileAttributesW(lpFileName) != INVALID_FILE_ATTRIBUTES;
}

static bool MaterializeMipUuidDwgForClose(const std::shared_ptr<PhantomVfs::VirtualFile>& vf) {
    if (!vf)
        return false;
    SyncVirtualFileFromDiskIfPresent(vf);
    std::lock_guard<std::recursive_mutex> lock(vf->fileMtx);
    LPCWSTR path = vf->path.c_str();
    size_t sizeToSave = vf->currentSize.load();

    // on-disk passthrough 편집: storage는 0이어도 디스크에 실제 DWG가 있으면 CREATE_ALWAYS 금지
    if (sizeToSave == 0 &&
        TrueGetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES) {
        WIN32_FILE_ATTRIBUTE_DATA fad = {};
        if (GetFileAttributesExW(path, GetFileExInfoStandard, &fad)) {
            const ULONGLONG diskBytes =
                ((ULONGLONG)fad.nFileSizeHigh << 32) | fad.nFileSizeLow;
            if (diskBytes > 0) {
                VFS_LOG_INFO(
                    L"[CLOSE] materialize skipped (disk %llu bytes, storage empty): %s",
                    diskBytes, path);
                return true;
            }
        }
    }

    TrueSetFileAttributesW(path, FILE_ATTRIBUTE_NORMAL);
    HANDLE hOrig = TrueCreateFilePassthrough(
        path, GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hOrig == INVALID_HANDLE_VALUE)
        return false;
    VFS_LOG_INFO(L"[CLOSE] materialize start (%llu bytes): %s",
                 (unsigned long long)sizeToSave, path);
    bool writeSuccess = true;
    if (sizeToSave > 0) {
        DWORD written = 0;
        writeSuccess = TrueWriteFile(hOrig, vf->pBase, (DWORD)sizeToSave, &written, NULL) &&
                       written == sizeToSave;
    }
    TryTrueCloseHandleNoThrow(hOrig);
    if (writeSuccess) {
        vf->isModified = false;
        VFS_LOG_INFO(L"[CLOSE] materialized %llu bytes: %s",
                     (unsigned long long)sizeToSave, path);
    } else {
        VFS_LOG_ERR(L"!!! [CLOSE] materialize failed: %s", path);
    }
    return writeSuccess;
}

static void ReleaseVirtualFileFromRegistry(PhantomVfs::IVfsEngine& engine,
                                           const std::wstring& path) {
    std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
    auto& storage = engine.GetStorage();
    auto& handleMap = engine.GetHandleMap();
    for (auto it = handleMap.begin(); it != handleMap.end();) {
        if (it->second && it->second->file && it->second->file->path == path)
            it = handleMap.erase(it);
        else
            ++it;
    }
    storage.erase(path);
}

// L2: MIP ApplyProtection·닫기 — Managed PrepareMipTempDwgForCloseCommit에서만 선행 materialize

/** @brief 폴더 삭제 감시: 임시 폴더 삭제 시 관리 중인 핸들들을 정리합니다. */
BOOL WINAPI Hooked_RemoveDirectoryW(LPCWSTR lpPathName) {
    auto& engine = PhantomVfs::GetVfsEngine();
    if (engine.IsManifestingPath(lpPathName)) {
        std::wstring path = engine.NormalizePath(lpPathName);
        engine.ReleaseAllKeepersInFolder(path);
        VFS_LOG_INFO(L"디렉토리 제거 허용: %s", lpPathName);
    }
    return TrueRemoveDirectoryW(lpPathName);
}

/** @brief 파일 생성/오픈 가로채기: 가상화의 핵심 진입점 (실제 로직) */
HANDLE WINAPI Internal_CreateFileW(LPCWSTR lpFileName, DWORD dwAccess,
                                   DWORD dwShare, LPSECURITY_ATTRIBUTES lpSA,
                                   DWORD dwCreation, DWORD dwFlags,
                                   HANDLE hTemplate) {
    if (g_hookPassthroughDepth > 0) {
        return TrueCreateFileW(lpFileName, dwAccess, dwShare, lpSA, dwCreation,
                               dwFlags, hTemplate);
    }

    auto& engine = PhantomVfs::GetVfsEngine();
    
    // [Phase 3: API Tracing] 상세 호출 기록
    VFS_LOG_DEBUG(L"[TRACE] CreateFileW: %s (Access: 0x%08X, Creation: %d)", 
               lpFileName ? lpFileName : L"NULL", dwAccess, dwCreation);

    // ZWCAD 저장(QSAVE) sidecar(zws*.tmp)는 반드시 감지되어야 하므로,
    // engine.IsManifestingPath 분기와 무관하게 진입 초기에 시각을 기록한다.
    TouchZwcadSaveSidecarTick(engine, lpFileName);
    EndSaveExposedIfExpired(engine);

    // [VFS Phase 9.5: Integrated Security Guard] 
    // 비인가 프로세스의 보호 구역(Ghost, eDIAN) 접근을 원천 차단 (읽기/복사 방지)
    if (!engine.CheckAccessPermission(lpFileName)) {
        VFS_LOG_WARN(L"[Security Guard] 접근 차단: %s", lpFileName);
        SetLastError(ERROR_ACCESS_DENIED);
        return INVALID_HANDLE_VALUE;
    }

    // 1. 임시 경로 접근 시: 불필요한 삭제 플래그 제거 및 청소기 작동
    if (engine.IsManifestingPath(lpFileName)) {
        engine.PassiveCleanupKeepers();
        DWORD secureFlags = (dwFlags & ~FILE_FLAG_DELETE_ON_CLOSE);
        if (secureFlags == 0)
            secureFlags = FILE_ATTRIBUTE_NORMAL;
        return TrueCreateFilePassthrough(lpFileName, dwAccess, dwShare, lpSA, dwCreation,
                               secureFlags, hTemplate);
    }

    // 2. 가상화 대상 파일(DWG 등) 접근 시
    if (engine.IsTarget(lpFileName)) {
        if (IsMipUuidDecryptBootstrapWrite(lpFileName, dwAccess, dwCreation)) {
            VFS_LOG_INFO(L"[OPEN] MIP decrypt bootstrap passthrough CreateFile: %s",
                         lpFileName);
            return PassthroughCreateFileTrusted(engine, lpFileName, dwAccess, dwShare,
                                                lpSA, dwCreation, dwFlags, hTemplate);
        }

        std::wstring path = engine.NormalizePath(lpFileName);
        std::shared_ptr<PhantomVfs::VirtualFile> vf = nullptr;
        bool mipPathFreshlyRegistered = false;

        // [1단계] Registry Mutex: 이미 로드되어 있는지 빠르게 검사 및 대기
        {
            std::unique_lock<std::recursive_mutex> lock(engine.GetRegistryMutex());
            
            // 동일한 파일이 다른 스레드에서 로딩 중이라면 완료될 때까지 대기
            while (engine.GetLoadingPaths().count(path) > 0) {
                engine.GetLoadingCV().wait(lock);
            }

            auto& storage = engine.GetStorage();
            auto it = storage.find(path);
            if (it != storage.end()) {
                vf = it->second;
            }
        }

        // [2단계] Lock-Free Heavy Load: 등록이 안 되어 있다면 락 밖에서 실물 파일 흡수 수행
        if (!vf) {
            // 다른 스레드들의 동시 진입을 막기 위해 로딩 상태 등록
            {
                std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
                engine.GetLoadingPaths().insert(path);
            }

            auto newVf = std::make_shared<PhantomVfs::VirtualFile>(path);
            const bool skipAbsorbForSave =
                IsMipTempUuidDwgPath(lpFileName) && IsInZwcadSaveWindow();
            const bool skipAbsorbForMipDecrypt =
                ShouldSkipMipUuidDiskAbsorb(lpFileName);
            if (TrueGetFileAttributesW(lpFileName) != INVALID_FILE_ATTRIBUTES &&
                !skipAbsorbForSave && !skipAbsorbForMipDecrypt) {
                // 실물 파일이 있으면 데이터를 메모리로 흡수
                HANDLE hReal = TrueCreateFilePassthrough(lpFileName, GENERIC_READ,
                                               FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                                               NULL, OPEN_EXISTING, 0, NULL);
                if (hReal != INVALID_HANDLE_VALUE) {
                    LARGE_INTEGER sz;
                    TrueGetFileSizeEx(hReal, &sz);
                    if (sz.QuadPart > 0) {
                        if (newVf->EnsureCapacity((size_t)sz.QuadPart)) {
                            DWORD r;
                            TrueReadFile(hReal, newVf->pBase, (DWORD)sz.QuadPart, &r, NULL);
                            newVf->currentSize = (size_t)sz.QuadPart;
                            VFS_LOG_INFO(L"실물 데이터 흡수 완료 (락 외부): %llu bytes", sz.QuadPart);
                        }
                    }
                    TrueCloseHandle(hReal);
                    // 실물 파일은 기화(Vaporize) 시켜 은폐함
                    if (TrueDeleteFileW(lpFileName)) {
                        VFS_LOG_INFO(L"실물 파일 기화 완료: %s", lpFileName);
                    } else {
                        VFS_LOG_INFO(L"파일 은닉 (속성 변경): %s", lpFileName);
                        TrueSetFileAttributesW(lpFileName, FILE_ATTRIBUTE_HIDDEN |
                                                               FILE_ATTRIBUTE_SYSTEM);
                    }
                }
            }
            newVf->lastVaporizedTime = GetTickCount64();

            // [3단계] Registry Mutex: 최종 등록 및 로딩 해제 통보
            {
                std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
                auto& storage = engine.GetStorage();
                auto it = storage.find(path);
                if (it == storage.end()) {
                    storage[path] = newVf;
                    vf = newVf;
                    mipPathFreshlyRegistered = true;
                } else {
                    // 경쟁 상태에서 다른 스레드가 먼저 등록했다면 이를 사용
                    vf = it->second;
                }
                
                // 로딩 목록에서 제거 및 대기 중인 스레드 깨우기
                engine.GetLoadingPaths().erase(path);
                engine.GetLoadingCV().notify_all();
            }
        }

        if (vf && IsMipTempUuidDwgPath(lpFileName) && !mipPathFreshlyRegistered) {
            if (IsInZwcadSaveWindow()) {
                SyncVirtualFileFromDiskIfPresent(vf);
                PreMaterializeMipUuidDwgForPath(engine, lpFileName);
            }
            if (IsInZwcadSaveWindow() || vf->saveExposed.load(std::memory_order_relaxed))
                EnsureMipUuidDwgOnDiskForSave(vf);
        }

        if (!mipPathFreshlyRegistered && IsInZwcadSaveWindow() &&
            IsMipTempUuidDwgPath(lpFileName))
            MarkSaveExposedForPath(engine, lpFileName);

        if (vf && IsMipTempUuidDwgPath(lpFileName) && (dwAccess & GENERIC_READ) != 0 &&
            TrueGetFileAttributesW(lpFileName) == INVALID_FILE_ATTRIBUTES &&
            !ShouldMipUuidPhysicalPassthrough(lpFileName, vf)) {
            EnsureMipCommitFileFromDiskOrBak(vf);
        }

        if (ShouldMipUuidPhysicalPassthrough(lpFileName, vf))
            EnsureMipCommitPathOnDisk(engine, lpFileName);

        const bool mipPassthrough =
            ShouldMipUuidPhysicalPassthrough(lpFileName, vf) ||
            ShouldMipUuidOpenDecryptPassthrough(lpFileName, dwAccess, vf);

        // L3/L2: _uuid.dwg 실물 I/O — QSAVE·닫기 commit·MIP 오픈 복호화(1회)
        if (mipPassthrough && TrueGetFileAttributesW(lpFileName) != INVALID_FILE_ATTRIBUTES) {
            DWORD finalShare = dwShare;
            if (engine.IsTrustedProcess() && engine.IsProtectedPath(lpFileName)) {
                finalShare |= (FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE);
            }
            if (vf && vf->saveExposed.load(std::memory_order_relaxed)) {
                VFS_LOG_INFO(L"[SAVE-IO] SaveExposed passthrough CreateFile: %s", lpFileName);
            } else if (ShouldMipUuidPhysicalPassthrough(lpFileName, vf)) {
                VFS_LOG_INFO(L"[CLOSE] MIP read passthrough CreateFile: %s", lpFileName);
            } else {
                VFS_LOG_INFO(L"[OPEN] MIP decrypt passthrough CreateFile: %s", lpFileName);
            }
            return TrueCreateFilePassthrough(lpFileName, dwAccess, finalShare, lpSA, dwCreation,
                                   dwFlags, hTemplate);
        }

        LPCWSTR ext = PathFindExtensionW(path.c_str());
        bool isExternal = engine.IsExternalProcess();
        ULONGLONG now = GetTickCount64();

        // 3. JIT 실체화 로직: 외부 프로세스나 읽기 전용 작업 시 일시적으로 실물 파일 생성
        if (vf && (_wcsicmp(ext, L".dwg") == 0) &&
            TrueGetFileAttributesW(lpFileName) == INVALID_FILE_ATTRIBUTES) {
            if (dwAccess & GENERIC_READ) {
                bool isReadOnlyPlot =
                    (dwAccess == GENERIC_READ) || (dwAccess == FILE_READ_DATA);

                // ZWCAD 저장 sidecar(zws*.tmp) 직후 구간에서는 JIT 실체화(share=0 독점)를 억제한다.
                // 저장 마무리의 원자 교체(Replace/Move/Copy)와 충돌하면 “DWG 저장 실패 → TMP 저장”이 발생할 수 있음.
                ULONGLONG lastSidecar = g_lastZwcadSaveSidecarTick.load(std::memory_order_relaxed);
                if (!isExternal && lastSidecar != 0 && (now - lastSidecar) < 5000) {
                    VFS_LOG_INFO(L"[SAVE-IO] JIT 실체화 억제(저장 구간, %llums): %s",
                               (now - lastSidecar), path.c_str());
                    goto skip_jit_manifesting;
                }
                // L1: mip _uuid.dwg — 고스트 편집 중 canonical JIT 실체화 금지
                if (!isExternal && IsMipTempUuidDwgPath(lpFileName) &&
                    vf->hKeeper != INVALID_HANDLE_VALUE) {
                    VFS_LOG_DEBUG(L"[OPEN] L1 JIT 억제(고스트 활성): %s", path.c_str());
                    goto skip_jit_manifesting;
                }
                if (isExternal || isReadOnlyPlot ||
                    (now - vf->lastVaporizedTime > 500)) {
                    if (vf->isManifesting.exchange(true)) {
                        int retry = 0;
                        while (vf->isManifesting && retry++ < 20)
                            Sleep(10);
                    }
                    PhantomVfs::ManifestingGuard mg(vf->isManifesting);
                    if (TrueGetFileAttributesW(lpFileName) == INVALID_FILE_ATTRIBUTES) {
                        VFS_LOG_INFO(L"JIT 실체화 수행 (v4.7s Persistent): %s",
                                   path.c_str());
                        HANDLE hManifest = TrueCreateFilePassthrough(
                            lpFileName, GENERIC_WRITE | GENERIC_READ,
                            0, NULL, // 0 = 독점 점유 (복사 방지 핵심)
                            CREATE_ALWAYS,
                            FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM |
                                FILE_ATTRIBUTE_TEMPORARY,
                            NULL);
                        if (hManifest != INVALID_HANDLE_VALUE) {
                            DWORD w;
                            size_t sz = vf->currentSize.load();
                            if (sz > 0)
                                TrueWriteFile(hManifest, vf->pBase, (DWORD)sz, &w, NULL);
                            
                            // 외부 프로세스면 실물 핸들을 직접 반환
                            if (isExternal) {
                                HANDLE hEngine =
                                    TrueCreateFilePassthrough(lpFileName, dwAccess, dwShare, lpSA,
                                                    dwCreation, dwFlags, hTemplate);
                                TrueCloseHandle(hManifest);
                                return hEngine;
                            }
                            // 내부 핸들이면 가상 핸들로 래핑
                            auto vh = std::make_shared<PhantomVfs::VirtualHandle>();
                            vh->file = vf;
                            vh->position = 0;
                            {
                                std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
                                vh->file->refCount++;
                                engine.GetHandleMap()[hManifest] = vh;
                            }
                            return hManifest;
                        }
                    }
                }
            }
        }
skip_jit_manifesting:

        // MIP 복호화 후 canonical 실물이 있으면 고스트 대신 직접 Open (빈 고스트 방지)
        if (IsMipTempUuidDwgPath(lpFileName) &&
            TrueGetFileAttributesW(lpFileName) != INVALID_FILE_ATTRIBUTES &&
            !engine.IsExternalProcess()) {
            VFS_LOG_INFO(L"[OPEN] MIP temp on-disk passthrough CreateFile: %s", lpFileName);
            return PassthroughCreateFileTrusted(engine, lpFileName, dwAccess, dwShare, lpSA,
                                                dwCreation, dwFlags, hTemplate);
        }

        // 4. 고스트 핸들 생성: 실물 파일 대신 유니크한 임시 파일로 우회
        std::wstring uniqueGhost = engine.GetUniqueGhostPath(path);
        DWORD creation = (dwCreation == OPEN_EXISTING) ? OPEN_ALWAYS : dwCreation;
        // [VFS Phase 9.7] 고스트 파일도 독점 점유하여 외부 유출 원천 봉쇄
        HANDLE hSurrogate = TrueCreateFilePassthrough(
            uniqueGhost.c_str(), dwAccess, 0, lpSA, creation,
            dwFlags | FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_TEMPORARY |
                FILE_FLAG_DELETE_ON_CLOSE,
            NULL);

        if (hSurrogate != INVALID_HANDLE_VALUE) {
            VFS_LOG_INFO(L"유니크 고스트 생성 (은닉): %s -> %s", path.c_str(),
                       uniqueGhost.c_str());
            // 파일 유지를 위해 키퍼 핸들 복제
            {
                std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
                if (vf->hKeeper == INVALID_HANDLE_VALUE) {
                    DuplicateHandle(GetCurrentProcess(), hSurrogate, GetCurrentProcess(),
                                    &vf->hKeeper, 0, FALSE, DUPLICATE_SAME_ACCESS);
                }
            }
            size_t sz = vf->currentSize.load();
            if (sz > 0) {
                DWORD w;
                TrueWriteFile(hSurrogate, vf->pBase, (DWORD)sz, &w, NULL);
                LARGE_INTEGER liZero = { 0 };
                TrueSetFilePointerEx(hSurrogate, liZero, NULL, FILE_BEGIN);
            }
            auto vh = std::make_shared<PhantomVfs::VirtualHandle>();
            vh->file = vf;
            vh->position = 0;
            {
                std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
                vh->file->refCount++;
                engine.GetHandleMap()[hSurrogate] = vh;
            }
            if (IsMipTempUuidDwgPath(lpFileName) && vf->currentSize.load() > 0)
                VaporizeMipUuidDwgCanonicalDisk(engine, vf, L"GhostReady");
            return hSurrogate;
        } else {
            VFS_LOG_INFO(L"치명적 오류: 고스트 생성 실패! 에러코드: %d", GetLastError());
        }
    }
    // 5. 일반 접근: 아군(Trusted)이 보호 구역 접근 시 공유 권한을 확장하여 자가 잠금(Self-Lock) 방지
    DWORD finalShare = dwShare;
    if (engine.IsTrustedProcess() && engine.IsProtectedPath(lpFileName)) {
        finalShare |= (FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE);
    }

    HANDLE hResult = TrueCreateFilePassthrough(lpFileName, dwAccess, finalShare, lpSA, dwCreation,
                           dwFlags, hTemplate);

    if (hResult == INVALID_HANDLE_VALUE) {
        DWORD err = GetLastError();
        if (err != ERROR_FILE_NOT_FOUND && err != ERROR_PATH_NOT_FOUND) {
            VFS_LOG_DEBUG(L"[TRACE] CreateFileW FAILED: %s (Error: %d)", lpFileName ? lpFileName : L"NULL", err);
        }
    }
    return hResult;
}

static HANDLE CreateFileWWithCppGuard(LPCWSTR lpFileName, DWORD dwAccess,
                                      DWORD dwShare, LPSECURITY_ATTRIBUTES lpSA,
                                      DWORD dwCreation, DWORD dwFlags,
                                      HANDLE hTemplate) {
    try {
        return Internal_CreateFileW(lpFileName, dwAccess, dwShare, lpSA,
                                    dwCreation, dwFlags, hTemplate);
    } catch (const std::exception& e) {
        VFS_LOG_ERR(L"!!! C++ exception in CreateFileW: %s | %S",
                    lpFileName ? lpFileName : L"NULL", e.what());
        SetLastError(ERROR_GEN_FAILURE);
        return INVALID_HANDLE_VALUE;
    } catch (...) {
        VFS_LOG_ERR(L"!!! C++ unknown exception in CreateFileW: %s",
                    lpFileName ? lpFileName : L"NULL");
        SetLastError(ERROR_GEN_FAILURE);
        return INVALID_HANDLE_VALUE;
    }
}

/** @brief 파일 생성 가로채기 래퍼: SEH 보호막을 제공합니다. */
HANDLE WINAPI Hooked_CreateFileW(LPCWSTR lpFileName, DWORD dwAccess,
                                 DWORD dwShare, LPSECURITY_ATTRIBUTES lpSA,
                                 DWORD dwCreation, DWORD dwFlags,
                                 HANDLE hTemplate) {
    __try {
        return CreateFileWWithCppGuard(lpFileName, dwAccess, dwShare, lpSA,
                                       dwCreation, dwFlags, hTemplate);
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        DWORD code = GetExceptionCode();
        VFS_LOG_ERR(L"!!! CRITICAL EXCEPTION in CreateFileW: %s (Code: 0x%08X)",
                   lpFileName ? lpFileName : L"NULL", code);
        return INVALID_HANDLE_VALUE;
    }
}

/** @brief 가상 파일 쓰기: 메모리 버퍼로 데이터를 직접 씁니다. */
BOOL WINAPI Hooked_WriteFile(HANDLE hFile, LPCVOID lpBuf, DWORD nWrite,
                             LPDWORD pWritten, LPOVERLAPPED lpOv) {
    auto& engine = PhantomVfs::GetVfsEngine();
    std::shared_ptr<PhantomVfs::VirtualHandle> vh = nullptr;
    {
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        auto& handleMap = engine.GetHandleMap();
        auto it = handleMap.find(hFile);
        if (it != handleMap.end())
            vh = it->second;
    }
    if (vh) {
        size_t needed = (size_t)(vh->position + nWrite);
        if (needed > vh->file->totalCapacity)
            vh->file->EnsureCapacity(needed);
        memcpy((BYTE*)vh->file->pBase + vh->position, lpBuf, nWrite);
        vh->position += nWrite;
        size_t oldSize = vh->file->currentSize.load();
        while (needed > oldSize &&
               !vh->file->currentSize.compare_exchange_weak(oldSize, needed))
            ;
        vh->file->isModified = true;
        if (pWritten)
            *pWritten = nWrite;
        return TRUE;
    }
    return TrueWriteFile(hFile, lpBuf, nWrite, pWritten, lpOv);
}

/** @brief 핸들 닫기: 참조 카운트를 관리하고 최종 수정본을 디스크에 커밋합니다. (실제 로직) */
BOOL WINAPI Internal_CloseHandle(HANDLE hFile) {
    auto& engine = PhantomVfs::GetVfsEngine();
    
    std::shared_ptr<PhantomVfs::VirtualFile> commitTarget = nullptr;
    {
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        auto& handleMap = engine.GetHandleMap();
        auto it = handleMap.find(hFile);
        if (it != handleMap.end()) {
            auto vh = it->second;
            vh->file->refCount--;
            if (vh->file->refCount == 0) {
                commitTarget = vh->file;
            }
            handleMap.erase(it);
        }
    }

    if (commitTarget) {
        LPCWSTR ext = PathFindExtensionW(commitTarget->path.c_str());
        bool isDwg = (_wcsicmp(ext, L".dwg") == 0);
        const bool isMipUuidDwg =
            IsMipTempUuidDwgPath(commitTarget->path.c_str());
        const bool inSaveWindow = IsInZwcadSaveWindow();

        if (isMipUuidDwg) {
            if (inSaveWindow) {
                VFS_LOG_DEBUG(L"[SAVE-IO] skip ghost-close disk commit (save window): %s",
                              commitTarget->path.c_str());
            }
            // mip _uuid.dwg: storage 유지 (오픈 프로브 refCount==0에서 virtual release 금지)
        } else if (isDwg) {
            std::lock_guard<std::recursive_mutex> lock(commitTarget->fileMtx);
            ULONGLONG now = GetTickCount64();
            if (commitTarget->isModified ||
                (now - commitTarget->lastVaporizedTime > 500)) {
                HANDLE hOrig = TrueCreateFilePassthrough(commitTarget->path.c_str(), GENERIC_WRITE,
                                               FILE_SHARE_READ, NULL, CREATE_ALWAYS,
                                               FILE_ATTRIBUTE_NORMAL, NULL);
                if (hOrig != INVALID_HANDLE_VALUE) {
                    size_t sizeToSave = commitTarget->currentSize.load();
                    VFS_LOG_INFO(L"최종 변경사항 디스크 커밋 시작 (%llu bytes): %s",
                               sizeToSave, commitTarget->path.c_str());
                    bool writeSuccess = true;
                    const size_t CHUNK_SIZE = 16 * 1024 * 1024;
                    const size_t SYNC_WRITE_THRESHOLD = 20 * 1024 * 1024;
                    if (sizeToSave <= SYNC_WRITE_THRESHOLD) {
                        DWORD written = 0;
                        if (!TrueWriteFile(hOrig, commitTarget->pBase, (DWORD)sizeToSave,
                                           &written, NULL) ||
                            written != sizeToSave)
                            writeSuccess = false;
                    } else {
                        size_t offset = 0;
                        while (offset < sizeToSave) {
                            size_t bytesToWrite =
                                (std::min)(CHUNK_SIZE, sizeToSave - offset);
                            DWORD written = 0;
                            if (!TrueWriteFile(hOrig, (BYTE*)commitTarget->pBase + offset,
                                               (DWORD)bytesToWrite, &written, NULL) ||
                                written != bytesToWrite) {
                                writeSuccess = false;
                                break;
                            }
                            offset += bytesToWrite;
                            SwitchToThread();
                        }
                    }
                    if (writeSuccess) {
                        commitTarget->isModified = false;
                        VFS_LOG_INFO(L"최종 변경사항 디스크 커밋 완료: %s",
                                   commitTarget->path.c_str());
                    } else {
                        VFS_LOG_ERR(L"!!! 최종 변경사항 디스크 커밋 실패: %s",
                                  commitTarget->path.c_str());
                    }
                    TryTrueCloseHandleNoThrow(hOrig);
                }
            }
        }
        if (commitTarget->hKeeper != INVALID_HANDLE_VALUE) {
            TryTrueCloseHandleNoThrow(commitTarget->hKeeper);
            commitTarget->hKeeper = INVALID_HANDLE_VALUE;
        }
        VFS_LOG_INFO(L"[VFS] 가상 파일 참조 해제 및 Keeper 폐쇄: %s", commitTarget->path.c_str());
    }
    return TryTrueCloseHandleNoThrow(hFile);
}

/** @brief 핸들 닫기 래퍼: SEH 보호막을 제공합니다. */
BOOL WINAPI Hooked_CloseHandle(HANDLE hFile) {
    __try {
        return Internal_CloseHandle(hFile);
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        auto& engine = PhantomVfs::GetVfsEngine();
        DWORD code = GetExceptionCode();
        VFS_LOG_ERR(L"!!! CRITICAL EXCEPTION in CloseHandle for Handle: 0x%p (Code: 0x%08X)",
                   hFile, code);
        // 예외 상황에서는 내부 상태 보장을 최우선으로 두고, 원본 CloseHandle은 no-throw 래퍼로 호출한다.
        return TryTrueCloseHandleNoThrow(hFile);
    }
}

/** @brief 가상 파일 읽기: 메모리 버퍼에서 데이터를 즉시 읽어옵니다. */
BOOL WINAPI Hooked_ReadFile(HANDLE hFile, LPVOID lpBuf, DWORD nRead,
                            LPDWORD pRead, LPOVERLAPPED lpOv) {
    auto& engine = PhantomVfs::GetVfsEngine();
    std::shared_ptr<PhantomVfs::VirtualHandle> vh = nullptr;
    {
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        auto& handleMap = engine.GetHandleMap();
        auto it = handleMap.find(hFile);
        if (it != handleMap.end())
            vh = it->second;
    }
    if (vh) {
        size_t savedSize = vh->file->currentSize.load();
        long long remains = (long long)savedSize - vh->position;
        DWORD actual =
            (DWORD)(std::min)((long long)nRead, (remains < 0 ? 0 : remains));
        if (actual > 0) {
            memcpy(lpBuf, (BYTE*)vh->file->pBase + vh->position, actual);
            vh->position += actual;
        }
        if (pRead)
            *pRead = actual;
        return TRUE;
    }
    return TrueReadFile(hFile, lpBuf, nRead, pRead, lpOv);
}

/** @brief 파일 포인터 제어: 가상 핸들의 현재 오프셋을 조정합니다. */
BOOL WINAPI Hooked_SetFilePointerEx(HANDLE hFile, LARGE_INTEGER liMove,
                                    PLARGE_INTEGER lpNew, DWORD dwMethod) {
    auto& engine = PhantomVfs::GetVfsEngine();
    std::shared_ptr<PhantomVfs::VirtualHandle> vh = nullptr;
    {
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        auto& handleMap = engine.GetHandleMap();
        auto it = handleMap.find(hFile);
        if (it != handleMap.end())
            vh = it->second;
    }
    if (vh) {
        size_t savedSize = vh->file->currentSize.load();
        switch (dwMethod) {
        case FILE_BEGIN:
            vh->position = liMove.QuadPart;
            break;
        case FILE_CURRENT:
            vh->position += liMove.QuadPart;
            break;
        case FILE_END:
            vh->position = (long long)savedSize + liMove.QuadPart;
            break;
        }
        if (vh->position < 0)
            vh->position = 0;
        if (lpNew)
            lpNew->QuadPart = vh->position;
        return TRUE;
    }
    return TrueSetFilePointerEx(hFile, liMove, lpNew, dwMethod);
}

/** @brief 파일 크기 조회: 가상 메모리에 기록된 데이터 크기를 반환합니다. */
BOOL WINAPI Hooked_GetFileSizeEx(HANDLE hFile, PLARGE_INTEGER lpSize) {
    auto& engine = PhantomVfs::GetVfsEngine();
    std::shared_ptr<PhantomVfs::VirtualHandle> vh = nullptr;
    {
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        auto& handleMap = engine.GetHandleMap();
        auto it = handleMap.find(hFile);
        if (it != handleMap.end())
            vh = it->second;
    }
    if (vh) {
        lpSize->QuadPart = (long long)vh->file->currentSize.load();
        return TRUE;
    }
    return TrueGetFileSizeEx(hFile, lpSize);
}

/** @brief 파일 타입 조회: 가상 핸들도 일반 디스크 파일처럼 취급되도록 합니다. */
DWORD WINAPI Hooked_GetFileType(HANDLE hFile) {
    auto& engine = PhantomVfs::GetVfsEngine();
    std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
    if (engine.GetHandleMap().count(hFile))
        return FILE_TYPE_DISK;
    return TrueGetFileType(hFile);
}

/** @brief 파일 속성 조회 가로채기: 비인가 프로세스에게 파일 존재를 은폐합니다. */
DWORD WINAPI Hooked_GetFileAttributesW(LPCWSTR lpFileName) {
    auto& engine = PhantomVfs::GetVfsEngine();
    
    if (engine.IsProtectedPath(lpFileName)) {
        if (!engine.IsTrustedProcess()) {
            VFS_LOG_WARN(L"[Security Gateway] 비인가 프로세스 속성 조회 차단: %s", lpFileName);
            SetLastError(ERROR_FILE_NOT_FOUND);
            return INVALID_FILE_ATTRIBUTES;
        }
    }

    if (engine.IsTarget(lpFileName)) {
        std::wstring path = engine.NormalizePath(lpFileName);
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        if (engine.GetStorage().count(path)) {
            DWORD real = TrueGetFileAttributesW(lpFileName);
            if (real != INVALID_FILE_ATTRIBUTES)
                return real;
            if (IsMipTempUuidDwgPath(lpFileName)) {
                const std::wstring bak = MipUuidDwgToBakPath(lpFileName);
                if (!bak.empty() && TrueGetFileAttributesW(bak.c_str()) != INVALID_FILE_ATTRIBUTES)
                    return FILE_ATTRIBUTE_NORMAL;
            }
            return INVALID_FILE_ATTRIBUTES;
        }
    }
    return TrueGetFileAttributesW(lpFileName);
}

/** @brief 파일 이동/이름변경: 가상 저장소 내의 경로 키값을 업데이트합니다. */
BOOL WINAPI Hooked_MoveFileExW(LPCWSTR lpSrc, LPCWSTR lpDst, DWORD dwFlags) {
    auto& engine = PhantomVfs::GetVfsEngine();
    TouchZwcadSaveSidecarTick(engine, lpSrc);
    TouchZwcadSaveSidecarTick(engine, lpDst);

    const bool savePassthrough =
        IsInZwcadSaveWindow() &&
        (IsMipTempSavePhysicalPath(lpSrc) || IsMipTempSavePhysicalPath(lpDst));

    if (savePassthrough) {
        VFS_LOG_INFO(L"[SAVE-IO] SaveExposed passthrough MoveFileExW: %s -> %s (0x%X)",
                     lpSrc ? lpSrc : L"NULL", lpDst ? lpDst : L"NULL", dwFlags);
        const BOOL ok = TrueMoveFileExW(lpSrc, lpDst, dwFlags);
        if (ok && lpSrc && lpDst) {
            const std::wstring srcPath = engine.NormalizePath(lpSrc);
            const std::wstring dstPath = engine.NormalizePath(lpDst);
            UpdateStorageKeyAfterPhysicalMove(engine, srcPath, dstPath);
            if (IsMipTempUuidDwgPath(lpDst))
                MarkSaveExposedForPath(engine, lpDst);
            std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
            auto it = engine.GetStorage().find(dstPath);
            if (it != engine.GetStorage().end() && it->second)
                SyncVirtualFileFromDiskIfPresent(it->second);
        }
        return ok;
    }

    std::wstring srcPath = engine.NormalizePath(lpSrc);
    std::wstring dstPath = engine.NormalizePath(lpDst);
    VFS_LOG_INFO(L"파일 이동 요청: %s -> %s (Flags: 0x%X)", lpSrc, lpDst, dwFlags);

    if (engine.IsManifestingPath(srcPath.c_str()))
        engine.ReleaseKeeperByPath(srcPath);

    if (engine.IsTarget(srcPath.c_str()) || engine.IsTarget(dstPath.c_str())) {
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        auto& storage = engine.GetStorage();
        if (storage.count(srcPath)) {
            const BOOL ok = TrueMoveFileExW(lpSrc, lpDst, dwFlags);
            if (ok) {
                VFS_LOG_INFO(L"내부 VFS 경로 리디렉션(실물 이동 동반): %s -> %s", lpSrc, lpDst);
                storage[dstPath] = storage[srcPath];
                storage[dstPath]->path = dstPath;
                storage.erase(srcPath);
            }
            return ok;
        }
    }
    return TrueMoveFileExW(lpSrc, lpDst, dwFlags);
}

/** @brief 파일 교체: 이동 작업과 동일하게 핸들 해제를 우선 처리합니다. */
BOOL WINAPI Hooked_ReplaceFileW(LPCWSTR lpDst, LPCWSTR lpSrc, LPCWSTR lpBak,
                                DWORD dwFlags, LPVOID lpRes1, LPVOID lpRes2) {
    auto& engine = PhantomVfs::GetVfsEngine();
    TouchZwcadSaveSidecarTick(engine, lpSrc);
    TouchZwcadSaveSidecarTick(engine, lpDst);
    TouchZwcadSaveSidecarTick(engine, lpBak);

    const bool savePassthrough =
        IsInZwcadSaveWindow() &&
        (IsMipTempSavePhysicalPath(lpSrc) || IsMipTempSavePhysicalPath(lpDst) ||
         IsMipTempSavePhysicalPath(lpBak));

    if (savePassthrough) {
        VFS_LOG_INFO(L"[SAVE-IO] SaveExposed passthrough ReplaceFileW: dst=%s src=%s",
                     lpDst ? lpDst : L"NULL", lpSrc ? lpSrc : L"NULL");
        return TrueReplaceFileW(lpDst, lpSrc, lpBak, dwFlags, lpRes1, lpRes2);
    }

    std::wstring srcPath = engine.NormalizePath(lpSrc);
    if (engine.IsManifestingPath(srcPath.c_str()))
        engine.ReleaseKeeperByPath(srcPath);
    return TrueReplaceFileW(lpDst, lpSrc, lpBak, dwFlags, lpRes1, lpRes2);
}

/** @brief 파일 삭제 가로채기: 보호(Shield) 로직과 가상 파일 삭제를 관리합니다. */
BOOL WINAPI Hooked_DeleteFileW(LPCWSTR lpFileName) {
    auto& engine = PhantomVfs::GetVfsEngine();
    
    VFS_LOG_DEBUG(L"[TRACE] DeleteFileW: %s", lpFileName ? lpFileName : L"NULL");

    // 1. 임시 경로 보호 로직
    if (engine.IsManifestingPath(lpFileName)) {
        std::wstring path = engine.NormalizePath(lpFileName);
        LPCWSTR ext = PathFindExtensionW(path.c_str());
        bool isExclusiveTarget =
            (_wcsicmp(ext, L".dwg") == 0 || _wcsicmp(ext, L".pdf") == 0 ||
             _wcsicmp(ext, L".dwl") == 0 || _wcsicmp(ext, L".dwl2") == 0 ||
             _wcsicmp(ext, L".dsd") == 0 || _wcsicmp(ext, L".xml") == 0);
        if (isExclusiveTarget) {
            VFS_LOG_INFO(L"선택적 바이패스 (잠금 없음): %s", lpFileName);
            return TRUE;
        }
        // 2. 외부 프로세스가 삭제를 시도할 경우 Keeper 핸들로 방어
        DWORD ownerPid = engine.ExtractPIDFromPath(path);
        if (ownerPid > 0) {
            HANDLE hKeeper = TrueCreateFilePassthrough(lpFileName, READ_CONTROL,
                                             FILE_SHARE_READ | FILE_SHARE_WRITE |
                                                 FILE_SHARE_DELETE,
                                             NULL, OPEN_EXISTING, 0, NULL);
            if (hKeeper != INVALID_HANDLE_VALUE) {
                std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
                engine.GetKeeperList().push_back(
                    { hKeeper, ownerPid, path, GetTickCount64() });
                VFS_LOG_INFO(L"책임 방어막(Shield) 가동 (PID: %d): %s", ownerPid,
                           lpFileName);
            } else {
                VFS_LOG_INFO(L"방어막 생성 실패 (이미 접근 불가능): %s",
                           lpFileName);
            }
        }
        return TRUE;
    }
    
    // 3. 가상 저장소 내의 파일 삭제
    std::wstring path = engine.NormalizePath(lpFileName);
    if (engine.IsTarget(path.c_str())) {
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        auto& storage = engine.GetStorage();
        if (storage.count(path)) {
            VFS_LOG_INFO(L"가상 고스트 기화 완료: %s", path.c_str());
            storage.erase(path);
        }
    }
    return TrueDeleteFileW(lpFileName);
}

/** @brief 파일 복사 가로채기: 비인가 프로세스의 복사 시도를 차단합니다. */
BOOL WINAPI Hooked_CopyFileW(LPCWSTR lpExistingFileName, LPCWSTR lpNewFileName, BOOL bFailIfExists) {
    auto& engine = PhantomVfs::GetVfsEngine();
    if (!engine.CheckAccessPermission(lpExistingFileName)) {
        VFS_LOG_WARN(L"[Security Guard] COPY DENIED (CopyFileW): %s", lpExistingFileName);
        SetLastError(ERROR_FILE_NOT_FOUND); 
        return FALSE;
    }
    TouchZwcadSaveSidecarTick(engine, lpExistingFileName);
    TouchZwcadSaveSidecarTick(engine, lpNewFileName);
    EndSaveExposedIfExpired(engine);

    if (lpNewFileName && IsMipTempUuidDwgPath(lpNewFileName)) {
        std::wstring path = engine.NormalizePath(lpNewFileName);
        std::shared_ptr<PhantomVfs::VirtualFile> vf;
        {
            std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
            auto it = engine.GetStorage().find(path);
            if (it != engine.GetStorage().end())
                vf = it->second;
        }
        if (vf) {
            EnsureMipUuidDwgOnDiskForSave(vf);
            VFS_LOG_INFO(L"[SAVE-IO] CopyFileW SaveExposed target: %s", lpNewFileName);
        }
    }
    return TrueCopyFileW(lpExistingFileName, lpNewFileName, bFailIfExists);
}

/** @brief 확장 파일 복사 가로채기: 쉘 대행 복사 등을 포함한 모든 복사 경로 차단. */
BOOL WINAPI Hooked_CopyFileExW(LPCWSTR lpExistingFileName, LPCWSTR lpNewFileName,
                               LPPROGRESS_ROUTINE lpProgressRoutine, LPVOID lpData,
                               LPBOOL pbCancel, DWORD dwCopyFlags) {
    auto& engine = PhantomVfs::GetVfsEngine();
    if (!engine.CheckAccessPermission(lpExistingFileName)) {
        VFS_LOG_WARN(L"[Security Guard] COPY DENIED (CopyFileExW): %s", lpExistingFileName);
        SetLastError(ERROR_FILE_NOT_FOUND); 
        return FALSE;
    }
    return TrueCopyFileExW(lpExistingFileName, lpNewFileName, lpProgressRoutine, lpData, pbCancel, dwCopyFlags);
}

/** @brief 파일 이동 가로채기: 파일을 빼돌리려는 이동 시도 차단. */
BOOL WINAPI Hooked_MoveFileW(LPCWSTR lpExistingFileName, LPCWSTR lpNewFileName) {
    auto& engine = PhantomVfs::GetVfsEngine();
    if (!engine.CheckAccessPermission(lpExistingFileName)) {
        VFS_LOG_WARN(L"[Security Guard] MOVE DENIED (MoveFileW): %s", lpExistingFileName);
        SetLastError(ERROR_FILE_NOT_FOUND);
        return FALSE;
    }
    TouchZwcadSaveSidecarTick(engine, lpExistingFileName);
    TouchZwcadSaveSidecarTick(engine, lpNewFileName);

    const bool savePassthrough =
        IsInZwcadSaveWindow() &&
        (IsMipTempSavePhysicalPath(lpExistingFileName) ||
         IsMipTempSavePhysicalPath(lpNewFileName));

    if (savePassthrough) {
        VFS_LOG_INFO(L"[SAVE-IO] SaveExposed passthrough MoveFileW: %s -> %s",
                     lpExistingFileName ? lpExistingFileName : L"NULL",
                     lpNewFileName ? lpNewFileName : L"NULL");
        const BOOL ok = TrueMoveFileW(lpExistingFileName, lpNewFileName);
        if (ok && lpExistingFileName && lpNewFileName) {
            const std::wstring srcPath = engine.NormalizePath(lpExistingFileName);
            const std::wstring dstPath = engine.NormalizePath(lpNewFileName);
            UpdateStorageKeyAfterPhysicalMove(engine, srcPath, dstPath);
            if (IsMipTempUuidDwgPath(lpNewFileName))
                MarkSaveExposedForPath(engine, lpNewFileName);
        }
        return ok;
    }
    return TrueMoveFileW(lpExistingFileName, lpNewFileName);
}

/** @brief 메모리 맵핑 가로채기: 가상 파일의 맵핑 핸들을 가로채어 전달합니다. */
HANDLE WINAPI Hooked_CreateFileMappingW(HANDLE hFile,
                                        LPSECURITY_ATTRIBUTES lpSA,
                                        DWORD flProtect, DWORD dwMaxHigh,
                                        DWORD dwMaxLow, LPCWSTR lpName) {
    auto& engine = PhantomVfs::GetVfsEngine();
    std::shared_ptr<PhantomVfs::VirtualHandle> vh = nullptr;
    {
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        auto& handleMap = engine.GetHandleMap();
        auto it = handleMap.find(hFile);
        if (it != handleMap.end())
            vh = it->second;
    }
    if (vh) {
        std::lock_guard<std::recursive_mutex> lock(vh->file->fileMtx);
        return vh->file->hMapping;
    }
    return TrueCreateFileMappingW(hFile, lpSA, flProtect, dwMaxHigh, dwMaxLow,
                                  lpName);
}

extern "C" {

void WINAPI ArmZwcadSaveWindow() {
    g_lastZwcadSaveSidecarTick.store(GetTickCount64(), std::memory_order_relaxed);
    VFS_LOG_INFO(L"[SAVE-IO] ArmZwcadSaveWindow (managed)");
}

BOOL WINAPI PrepareMipTempDwgForCloseCommit(LPCWSTR lpMipUuidDwgPath) {
    if (!lpMipUuidDwgPath || !lpMipUuidDwgPath[0])
        return FALSE;
    if (!IsMipTempUuidDwgPath(lpMipUuidDwgPath))
        return FALSE;

    auto& engine = PhantomVfs::GetVfsEngine();
    const std::wstring path = engine.NormalizePath(lpMipUuidDwgPath);
    std::shared_ptr<PhantomVfs::VirtualFile> vf;
    {
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        auto it = engine.GetStorage().find(path);
        if (it != engine.GetStorage().end())
            vf = it->second;
    }

    BOOL ok = FALSE;
    if (vf)
        ok = EnsureMipCommitFileFromDiskOrBak(vf) ? TRUE : FALSE;
    else
        ok = EnsureMipCommitPathOnDisk(engine, lpMipUuidDwgPath) ? TRUE : FALSE;

    if (ok) {
        g_mipCloseCommitPathNormalized = path;
        g_mipCloseCommitUntil.store(GetTickCount64() + MIP_CLOSE_COMMIT_WINDOW_MS,
                                    std::memory_order_relaxed);
        VFS_LOG_INFO(L"[CLOSE] PrepareMipTempDwgForCloseCommit OK: %s", path.c_str());
    } else {
        VFS_LOG_ERR(L"!!! [CLOSE] PrepareMipTempDwgForCloseCommit failed: %s",
                    path.c_str());
    }
    return ok;
}

BOOL WINAPI FinalizeMipTempDwgAfterCadOpen(LPCWSTR lpMipUuidDwgPath) {
    if (!lpMipUuidDwgPath || !lpMipUuidDwgPath[0])
        return FALSE;
    auto& engine = PhantomVfs::GetVfsEngine();
    const BOOL ok =
        VaporizeMipUuidDwgCanonicalDiskByPath(engine, lpMipUuidDwgPath, L"CadOpen", true)
            ? TRUE
            : FALSE;
    if (!ok)
        VFS_LOG_DEBUG(L"[OPEN] L1 FinalizeMipTempDwgAfterCadOpen skipped or no storage: %s",
                      lpMipUuidDwgPath);
    return ok;
}

} // extern "C"
