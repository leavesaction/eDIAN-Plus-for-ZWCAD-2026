#include "DetoursInterceptor.h"
#include "IVfsEngine.h"
#include <psapi.h>
#include <shlwapi.h>

#pragma comment(lib, "Shlwapi.lib")

// ============================================================================
// [PHANTOM VFS v17.3] 후킹 인터셉터 레이어 - 매니저 및 초기화 구현부
// ============================================================================

// ----------------------------------------------------------------------------
// [전역 원본 API 함수 포인터 변수 정의 및 초기화]
// ----------------------------------------------------------------------------
PFN_CreateFileW TrueCreateFileW = CreateFileW;
PFN_ReadFile TrueReadFile = ReadFile;
PFN_WriteFile TrueWriteFile = WriteFile;
PFN_SetFilePointerEx TrueSetFilePointerEx = SetFilePointerEx;
PFN_CloseHandle TrueCloseHandle = CloseHandle;
PFN_GetFileSizeEx TrueGetFileSizeEx = GetFileSizeEx;
PFN_GetFileType TrueGetFileType = GetFileType;
PFN_GetFileInformationByHandleEx TrueGetFileInformationByHandleEx = GetFileInformationByHandleEx;
PFN_CreateFileMappingW TrueCreateFileMappingW = CreateFileMappingW;
PFN_MoveFileW TrueMoveFileW = MoveFileW;
PFN_MoveFileExW TrueMoveFileExW = MoveFileExW;
PFN_CopyFileW TrueCopyFileW = CopyFileW;
PFN_CopyFileExW TrueCopyFileExW = CopyFileExW;
PFN_ReplaceFileW TrueReplaceFileW = ReplaceFileW;
PFN_DeleteFileW TrueDeleteFileW = DeleteFileW;
PFN_GetFileAttributesW TrueGetFileAttributesW = GetFileAttributesW;
PFN_SetFileAttributesW TrueSetFileAttributesW = SetFileAttributesW;
PFN_RemoveDirectoryW TrueRemoveDirectoryW = RemoveDirectoryW;
PFN_ExitProcess TrueExitProcess = ExitProcess;
PFN_TerminateProcess TrueTerminateProcess = TerminateProcess;
PFN_CreateProcessW TrueCreateProcessW = CreateProcessW;

static PVOID g_LdrCookie = NULL;

struct HookEntry {
    const char* szFunc;
    PVOID pNew;
    PVOID pOld;
};

static const HookEntry g_HookEntries[] = {
    { "CreateFileW", (PVOID)Hooked_CreateFileW, (PVOID)TrueCreateFileW },
    { "RemoveDirectoryW", (PVOID)Hooked_RemoveDirectoryW, (PVOID)TrueRemoveDirectoryW },
    { "GetFileAttributesW", (PVOID)Hooked_GetFileAttributesW, (PVOID)TrueGetFileAttributesW },
    { "MoveFileExW", (PVOID)Hooked_MoveFileExW, (PVOID)TrueMoveFileExW },
    { "MoveFileW", (PVOID)Hooked_MoveFileW, (PVOID)TrueMoveFileW },
    { "CopyFileW", (PVOID)Hooked_CopyFileW, (PVOID)TrueCopyFileW },
    { "CopyFileExW", (PVOID)Hooked_CopyFileExW, (PVOID)TrueCopyFileExW },
    { "ReplaceFileW", (PVOID)Hooked_ReplaceFileW, (PVOID)TrueReplaceFileW },
    { "DeleteFileW", (PVOID)Hooked_DeleteFileW, (PVOID)TrueDeleteFileW },
    { "ReadFile", (PVOID)Hooked_ReadFile, (PVOID)TrueReadFile },
    { "WriteFile", (PVOID)Hooked_WriteFile, (PVOID)TrueWriteFile },
    { "SetFilePointerEx", (PVOID)Hooked_SetFilePointerEx, (PVOID)TrueSetFilePointerEx },
    { "GetFileSizeEx", (PVOID)Hooked_GetFileSizeEx, (PVOID)TrueGetFileSizeEx },
    { "GetFileType", (PVOID)Hooked_GetFileType, (PVOID)TrueGetFileType },
    { "CloseHandle", (PVOID)Hooked_CloseHandle, (PVOID)TrueCloseHandle },
    { "CreateFileMappingW", (PVOID)Hooked_CreateFileMappingW, (PVOID)TrueCreateFileMappingW },
    { "ExitProcess", (PVOID)Hooked_ExitProcess, (PVOID)TrueExitProcess },
    { "TerminateProcess", (PVOID)Hooked_TerminateProcess, (PVOID)TrueTerminateProcess },
    { "CreateProcessW", (PVOID)Hooked_CreateProcessW, (PVOID)TrueCreateProcessW }
};

static void PatchModuleFunctions(HMODULE hMod) {
    if (!hMod || hMod == GetModuleHandleW(L"eDIAN.Hook.Native.dll"))
        return;

    PIMAGE_DOS_HEADER pDos = (PIMAGE_DOS_HEADER)hMod;
    if (pDos->e_magic != IMAGE_DOS_SIGNATURE)
        return;

    __try {
        PIMAGE_NT_HEADERS pNt = (PIMAGE_NT_HEADERS)((PBYTE)hMod + pDos->e_lfanew);
        DWORD rva = pNt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress;
        if (rva == 0)
            return;

        PIMAGE_IMPORT_DESCRIPTOR pImp = (PIMAGE_IMPORT_DESCRIPTOR)((PBYTE)hMod + rva);
        for (; pImp->Name; pImp++) {
            const char* szName = (const char*)((PBYTE)hMod + pImp->Name);
            if (StrStrIA(szName, "kernel32") != nullptr ||
                StrStrIA(szName, "kernelbase") != nullptr ||
                StrStrIA(szName, "ntdll") != nullptr ||
                StrStrIA(szName, "api-ms-win-core-file") != nullptr ||
                StrStrIA(szName, "api-ms-win-core-processthreads") != nullptr) {

                PIMAGE_THUNK_DATA pThunk = (PIMAGE_THUNK_DATA)((PBYTE)hMod + pImp->FirstThunk);
                for (; pThunk->u1.Function; pThunk++) {
                    PVOID pFuncAddr = (PVOID)pThunk->u1.Function;
                    for (const auto& entry : g_HookEntries) {
                        if (pFuncAddr == entry.pOld) {
                            DWORD dwOld;
                            if (VirtualProtect(&pThunk->u1.Function, sizeof(PVOID), PAGE_READWRITE, &dwOld)) {
                                pThunk->u1.Function = (ULONGLONG)entry.pNew;
                                VirtualProtect(&pThunk->u1.Function, sizeof(PVOID), dwOld, &dwOld);
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        // Safe bypass for packed/protected modules
    }
}

extern "C" {
/** @brief 현재 로드된 모든 모듈의 IAT를 패칭하여 후킹을 설치합니다. */
__declspec(dllexport) BOOL WINAPI InstallHooks() {
    static std::mutex installMtx;
    std::lock_guard<std::mutex> lock(installMtx);

    HMODULE hMods[2048];
    DWORD cbNeeded;
    if (EnumProcessModules(GetCurrentProcess(), hMods, sizeof(hMods), &cbNeeded)) {
        DWORD numModules = cbNeeded / sizeof(HMODULE);
        for (DWORD i = 0; i < numModules; i++) {
            PatchModuleFunctions(hMods[i]);
        }
    }
    return TRUE;
}

/** @brief 후킹 상태를 재동기화(새로 로드된 DLL 포함) 합니다. */
__declspec(dllexport) void WINAPI SyncHooks() { InstallHooks(); }

/** @brief DLL이 로드될 때마다 후킹을 자동으로 설치하기 위한 콜백 */
VOID NTAPI LdrDllNotificationCallback(ULONG Reason,
                                      PLDR_DLL_NOTIFICATION_DATA Data,
                                      PVOID Context) {
    if (Reason == 1 && Data && Data->Loaded.DllBase) { // 1 = LDR_DLL_NOTIFICATION_REASON_LOADED
        HMODULE hMod = (HMODULE)Data->Loaded.DllBase;
        PatchModuleFunctions(hMod);
    }
}

/** @brief VFS 엔진을 초기화하고 DLL 알림 콜백을 등록합니다. */
__declspec(dllexport) void WINAPI InitializeVfs(LPCWSTR lpTempPath,
                                                LPCWSTR lpLogPath,
                                                int nLogLevel,
                                                LPCWSTR lpConfigString,
                                                FnVfsOpenCallback openCb,
                                                FnVfsCloseCallback closeCb) {
    PhantomVfs::GetVfsEngine().Initialize(lpTempPath, lpLogPath, nLogLevel, lpConfigString, openCb, closeCb);
    HMODULE hNtdll = GetModuleHandleW(L"ntdll.dll");
    if (hNtdll) {
        auto pRegister = (LdrRegisterDllNotification_t)GetProcAddress(
            hNtdll, "LdrRegisterDllNotification");
        if (pRegister)
            pRegister(0, LdrDllNotificationCallback, NULL, &g_LdrCookie);
    }
}

/** @brief 후킹 알림 콜백을 등록 해제합니다. */
__declspec(dllexport) void WINAPI UninstallHooks() {
    if (g_LdrCookie) {
        HMODULE hNtdll = GetModuleHandleW(L"ntdll.dll");
        if (hNtdll) {
            auto pUnregister = (LdrUnregisterDllNotification_t)GetProcAddress(
                hNtdll, "LdrUnregisterDllNotification");
            if (pUnregister)
                pUnregister(g_LdrCookie);
        }
    }
}
}
