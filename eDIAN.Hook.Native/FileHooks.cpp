#include "DetoursInterceptor.h"
#include "IVfsEngine.h"
#include "VfsLogger.h"
#include <algorithm>
#include <shlwapi.h>

#pragma comment(lib, "Shlwapi.lib")

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
    auto& engine = PhantomVfs::GetVfsEngine();
    
    // [Phase 3: API Tracing] 상세 호출 기록
    VFS_LOG_DEBUG(L"[TRACE] CreateFileW: %s (Access: 0x%08X, Creation: %d)", 
               lpFileName ? lpFileName : L"NULL", dwAccess, dwCreation);

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
        return TrueCreateFileW(lpFileName, dwAccess, dwShare, lpSA, dwCreation,
                               secureFlags, hTemplate);
    }

    // 2. 가상화 대상 파일(DWG 등) 접근 시
    if (engine.IsTarget(lpFileName)) {
        std::wstring path = engine.NormalizePath(lpFileName);
        std::shared_ptr<PhantomVfs::VirtualFile> vf = nullptr;

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
            if (TrueGetFileAttributesW(lpFileName) != INVALID_FILE_ATTRIBUTES) {
                // 실물 파일이 있으면 데이터를 메모리로 흡수
                HANDLE hReal = TrueCreateFileW(lpFileName, GENERIC_READ,
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
                } else {
                    // 경쟁 상태에서 다른 스레드가 먼저 등록했다면 이를 사용
                    vf = it->second;
                }
                
                // 로딩 목록에서 제거 및 대기 중인 스레드 깨우기
                engine.GetLoadingPaths().erase(path);
                engine.GetLoadingCV().notify_all();
            }
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
                        HANDLE hManifest = TrueCreateFileW(
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
                                    TrueCreateFileW(lpFileName, dwAccess, dwShare, lpSA,
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

        // 4. 고스트 핸들 생성: 실물 파일 대신 유니크한 임시 파일로 우회
        std::wstring uniqueGhost = engine.GetUniqueGhostPath(path);
        DWORD creation = (dwCreation == OPEN_EXISTING) ? OPEN_ALWAYS : dwCreation;
        // [VFS Phase 9.7] 고스트 파일도 독점 점유하여 외부 유출 원천 봉쇄
        HANDLE hSurrogate = TrueCreateFileW(
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

    HANDLE hResult = TrueCreateFileW(lpFileName, dwAccess, finalShare, lpSA, dwCreation,
                           dwFlags, hTemplate);

    if (hResult == INVALID_HANDLE_VALUE) {
        DWORD err = GetLastError();
        if (err != ERROR_FILE_NOT_FOUND && err != ERROR_PATH_NOT_FOUND) {
            VFS_LOG_DEBUG(L"[TRACE] CreateFileW FAILED: %s (Error: %d)", lpFileName ? lpFileName : L"NULL", err);
        }
    }
    return hResult;
}

/** @brief 파일 생성 가로채기 래퍼: SEH 보호막을 제공합니다. */
HANDLE WINAPI Hooked_CreateFileW(LPCWSTR lpFileName, DWORD dwAccess,
                                 DWORD dwShare, LPSECURITY_ATTRIBUTES lpSA,
                                 DWORD dwCreation, DWORD dwFlags,
                                 HANDLE hTemplate) {
    __try {
        return Internal_CreateFileW(lpFileName, dwAccess, dwShare, lpSA,
                                    dwCreation, dwFlags, hTemplate);
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        auto& engine = PhantomVfs::GetVfsEngine();
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

    // 모든 핸들이 닫히면 최종 내용을 실물 파일로 커밋
    if (commitTarget) {
        std::lock_guard<std::mutex> lock(commitTarget->fileMtx);
        LPCWSTR ext = PathFindExtensionW(commitTarget->path.c_str());
        bool isDwg = (_wcsicmp(ext, L".dwg") == 0);
        ULONGLONG now = GetTickCount64();
        if (isDwg && (commitTarget->isModified ||
                      (now - commitTarget->lastVaporizedTime > 500))) {
            HANDLE hOrig = TrueCreateFileW(commitTarget->path.c_str(), GENERIC_WRITE,
                                           FILE_SHARE_READ, NULL, CREATE_ALWAYS,
                                           FILE_ATTRIBUTE_NORMAL, NULL);
            if (hOrig != INVALID_HANDLE_VALUE) {
                size_t sizeToSave = commitTarget->currentSize.load();
                VFS_LOG_INFO(L"최종 변경사항 디스크 커밋 시작 (%llu bytes): %s",
                           sizeToSave, commitTarget->path.c_str());

                bool writeSuccess = true;
                const size_t CHUNK_SIZE = 16 * 1024 * 1024; // 16MB 청크 크기
                const size_t SYNC_WRITE_THRESHOLD = 20 * 1024 * 1024; // 20MB 이하는 일시 쓰기

                if (sizeToSave <= SYNC_WRITE_THRESHOLD) {
                    DWORD written = 0;
                    if (!TrueWriteFile(hOrig, commitTarget->pBase, (DWORD)sizeToSave, &written, NULL) || written != sizeToSave) {
                        writeSuccess = false;
                    }
                } else {
                    size_t offset = 0;
                    while (offset < sizeToSave) {
                        size_t bytesToWrite = (std::min)(CHUNK_SIZE, sizeToSave - offset);
                        DWORD written = 0;
                        if (!TrueWriteFile(hOrig, (BYTE*)commitTarget->pBase + offset, (DWORD)bytesToWrite, &written, NULL) || written != bytesToWrite) {
                            writeSuccess = false;
                            VFS_LOG_ERR(L"!!! 대용량 분할 쓰기 실패 (오프셋: %llu, 크기: %llu)", offset, bytesToWrite);
                            break;
                        }
                        offset += bytesToWrite;
                        SwitchToThread();
                    }
                }

                if (writeSuccess) {
                    commitTarget->isModified = false;
                    VFS_LOG_INFO(L"최종 변경사항 디스크 커밋 완료: %s", commitTarget->path.c_str());
                } else {
                    VFS_LOG_ERR(L"!!! 최종 변경사항 디스크 커밋 실패 (보존 조치): %s", commitTarget->path.c_str());
                }
                TrueCloseHandle(hOrig);
            }
        }
        if (commitTarget->hKeeper != INVALID_HANDLE_VALUE) {
            TrueCloseHandle(commitTarget->hKeeper);
            commitTarget->hKeeper = INVALID_HANDLE_VALUE;
        }
        VFS_LOG_INFO(L"[VFS] 가상 파일 참조 해제 및 Keeper 폐쇄: %s", commitTarget->path.c_str());
    }
    return TrueCloseHandle(hFile);
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
        return TrueCloseHandle(hFile);
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
        if (engine.GetStorage().count(path))
            return FILE_ATTRIBUTE_NORMAL;
    }
    return TrueGetFileAttributesW(lpFileName);
}

/** @brief 파일 이동/이름변경: 가상 저장소 내의 경로 키값을 업데이트합니다. */
BOOL WINAPI Hooked_MoveFileExW(LPCWSTR lpSrc, LPCWSTR lpDst, DWORD dwFlags) {
    auto& engine = PhantomVfs::GetVfsEngine();
    std::wstring srcPath = engine.NormalizePath(lpSrc);
    std::wstring dstPath = engine.NormalizePath(lpDst);
    VFS_LOG_INFO(L"파일 이동 요청: %s -> %s (Flags: 0x%X)", lpSrc, lpDst, dwFlags);

    if (engine.IsManifestingPath(srcPath.c_str())) {
        engine.ReleaseKeeperByPath(srcPath);
    }

    if (engine.IsTarget(srcPath.c_str()) || engine.IsTarget(dstPath.c_str())) {
        std::lock_guard<std::recursive_mutex> lock(engine.GetRegistryMutex());
        auto& storage = engine.GetStorage();
        if (storage.count(srcPath)) {
            VFS_LOG_INFO(L"내부 VFS 경로 리디렉션 수행.");
            storage[dstPath] = storage[srcPath];
            storage[dstPath]->path = dstPath;
            storage.erase(srcPath);
            return TRUE;
        }
    }
    return TrueMoveFileExW(lpSrc, lpDst, dwFlags);
}

/** @brief 파일 교체: 이동 작업과 동일하게 핸들 해제를 우선 처리합니다. */
BOOL WINAPI Hooked_ReplaceFileW(LPCWSTR lpDst, LPCWSTR lpSrc, LPCWSTR lpBak,
                                DWORD dwFlags, LPVOID lpRes1, LPVOID lpRes2) {
    auto& engine = PhantomVfs::GetVfsEngine();
    std::wstring srcPath = engine.NormalizePath(lpSrc);
    if (engine.IsManifestingPath(srcPath.c_str())) {
        engine.ReleaseKeeperByPath(srcPath);
    }
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
            HANDLE hKeeper = TrueCreateFileW(lpFileName, READ_CONTROL,
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
        std::lock_guard<std::mutex> lock(vh->file->fileMtx);
        return vh->file->hMapping;
    }
    return TrueCreateFileMappingW(hFile, lpSA, flProtect, dwMaxHigh, dwMaxLow,
                                  lpName);
}
