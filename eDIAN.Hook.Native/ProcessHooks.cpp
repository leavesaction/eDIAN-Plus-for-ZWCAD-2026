#include "DetoursInterceptor.h"
#include "IVfsEngine.h"
#include "ThreadHookGuard.h"
#include "VfsLogger.h"

void WINAPI Hooked_ExitProcess(UINT uExitCode) {
    auto& engine = PhantomVfs::GetVfsEngine();
    VFS_LOG_INFO(L"[Vaporize] %s ExitProcess 감지 - 세션 소거 개시.", engine.GetCurrentProcessName().c_str());
    engine.VaporizeSessionDir();
    TrueExitProcess(uExitCode);
}

BOOL WINAPI Hooked_TerminateProcess(HANDLE hProcess, UINT uExitCode) {
    if (hProcess == GetCurrentProcess()) {
        auto& engine = PhantomVfs::GetVfsEngine();
        VFS_LOG_INFO(L"[Vaporize] %s TerminateProcess(자폭) 감지 - 세션 소거 개시.", engine.GetCurrentProcessName().c_str());
        engine.VaporizeSessionDir();
    }
    return TrueTerminateProcess(hProcess, uExitCode);
}

BOOL WINAPI Hooked_CreateProcessW(
    LPCWSTR lpApplicationName,
    LPWSTR lpCommandLine,
    LPSECURITY_ATTRIBUTES lpProcessAttributes,
    LPSECURITY_ATTRIBUTES lpThreadAttributes,
    BOOL bInheritHandles,
    DWORD dwCreationFlags,
    LPVOID lpEnvironment,
    LPCWSTR lpCurrentDirectory,
    LPSTARTUPINFOW lpStartupInfo,
    LPPROCESS_INFORMATION lpProcessInformation
) {
    if (PhantomVfs::ThreadHookGuard::IsBypassed()) {
        return TrueCreateProcessW(
            lpApplicationName, lpCommandLine, lpProcessAttributes, lpThreadAttributes,
            bInheritHandles, dwCreationFlags, lpEnvironment, lpCurrentDirectory,
            lpStartupInfo, lpProcessInformation
        );
    }

    PhantomVfs::HookBypassGuard guard;
    auto& engine = PhantomVfs::GetVfsEngine();

    std::wstring appName = lpApplicationName ? lpApplicationName : L"N/A";
    std::wstring cmdLine = lpCommandLine ? lpCommandLine : L"N/A";

    VFS_LOG_INFO(L"[Audit Log] CreateProcessW Intercepted | App: %s | Cmd: %s", appName.c_str(), cmdLine.c_str());

    BOOL result = TrueCreateProcessW(
        lpApplicationName,
        lpCommandLine,
        lpProcessAttributes,
        lpThreadAttributes,
        bInheritHandles,
        dwCreationFlags,
        lpEnvironment,
        lpCurrentDirectory,
        lpStartupInfo,
        lpProcessInformation
    );

    if (result && lpProcessInformation) {
        VFS_LOG_INFO(L"[Audit Log] CreateProcessW Succeeded | Created Process ID: %u", lpProcessInformation->dwProcessId);
    } else {
        VFS_LOG_INFO(L"[Audit Log] CreateProcessW Failed | Error: %u", GetLastError());
    }

    return result;
}
