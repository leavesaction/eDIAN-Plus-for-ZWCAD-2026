# ZWCAD Phase 2 — VFS 실물 노출·가상화 생명주기 (설계)

MIP 복호화 임시 DWG(`mip\temp\_*.dwg`)에 대한 **PhantomVfs 실물 노출·기화·가상화 종료** 시점을 정의하는 문서입니다.

| 항목 | 내용 |
|------|------|
| **상위 로드맵** | [ROADMAP_PHASE2_VFS.md](./ROADMAP_PHASE2_VFS.md) — Phase 2 마일스톤·진행 상태 |
| **본 문서** | Native 구현·실측의 **설계 계약** (상세) |
| **범위** | `eDIAN.Hook.Native` 중심. Managed는 기존 흐름 유지·최소 수정 |
| **참고 (AutoCAD)** | EPDF 등 외부 프로세스 동안 실물 유지 — AutoCAD 실측 철학과 동일 |
| **검증·판정 단일 기준** | [ZWCAD_VFS_ENGINEERING_GUIDE.md](./ZWCAD_VFS_ENGINEERING_GUIDE.md) — P0/P1/P2, ETW/Procmon 판정([A/B/C/D]), 프리징 0 게이트 |

---

## 1. 문제 요약

| 증상 | 로그·상황 |
|------|-----------|
| QSAVE는 성공처럼 보임 | `saveComplete`, ZWCAD `CopyFileW` 가상 복사 OK |
| 닫고 `test_01.dwg` 재오픈 시 변경 없음 | `applyProtectionToTempFile` — temp `No such file or directory` |
| 저장 후 목록이 임시명·읽기전용 | Managed `equals`에서 `IsReadOnly` 매칭 — **완화됨** (`608f91f`) |

**원인 (Native 관점)**  
AutoCAD와 **동일 Native**이나, ZWCAD는 **저장·닫기·MIP의 Win32 호출 순서·타이밍**이 VFS가 기대하는 **실물 노출·기화 시점**과 어긋남.  
가상화(GHT 고스트) 자체가 ZWCAD만 다르게 설계된 것은 아님.

---

## 2. 파일 종류 구분

| 구분 | 경로 예 | 생성 주체 | Native 처리 |
|------|---------|-----------|-------------|
| **가상화 본체** | `mip\temp\_uuid.dwg` (키) + `…\{세션}\GHT*.tmp` | VFS | **AutoCAD와 동일** 유지 |
| **ZWCAD 저장 sidecar** | `mip\temp\zws*.tmp` | ZWCAD QSAVE | **ZWCAD 전용** (`TryVirtualCopyForZwcadSave` 등) |

- **가상화 tmp** = `GetUniqueGhostPath` 고스트 (`FILE_FLAG_DELETE_ON_CLOSE`). **ACAD/ZW 공통.**
- **`zws*.tmp`** = CAD 저장 마무리용. 가상화 tmp와 **혼동 금지.**

---

## 3. Managed vs Native 역할

| 역할 | 담당 |
|------|------|
| 열기 | Managed: `File.Copy` → `_uuid.dwg` (잠깐 실물) → CAD `Open` |
| 편집·저장 이벤트 | Managed: `needLabeling`, `commandEnded`, `applyProtection` 트리거 |
| 가상 I/O | Native: storage + 고스트 |
| 닫기 MIP 반영 | Managed: `applyProtectionToTempFile` → `CommitAsync(원본)` |
| temp 실물 삭제 | Managed: `deleteFilesByName` → **`DeleteFileW` 훅** |
| **실물 노출·기화 시점** | **Native** (본 문서) |

> `setUserMipTempWritePermissions`는 `IS_FILE_ACL=false`로 **no-op**. ACL은 본 튜닝 범위 밖.

---

## 4. 생명주기 (목표 설계)

```text
[열기]       가상화 시작 — 실물 흡수·기화 → storage + 고스트 (평소 디스크 없음)

[필요 시]
  · 저장 (ZWCAD QSAVE)  — 처리 중만 실물 → 끝나면 디스크만 기화, storage 유지
  · 외부 (EPDF 등)      — 시작 시 실물 노출 → 종료 시 실물만 기화, 가상화 유지

[닫기]       실물 노출 + 가상화 종료 (storage·handleMap 릴리즈)
             → MIP가 실파일만 사용

[MIP 후]     Managed deleteFilesByName — 실물 삭제
```

### 4.1 시점별 계약표

| 시점 | 디스크 실물 | 가상화 (storage·고스트) | 비고 |
|------|-------------|-------------------------|------|
| **열기** | 없음 (흡수 후 기화) | **시작·유지** | ACAD/ZW 동일. ZW는 창이 짧아 “안 보임”처럼 느낄 수 있음 |
| **저장** | **처리 중만** 노출 | 유지 | ZW: `CopyFile` (zws→dwg). 저장 후 **디스크만** 재-은폐 |
| **외부 시작** | **노출** | 유지 | AutoCAD EPDF 실측과 동일 |
| **외부 종료** | **소멸** | 유지 | 도면은 계속 열려 있을 수 있음 |
| **닫기** | **노출·유지** | **종료(릴리즈)** | Native는 **실물 삭제 안 함** |
| **MIP 이후** | Managed 삭제 | — | `deleteFilesByName` |

### 4.2 닫기 모델 (확정)

1. **닫기 Native**: storage → 디스크 **강제 커밋** → **가상화 데이터 소멸** (storage / handleMap 해제).
2. **닫기 때 실물 삭제를 Managed 삭제와 묶지 않음.**
3. **실물 삭제**: MIP `CommitAsync` 성공 후 기존 **`deleteFilesByName`** → `DeleteFileW`.

**조건**: 가상 핸들 **`refCount == 0`** (CAD 핸들 정리 완료) 이후에만 커밋+릴리즈.

### 4.3 외부 프로세스 (EPDF 등)

| 단계 | 실물 | 가상화 |
|------|------|--------|
| 작업 시작 | **노출** | 유지 |
| 작업 중 | **유지** | 유지 |
| 작업 종료 | **소멸** | 유지 |

닫기와의 차이: **외부 종료**는 실물만 지우고 가상화는 유지. **닫기**는 실물 노출 + **가상화 종료**.

---

## 5. AutoCAD vs ZWCAD

| | AutoCAD | ZWCAD |
|--|---------|-------|
| 가상화(열기·고스트) | 동일 코드 | 동일 |
| 저장 마무리 | 가상 쓰기 + CloseHandle 커밋 위주 | **`CopyFileW`(zws→dwg)** 추가 |
| 닫기·MIP | 커밋 타이밍이 MIP와 대체로 일치 | MIP **비동기** + 커밋/삭제 **레이스** |
| 열 때 실물 | 잠깐 보였다 사라짐 가능 | **같은 메커니즘**, 더 빨라 “없음”처럼 보일 수 있음 |

**정리**: ZWCAD 오동작은 **같은 소스에서 실물 노출·소거 시점이 CAD I/O 순서와 안 맞음** + 저장용 `CopyFile` 레이어.  
가상화(GHT)는 **AutoCAD와 동일 프로세스**로 유지.

---

## 6. Native 구현 트랙 (진행 상태)

상위 표는 [ROADMAP_PHASE2_VFS.md §6a](./ROADMAP_PHASE2_VFS.md#6a--mip저장닫기-실물-노출-native) 에서 갱신.

| ID | 내용 | 파일 | 상태 |
|----|------|------|------|
| **L1** | Managed: `equals`에서 `isReadOnly` 제외 | `DocumentHandler.cs`, `MainFormExtends.cs` | **완료** (`608f91f`) |
| **L2** | 닫기: **Materialize + VirtualRelease** (`refCount==0`, mip temp `_*.dwg`) | `FileHooks.cpp` | **구현** (Debug 빌드, L6 실측 대기) |
| **L3** | 저장: **SaveExposed** — QSAVE 중만 실물, 종료 후 디스크만 기화 | `FileHooks.cpp` | 대기 |
| **L4** | 외부: 시작 노출 · 종료 실물 기화 (Keeper/프로세스 종료) | `FileHooks.cpp` | 대기 |
| **L5** | 로그 태그 `[CLOSE]` / `[SAVE-IO]` / `[EXTERNAL]` 정리 | `FileHooks.cpp` | 대기 |
| **L6** | 실측: 저장 → 닫기 → `test_01` 재오픈 | 박부장 | 대기 |
| **L7** | AutoCAD 회귀 1회 (공통 경로) | 박부장 | 대기 |

### 6.1 L2 구현 개념

- `CloseMaterializeAndRelease(path)`:
  - `isModified` / 500ms 조건 **우회** 후 버퍼 → `CREATE_ALWAYS` 커밋
  - storage / handleMap에서 경로 **제거**
  - 로그: `[CLOSE] materialized N bytes + virtual released`
- `GetFileAttributes`: storage만 있고 실물 없을 때 MIP에 속이지 않기
- `DeleteFileW`: storage 이미 없을 때 Managed 삭제 → **`TrueDeleteFileW`만**

### 6.2 L3 구현 개념

- `TryVirtualCopyForZwcadSave` 성공 시 `SaveExposed` 표시
- SAVE-IO 종료 후 `PhysicalVaporizeDiskOnly` (storage 유지)
- `SaveExposed` 중 `CreateFile` 흡수·기화 금지

### 6.3 ZWCAD 전용 vs 공통 경계

| 변경 허용 | 변경 금지 (ACAD 동일 유지) |
|-----------|---------------------------|
| `zws` sidecar, `TryVirtualCopyForZwcadSave` | 고스트 `GHT*.tmp` 생성·정리 |
| 닫기 Materialize+Release | 열기 흡수·기화 기본 흐름 |
| SaveExposed / 외부 종료 기화 | `IsTarget` / refCount / 고스트 surrogate |

---

## 7. Managed (완료·유지)

| 항목 | 상태 |
|------|------|
| `DocumentHandler.equals` — 경로 + `hashCode`, `isReadOnly` 제외 | **커밋** `608f91f` |
| `MainFormExtends` 목록 선택 매칭 동일 | **커밋** `608f91f` |
| `applyProtectionToTempFile` / `deleteFilesByName` | **변경 없음** (Native 시점 맞춤 후 검증) |

---

## 8. 검증 시나리오

1. MIP `test_01.dwg` 열기 → 편집 → QSAVE  
2. 플러그인에서 문서 닫기  
3. `test_01.dwg` 재오픈 → **변경 내용 유지**  
4. 로그:
   - `[CLOSE]` materialized + virtual released
   - `application.log` — `applyProtectionToTempFile` **성공** (예외 없음)
   - (선택) `deleteFilesByName` 후 temp 파일 없음  
5. AutoCAD: 열기·저장·닫기·EPDF 1회 회귀  

---

## 9. 로그 교차 검증 (에이전트 SOP)

**원칙**: 단일 로그만으로 원인 단정하지 않는다. **같은 시각(초)·같은 `_uuid.dwg`·MIP CorrelationId**로 묶는다.  
상세 수집/판정/검색 예시는 `ZWCAD_VFS_ENGINEERING_GUIDE.md` §5(단일 기준)를 따른다. 본 절은 **증상→키워드(타이밍) 매핑**만 유지한다.

### 9.1 증상 → 로그 키워드(타이밍) 매핑

| 사용자 증상 | 1차 | 2차 | 3차 | VFS에서 확인할 키워드 |
|-------------|-----|-----|-----|----------------------|
| 저장 후 닫기, 재오픈 시 **변경 없음** | `close_flow` ApplyProtectionFailed | `application` `applyProtectionToTempFile` IOException | `mip_sdk` Failed to open file | `[CLOSE]` 선행 33KB vs CopyFile 55KB |
| 팔레트 닫기, MIP 실패 | `close_flow` | `mip_sdk` | `vfs_console` | temp 실물 없음 시점 |
| **저장 후 닫기**, 재오픈 **오류** | `application` `openDocumentFile` + `Document.Open` | `mip_sdk` `file_commit_async` Commit.Result | `vfs_console` 닫기 직전 | commit 성공 vs ZWCAD ArgumentException |
| EPDF 정상 | `application` EXPORTPDF | (닫기 MIP 경로 아님) | — | 외부 JIT, `[CLOSE]`와 분리 |
| 레이블 적용 정상 | `application` pdwg 경로 | `mip_sdk` | — | temp 실패해도 우회 가능 |

---

## 10. 변경 이력

| 날짜 | 내용 |
|------|------|
| 2026-05-27 | 초안 — 실측·설계 합의 반영 (6단계 하위 트랙) |
| 2026-05-28 | §9 로그 교차 검증 SOP — 실측 분석 절차·증상 매핑 |

---

**Last Updated**: 2026-05-28  
**Status**: **L1·L2 구현** — **L3~L5 Native·L6~L7 실측 대기**
