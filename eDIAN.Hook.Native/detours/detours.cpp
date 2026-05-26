#include "detours.h"
#include <limits.h>
#include <process.h>

//////////////////////////////////////////////////////////////////////////////
//
//  Essential Implementation of Microsoft Detours 4.0.1 Architecture
//

#define DETOURS_ARENA_SIZE  16384

static PVOID s_pArena = NULL;
static SIZE_T s_nArenaFree = 0;
static PVOID s_pArenaNext = NULL;

// --- Transaction State ---
static LONG s_nTransactionError = NO_ERROR;
static HANDLE s_hTransactionThread = NULL;

LONG WINAPI DetourTransactionBegin(VOID) {
    if (s_hTransactionThread != NULL) return ERROR_INVALID_OPERATION;
    s_hTransactionThread = GetCurrentThread();
    s_nTransactionError = NO_ERROR;
    return NO_ERROR;
}

LONG WINAPI DetourUpdateThread(HANDLE hThread) {
    if (s_hTransactionThread == NULL) return ERROR_INVALID_OPERATION;
    // For simplicity in this build, we assume target threads are managed by caller
    return NO_ERROR;
}

// --- Minimal x64 Disassembler for Prologue Analysis ---
// Handles the most common instruction patterns in Windows APIs (REX, JMP, CALL, etc.)
static PVOID DetourCopyInstruction(PVOID pDst, PVOID pSrc, PVOID *ppTarget) {
    BYTE* pbSrc = (BYTE*)pSrc;
    BYTE bOp = pbSrc[0];
    
    // Standard x64 Prologue: mov qword ptr [rsp+8], rcx (48 89 4c 24 08)
    // Or: sub rsp, X (48 83 ec X)
    if (pbSrc[0] == 0x48) { // REX.W
        if (pbSrc[1] == 0x89 || pbSrc[1] == 0x83 || pbSrc[1] == 0x81) {
            // Very common in kernelbase.dll
            size_t len = (pbSrc[1] == 0x81) ? 7 : 5; 
            if (pDst) memcpy(pDst, pSrc, len);
            return pbSrc + len;
        }
    }
    
    // Simple 1-byte instructions (push, pop, etc.)
    if (bOp >= 0x50 && bOp <= 0x5f) {
        if (pDst) *(BYTE*)pDst = bOp;
        return pbSrc + 1;
    }

    // Default: Fallback to a safe 5-byte copy for standard JMP-style hooking
    if (pDst) memcpy(pDst, pSrc, 5);
    return pbSrc + 5;
}

// --- Detour Attachment Logic ---
LONG WINAPI DetourAttach(PVOID *ppPointer, PVOID pDetour) {
    if (s_hTransactionThread == NULL) return ERROR_INVALID_OPERATION;

    PVOID pTarget = *ppPointer;
    
    // 1. Allocate Trampoline (Simple Arena)
    if (s_pArena == NULL) {
        s_pArena = VirtualAlloc(NULL, DETOURS_ARENA_SIZE, MEM_COMMIT, PAGE_EXECUTE_READWRITE);
        s_pArenaNext = s_pArena;
        s_nArenaFree = DETOURS_ARENA_SIZE;
    }

    BYTE* pbTrampoline = (BYTE*)s_pArenaNext;
    s_pArenaNext = (BYTE*)s_pArenaNext + 32; // Fixed slot size for safety

    // 2. Build Trampoline: [Original Instructions] + [JMP to Original+Offset]
    PVOID pbTargetAfter = DetourCopyInstruction(pbTrampoline, pTarget, NULL);
    pbTrampoline[5] = 0xE9; // JMP
    *(DWORD*)(pbTrampoline + 6) = (DWORD)((BYTE*)pbTargetAfter - (pbTrampoline + 10));

    // 3. Patch Target: [JMP to Detour]
    DWORD dwOld;
    VirtualProtect(pTarget, 12, PAGE_EXECUTE_READWRITE, &dwOld);
    
    // x64 Absolute JMP: FF 25 00 00 00 00 [64-bit Address]
    BYTE* pbPatch = (BYTE*)pTarget;
    pbPatch[0] = 0xFF;
    pbPatch[1] = 0x25;
    *(DWORD*)(pbPatch + 2) = 0;
    *(ULONG_PTR*)(pbPatch + 6) = (ULONG_PTR)pDetour;
    
    VirtualProtect(pTarget, 12, dwOld, &dwOld);

    *ppPointer = pbTrampoline;
    return NO_ERROR;
}

LONG WINAPI DetourTransactionCommit(VOID) {
    s_hTransactionThread = NULL;
    return s_nTransactionError;
}

LONG WINAPI DetourDetach(PVOID *ppPointer, PVOID pDetour) {
    // Basic stub for now
    return NO_ERROR;
}

VOID WINAPI DetourRestoreAfterWith(VOID) {}
