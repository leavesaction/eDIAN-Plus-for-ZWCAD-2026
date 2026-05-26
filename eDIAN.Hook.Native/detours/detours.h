//////////////////////////////////////////////////////////////////////////////
//
//  Essential Microsoft Detours Header (Condensed for eDIAN Plus)
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#ifndef _DETOURS_H_
#define _DETOURS_H_

#define DETOURS_VERSION     0x040001 // 4.0.1

#include <windows.h>

#ifdef __cplusplus
extern "C" {
#endif

//////////////////////////////////////////////////////////////////////////////
//
//  Standard Detours APIs
//

LONG WINAPI DetourTransactionBegin(VOID);
LONG WINAPI DetourTransactionCommit(VOID);
LONG WINAPI DetourUpdateThread(HANDLE hThread);

LONG WINAPI DetourAttach(PVOID *ppPointer, PVOID pDetour);
LONG WINAPI DetourDetach(PVOID *ppPointer, PVOID pDetour);

VOID WINAPI DetourRestoreAfterWith(VOID);

//////////////////////////////////////////////////////////////////////////////
//
//  Internal Helpers for Instruction Analysis (Required for x64)
//

PVOID WINAPI DetourCodeFromPointer(PVOID pPointer, PVOID *ppRealPointer);

#ifdef __cplusplus
}
#endif

#endif // _DETOURS_H_
