#include "DetoursInterceptor.h"

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved) {
    switch (ul_reason_for_call) {
    case DLL_PROCESS_ATTACH:
        // Optimize performance by disabling thread library calls
        DisableThreadLibraryCalls(hModule);
        break;

    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
        break;

    case DLL_PROCESS_DETACH:
        // If the hooks were still active, uninstall them gracefully
        UninstallHooks();
        break;
    }
    return TRUE;
}
