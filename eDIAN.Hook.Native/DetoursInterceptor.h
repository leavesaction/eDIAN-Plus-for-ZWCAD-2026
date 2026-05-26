#pragma once
#include <windows.h>
#include <winternl.h>
#include <atomic>
#include <vector>
#include <mutex>
#include <string>

/**
 * @brief Managed Callback Signatures
 * These must match the [UnmanagedFunctionPointer] delegates in C#.
 */
typedef HANDLE (WINAPI *FnVfsOpenCallback)(LPCWSTR lpFileName, DWORD dwAccess, DWORD dwShare);
typedef void   (WINAPI *FnVfsCloseCallback)(HANDLE hFile);

// --- Exported Functions for C# ---
extern "C" {
    /**
     * @brief Initialize the VFS interceptor with C# callbacks.
     */
    __declspec(dllexport) void WINAPI InitializeVfs(LPCWSTR lpTempPath, LPCWSTR lpLogPath, int nLogLevel, LPCWSTR lpConfigString, FnVfsOpenCallback openCb, FnVfsCloseCallback closeCb);
    
    /**
     * @brief Install API hooks using IAT patching.
     */
    __declspec(dllexport) BOOL WINAPI InstallHooks();

    /**
     * @brief Uninstall all API hooks.
     */
    __declspec(dllexport) void WINAPI UninstallHooks();

    /**
     * @brief Trigger a re-synchronization of hooks (called periodically or on demand).
     */
    __declspec(dllexport) void WINAPI SyncHooks();
}

// --- Internal NT Native structures for Ldr notification ---
typedef struct _LDR_DLL_LOADED_NOTIFICATION_DATA {
    ULONG Flags;
    PCUNICODE_STRING FullDllName;
    PCUNICODE_STRING BaseDllName;
    PVOID DllBase;
    ULONG SizeOfImage;
} LDR_DLL_LOADED_NOTIFICATION_DATA, *PLDR_DLL_LOADED_NOTIFICATION_DATA;

typedef struct _LDR_DLL_UNLOADED_NOTIFICATION_DATA {
    ULONG Flags;
    PCUNICODE_STRING FullDllName;
    PCUNICODE_STRING BaseDllName;
    PVOID DllBase;
    ULONG SizeOfImage;
} LDR_DLL_UNLOADED_NOTIFICATION_DATA, *PLDR_DLL_UNLOADED_NOTIFICATION_DATA;

typedef union _LDR_DLL_NOTIFICATION_DATA {
    LDR_DLL_LOADED_NOTIFICATION_DATA Loaded;
    LDR_DLL_UNLOADED_NOTIFICATION_DATA Unloaded;
} LDR_DLL_NOTIFICATION_DATA, *PLDR_DLL_NOTIFICATION_DATA;

typedef VOID (NTAPI *PLDR_DLL_NOTIFICATION_FUNCTION)(
    ULONG NotificationReason,
    PLDR_DLL_NOTIFICATION_DATA NotificationData,
    PVOID Context
);

typedef NTSTATUS (NTAPI *LdrRegisterDllNotification_t)(
    ULONG Flags,
    PLDR_DLL_NOTIFICATION_FUNCTION NotificationFunction,
    PVOID Context,
    PVOID *Cookie
);

typedef NTSTATUS (NTAPI *LdrUnregisterDllNotification_t)(
    PVOID Cookie
);

// ----------------------------------------------------------------------------
// [원본 API 함수 포인터 정의]
// ----------------------------------------------------------------------------
typedef HANDLE(WINAPI* PFN_CreateFileW)(LPCWSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
typedef BOOL(WINAPI* PFN_ReadFile)(HANDLE, LPVOID, DWORD, LPDWORD, LPOVERLAPPED);
typedef BOOL(WINAPI* PFN_WriteFile)(HANDLE, LPCVOID, DWORD, LPDWORD, LPOVERLAPPED);
typedef BOOL(WINAPI* PFN_SetFilePointerEx)(HANDLE, LARGE_INTEGER, PLARGE_INTEGER, DWORD);
typedef BOOL(WINAPI* PFN_CloseHandle)(HANDLE);
typedef BOOL(WINAPI* PFN_GetFileSizeEx)(HANDLE, PLARGE_INTEGER);
typedef DWORD(WINAPI* PFN_GetFileType)(HANDLE);
typedef BOOL(WINAPI* PFN_GetFileInformationByHandleEx)(HANDLE, FILE_INFO_BY_HANDLE_CLASS, LPVOID, DWORD);
typedef HANDLE(WINAPI* PFN_CreateFileMappingW)(HANDLE, LPSECURITY_ATTRIBUTES, DWORD, DWORD, DWORD, LPCWSTR);
typedef BOOL(WINAPI* PFN_MoveFileW)(LPCWSTR, LPCWSTR);
typedef BOOL(WINAPI* PFN_MoveFileExW)(LPCWSTR, LPCWSTR, DWORD);
typedef BOOL(WINAPI* PFN_CopyFileW)(LPCWSTR, LPCWSTR, BOOL);
typedef BOOL(WINAPI* PFN_CopyFileExW)(LPCWSTR, LPCWSTR, LPPROGRESS_ROUTINE, LPVOID, LPBOOL, DWORD);
typedef BOOL(WINAPI* PFN_ReplaceFileW)(LPCWSTR, LPCWSTR, LPCWSTR, DWORD, LPVOID, LPVOID);
typedef BOOL(WINAPI* PFN_DeleteFileW)(LPCWSTR);
typedef DWORD(WINAPI* PFN_GetFileAttributesW)(LPCWSTR);
typedef BOOL(WINAPI* PFN_SetFileAttributesW)(LPCWSTR, DWORD);
typedef BOOL(WINAPI* PFN_RemoveDirectoryW)(LPCWSTR);
typedef void(WINAPI* PFN_ExitProcess)(UINT);
typedef BOOL(WINAPI* PFN_TerminateProcess)(HANDLE, UINT);
typedef BOOL(WINAPI* PFN_CreateProcessW)(LPCWSTR, LPWSTR, LPSECURITY_ATTRIBUTES, LPSECURITY_ATTRIBUTES, BOOL, DWORD, LPVOID, LPCWSTR, LPSTARTUPINFOW, LPPROCESS_INFORMATION);

// ----------------------------------------------------------------------------
// [전역 원본 API 함수 포인터 변수 선언]
// ----------------------------------------------------------------------------
extern PFN_CreateFileW TrueCreateFileW;
extern PFN_ReadFile TrueReadFile;
extern PFN_WriteFile TrueWriteFile;
extern PFN_SetFilePointerEx TrueSetFilePointerEx;
extern PFN_CloseHandle TrueCloseHandle;
extern PFN_GetFileSizeEx TrueGetFileSizeEx;
extern PFN_GetFileType TrueGetFileType;
extern PFN_GetFileInformationByHandleEx TrueGetFileInformationByHandleEx;
extern PFN_CreateFileMappingW TrueCreateFileMappingW;
extern PFN_MoveFileW TrueMoveFileW;
extern PFN_MoveFileExW TrueMoveFileExW;
extern PFN_CopyFileW TrueCopyFileW;
extern PFN_CopyFileExW TrueCopyFileExW;
extern PFN_ReplaceFileW TrueReplaceFileW;
extern PFN_DeleteFileW TrueDeleteFileW;
extern PFN_GetFileAttributesW TrueGetFileAttributesW;
extern PFN_SetFileAttributesW TrueSetFileAttributesW;
extern PFN_RemoveDirectoryW TrueRemoveDirectoryW;
extern PFN_ExitProcess TrueExitProcess;
extern PFN_TerminateProcess TrueTerminateProcess;
extern PFN_CreateProcessW TrueCreateProcessW;

// ----------------------------------------------------------------------------
// [후킹 콜백 함수 선언]
// ----------------------------------------------------------------------------
HANDLE WINAPI Hooked_CreateFileW(LPCWSTR lpFileName, DWORD dwAccess, DWORD dwShare, LPSECURITY_ATTRIBUTES lpSA, DWORD dwCreation, DWORD dwFlags, HANDLE hTemplate);
BOOL WINAPI Hooked_RemoveDirectoryW(LPCWSTR lpPathName);
DWORD WINAPI Hooked_GetFileAttributesW(LPCWSTR lpFileName);
BOOL WINAPI Hooked_MoveFileExW(LPCWSTR lpSrc, LPCWSTR lpDst, DWORD dwFlags);
BOOL WINAPI Hooked_MoveFileW(LPCWSTR lpExistingFileName, LPCWSTR lpNewFileName);
BOOL WINAPI Hooked_CopyFileW(LPCWSTR lpExistingFileName, LPCWSTR lpNewFileName, BOOL bFailIfExists);
BOOL WINAPI Hooked_CopyFileExW(LPCWSTR lpExistingFileName, LPCWSTR lpNewFileName, LPPROGRESS_ROUTINE lpProgressRoutine, LPVOID lpData, LPBOOL pbCancel, DWORD dwCopyFlags);
BOOL WINAPI Hooked_ReplaceFileW(LPCWSTR lpDst, LPCWSTR lpSrc, LPCWSTR lpBak, DWORD dwFlags, LPVOID lpExclude, LPVOID lpReserved);
BOOL WINAPI Hooked_DeleteFileW(LPCWSTR lpFileName);
BOOL WINAPI Hooked_ReadFile(HANDLE hFile, LPVOID lpBuf, DWORD nRead, LPDWORD lpRead, LPOVERLAPPED lpOverlapped);
BOOL WINAPI Hooked_WriteFile(HANDLE hFile, LPCVOID lpBuf, DWORD nWrite, LPDWORD lpWritten, LPOVERLAPPED lpOverlapped);
BOOL WINAPI Hooked_SetFilePointerEx(HANDLE hFile, LARGE_INTEGER liMove, PLARGE_INTEGER lpNew, DWORD dwMoveMethod);
BOOL WINAPI Hooked_GetFileSizeEx(HANDLE hFile, PLARGE_INTEGER lpSize);
DWORD WINAPI Hooked_GetFileType(HANDLE hFile);
BOOL WINAPI Hooked_CloseHandle(HANDLE hFile);
HANDLE WINAPI Hooked_CreateFileMappingW(HANDLE hFile, LPSECURITY_ATTRIBUTES lpSA, DWORD flProtect, DWORD dwMaxHigh, DWORD dwMaxLow, LPCWSTR lpName);
void WINAPI Hooked_ExitProcess(UINT uExitCode);
BOOL WINAPI Hooked_TerminateProcess(HANDLE hProcess, UINT uExitCode);
BOOL WINAPI Hooked_CreateProcessW(LPCWSTR lpApplicationName, LPWSTR lpCommandLine, LPSECURITY_ATTRIBUTES lpProcessAttributes, LPSECURITY_ATTRIBUTES lpThreadAttributes, BOOL bInheritHandles, DWORD dwCreationFlags, LPVOID lpEnvironment, LPCWSTR lpCurrentDirectory, LPSTARTUPINFOW lpStartupInfo, LPPROCESS_INFORMATION lpProcessInformation);
