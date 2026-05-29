#pragma once
#include <windows.h>
#include <string>
#include <vector>
#include <mutex>
#include <atomic>
#include <memory>
typedef HANDLE (WINAPI *FnVfsOpenCallback)(LPCWSTR lpFileName, DWORD dwAccess, DWORD dwShare);
typedef void   (WINAPI *FnVfsCloseCallback)(HANDLE hFile);

namespace PhantomVfs {

// ----------------------------------------------------------------------------
// [상수 정의]
// ----------------------------------------------------------------------------
/** @brief 초기 가상 파일 메모리 할당 크기 (10MB 단위로 확장) */
static const size_t INITIAL_PADDING = 10 * 1024 * 1024; 


// ----------------------------------------------------------------------------
// [데이터 구조체]
// ----------------------------------------------------------------------------

/**
 * @brief 가상 파일 객체
 * 실물 파일 대신 메모리 맵핑(File Mapping)을 통해 파일 내용을 관리합니다.
 */
struct VirtualFile {
    std::wstring path;              ///< 대상 파일의 정규화된 경로
    HANDLE hMapping;                ///< 메모리 맵핑 핸들
    LPVOID pBase;                   ///< 메모리 맵핑 시작 주소 (데이터 포인터)
    HANDLE hKeeper;                 ///< 파일 삭제 방지를 위한 유지(Keeper) 핸들
    std::atomic<size_t> currentSize; ///< 현재 파일의 실제 데이터 크기
    size_t totalCapacity;           ///< 현재 할당된 메모리 전체 용량
    std::recursive_mutex fileMtx;   ///< storage·Sync·EnsureCapacity 동일 스레드 재진입 허용
    std::atomic<int> refCount;      ///< 이 파일을 참조 중인 핸들 개수
    bool isModified;                ///< 데이터 수정 여부 (수정 시 나중에 디스크에 커밋)
    std::atomic<bool> isManifesting; ///< 현재 JIT 실체화(파일 생성) 작업 진행 중인지 여부
    std::atomic<bool> saveExposed;   ///< L3: QSAVE 구간 _uuid.dwg 실물 I/O (저장 후 디스크만 기화)
    ULONGLONG lastVaporizedTime;    ///< 마지막으로 실물 파일이 삭제(기화)된 시간

    VirtualFile(const std::wstring& p)
        : path(p), hMapping(NULL), pBase(NULL), hKeeper(INVALID_HANDLE_VALUE),
          currentSize(0), totalCapacity(0), refCount(0), isModified(false),
          isManifesting(false), saveExposed(false), lastVaporizedTime(0) {}

    ~VirtualFile() { Cleanup(); }

    /** @brief 할당된 모든 네이티브 자원(맵핑, 핸들)을 해제합니다. */
    void Cleanup() {
        if (pBase) {
            UnmapViewOfFile(pBase);
            pBase = NULL;
        }
        if (hMapping) {
            CloseHandle(hMapping);
            hMapping = NULL;
        }
        if (hKeeper != INVALID_HANDLE_VALUE) {
            CloseHandle(hKeeper);
            hKeeper = INVALID_HANDLE_VALUE;
        }
    }

    /**
     * @brief 필요한 크기만큼 가상 메모리 공간을 확보합니다.
     * @param needed 필요한 최소 바이트 크기
     * @return 성공 여부
     */
    bool EnsureCapacity(size_t needed) {
        if (needed <= totalCapacity && hMapping != NULL)
            return true;
        std::lock_guard<std::recursive_mutex> lock(fileMtx);
        if (needed <= totalCapacity && hMapping != NULL)
            return true;

        // 지수적 확장 전략 (대용량 파일 시 메모리 과할당 방지를 위해 64MB 캡 사용)
        size_t newCapacity = needed + INITIAL_PADDING;
        if (totalCapacity > 0) {
            size_t capLimit = 64 * 1024 * 1024; // 64MB
            if (totalCapacity < capLimit) {
                newCapacity = (std::max)(needed + INITIAL_PADDING, totalCapacity * 2);
            } else {
                newCapacity = (std::max)(needed + INITIAL_PADDING, totalCapacity + 32 * 1024 * 1024); // 32MB 고정 증가
            }
        }

        HANDLE newMapping = CreateFileMapping(
            INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, (DWORD)(newCapacity >> 32),
            (DWORD)(newCapacity & 0xFFFFFFFF), NULL);
        if (!newMapping)
            return false;
        LPVOID newBase = MapViewOfFile(newMapping, FILE_MAP_ALL_ACCESS, 0, 0, 0);
        if (!newBase) {
            CloseHandle(newMapping);
            return false;
        }

        size_t savedSize = currentSize.load();
        if (pBase && savedSize > 0)
            memcpy(newBase, pBase, savedSize);
        Cleanup();
        hMapping = newMapping;
        pBase = newBase;
        totalCapacity = newCapacity;
        return true;
    }
};

/**
 * @brief 가상 파일 핸들
 * 실제 파일 핸들과 1:1로 매칭되어 현재 읽기/쓰기 위치(Position)를 추적합니다.
 */
struct VirtualHandle {
    std::shared_ptr<VirtualFile> file; ///< 참조하는 가상 파일 객체
    long long position;                ///< 현재 파일 포인터 위치
};

/**
 * @brief 파일 유지 정보
 * 백그라운드 프로세스(acpublish 등)가 파일을 사용하는 동안 삭제되지 않게 보호합니다.
 */
struct KeeperInfo {
    HANDLE hKeeper;        ///< 보호용 오픈 핸들
    DWORD ownerPID;        ///< 파일을 생성한 프로세스 ID
    std::wstring path;     ///< 파일 경로
    ULONGLONG createdAt;   ///< 보호 시작 시간
};

/**
 * @brief 실체화 가드 (RAII)
 * JIT 실체화 작업 중 발생할 수 있는 중복 작업을 방지하기 위한 안전장치입니다.
 */
struct ManifestingGuard {
    std::atomic<bool>& flag;
    ManifestingGuard(std::atomic<bool>& f) : flag(f) {}
    ~ManifestingGuard() { flag = false; }
};

} // namespace PhantomVfs
