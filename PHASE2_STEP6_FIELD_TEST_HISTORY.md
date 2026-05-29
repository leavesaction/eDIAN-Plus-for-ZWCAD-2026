# Phase 2 — 6단계(ZWCAD 실측·튜닝) 진행 기록 (성공/실패 요약)

본 문서는 Phase 2의 **6단계(ZWCAD 실측·튜닝)** 진행 과정에서 실제로 수행한 테스트/패치와 그 **성공·실패 여부**를 “복원(rollback) 전” 기준으로 남기기 위한 기록이다.  
설계 계약은 `ROADMAP_PHASE2_VFS_LIFECYCLE.md`, 판정/로그 수집 단일 기준은 `ZWCAD_VFS_ENGINEERING_GUIDE.md`를 따른다.

## 0. 범위

- **대상**: ZWCAD + PhantomVfs(Native) 적용 이후, 실측·튜닝 구간에서 발생한 이슈(저장/닫기/MIP/재오픈, 프리징/저장 실패 메시지).
- **포함**: Native `FileHooks.cpp` 변경에 따른 증상 변화(프리징/저장 실패/커밋 로그 유무).
- **제외**: Plot/Publish 부속 exe 실측(H3/H4 등)은 “현장 Procmon 확정” 전이라 본 기록에 결과를 확정하지 않는다.

## 1. 기준선(5단계 VFS ON 성공)

- **시점**: 2026-05-26 (ZWCAD Debug 기동)
- **판정**: **성공** (VFS 정상 가동)
- **근거(문서/로그)**:
  - `ROADMAP_PHASE2_VFS.md` 5단계 “완료(Completed)” 기록
  - `plugin.log`: `Initialize called` / `completed`
  - `vfs_console_5616.log`: `Process: ZWCAD.exe`, 동적 세션 샌드박스 생성, 고스트·커밋, 종료 시 `[Vaporize]` 세션 소거

> 이 “5단계 직후 Native”를 롤백 기준선으로 삼으면, 이후 변화(L2/L3/L4 등)를 **단일 변화로 재도입**하며 비교가 가능하다.

## 2. 6단계 착수(Managed/문서 보강)

- **시점**: 2026-05-26~27
- **작업(요약)**:
  - `HookConstants.ZWCAD_TEMP_PATTERNS`에 `\temp\zws` 추가(5단계에서 `zws*.tmp` 관측 기반)
  - `PluginApplication.cs`: VFS Install 이후 native config 문자열을 `plugin.log`에 기록(진단 편의)
  - 6단계 현장 가이드 문서(Procmon/체크리스트) 작성
- **판정**: 준비 작업(성공/실패 판정 대상 아님)

## 3. Phase 6 — Native 변경·실측 이슈 타임라인

### 3.1 CloseHandle SEH(0xC0000008) 대응 패치

- **시점**: 2026-05-27 (대화 로그 기준)
- **변경**: `FileHooks.cpp`
  - `TryTrueCloseHandleNoThrow()` 추가(예: `0xC0000008` SEH 방어)
- **목적**: CloseHandle 훅 경로에서 “이미 닫힌 핸들/경합”로 인한 예외가 연쇄되며 저장/닫기 경로를 흔드는 것을 완화
- **판정**: “치명 예외 감소” 목적 패치로, 단독 성공 판정은 이후 실측 결과로만 가능

### 3.2 (실패) 커밋 조건 축소 → QSAVE 저장 프리징 발생

- **테스트 시각(사용자 제보)**: **2026-05-27 15:06~15:08**
- **증상**: 저장 버튼 클릭 시 **CAD 프리징**
- **로그 근거(요약)**:
  - `application.log`: `QSAVE` 진입 후 `beginSave`에서 진행이 멈춘 형태
  - `vfs_console_12868.log`: QSAVE 구간에서 “커밋 시작/완료” 계열 로그가 없고, `Keeper 폐쇄`/`JIT 실체화` 등만 관측
- **원인 가설(당시 분석)**:
  - `Internal_CloseHandle`에서 커밋 조건을 **`isModified`만**으로 축소한 변경 때문에, ZWCAD 저장 루프가 기대하는 디스크 업데이트/정리 단계가 스킵되어 저장 사이클이 끝까지 진행되지 못함
- **조치**: **원복(복구)**  
  - 커밋 조건을 `isModified || (now - lastVaporizedTime > 500)` 형태로 되돌림(프리징 유발 가능 분기 제거)
- **판정**: **실패(프리징 0 게이트 위반)**  
  - “커밋 조건 축소” 접근은 Phase 6에서 **폐기 대상**

### 3.3 (부분 성공) 프리징은 없지만 “DWG 저장 실패 → TMP로 저장” 메시지 발생

- **테스트 시각(로그 기반)**: **2026-05-27 15:31~15:33**
- **사용자 증상**:
  - 프리징은 없음
  - 저장 클릭 시 “`*.dwg`에 저장할 수 없습니다. `*.tmp`에 저장했습니다” 메시지
  - 저장이 기대대로 원본에 반영되지 않는 느낌
- **로그 근거(요약)**:
  - `application.log`:
    - `15:31:31` QSAVE 진입/`beginSave`
    - `15:33:51` `saveComplete` / `commandEnded`
    - 저장 자체는 “완료 이벤트”가 존재(다만 시간이 비정상적으로 김)
  - `vfs_console_27484.log`:
    - 같은 시각대에 “커밋 시작/완료” 계열 로그가 존재(즉, VFS 관점 커밋 동작은 수행된 것으로 보임)
- **판정**: **미해결(기능 실패는 남음, 프리징은 해소)**
  - “원본 `.dwg` 저장 실패 후 `.tmp`로 저장” 메시지는 ZWCAD 저장/원자 교체 시퀀스(예: Copy/Replace/Move)와 VFS 정책이 충돌하는 신호일 수 있어, **ZWCAD 저장 시퀀스 실측(Procmon) + Native 정책(L3 SaveExposed) 재설계**가 필요

## 4. 현재 결론(Phase 6 기준)

- **확정 성공**:
  - 5단계 VFS ON 자체(엔진 가동/세션/기화)는 로그로 성공 판정이 가능
  - (2026-05-28 §4.12) QSAVE/닫기/재오픈 **5회 연속** — “tmp로 저장” 0회, 재오픈 시 수정 반영, 원본 LastWriteTime 갱신
- **확정 실패**:
  - “커밋 조건 축소(isModified only)”는 저장 프리징을 유발 → **폐기**
- **응급 완화(정식 Lifecycle 미완)**:
  - `mip_data\mip\temp` passthrough(`AccessGuard::IsManifestingPath`, `630f3a2`) — 저장 체인 보호용. **목표(순간 노출→디스크 기화)는 L3 SaveExposed로 수렴**
- **미해결(Lifecycle·인계 I1~I6 기준)**:
  - Open/Save/외부 구간 **“필요 순간만 노출 후 디스크 기화”** — §3.1·§3.2 계약 대비 **L3·L4·L6 미완**
  - 닫기 인계 **I5** — L2 Materialize + `applyProtection` 성공을 **교차 로그로 확정** 필요 (§4.12는 저장·재오픈 위주)

## 4.1 (추가 기록) 5단계 Native 복원 후 재현(동일 증상 확인)

- **시점**: 2026-05-28 16:10~16:11 (ZWCAD PID **24180**)
- **대상**: `test_01.dwg` → temp `_bc65d667-dd4e-4957-b5f2-b0b4affd2328.dwg`
- **판정**: **동일 증상 재현(미해결 유지)** — “5단계 Native 기준선에서도 문제 존재”를 재확인

### A) Managed/CloseFlow 관측(요약)

- `application.log`:
  - QSAVE 이후 닫기 흐름에서 `applyProtectionToTempFile`가 **Permission denied**로 실패
  - temp 삭제도 “다른 프로세스에서 사용 중”으로 실패
- `close_flow.log`:
  - `ApplyProtectionFailed … Failed to open file, Permission denied`
  - CorrelationId: **`ec1fdc72-25c0-48ae-ae72-ee60bcef860e`**

### B) Native 관측(요약)

`vfs_console_24180.log`에서 다음이 순서대로 관측됨:

- `_uuid.dwg` 실물 흡수/기화 및 고스트 생성
- QSAVE 구간에서 `zws*.tmp` 생성/기화 및 Keeper 폐쇄
- **`최종 변경사항 디스크 커밋 완료`** (33235 bytes) 후
- **`!!! CRITICAL EXCEPTION in CloseHandle … (Code: 0xC0000008)`**

### C) MIP SDK 관측(요약)

`mip_sdk.miplog`:

- `_bc65d667-...d2328.dwg`에 대해 `file_create_file_handler_async`
- **`Failed to open file, Permission denied`** (CorrelationId: `ec1fdc72-...`)

### D) 해석(Phase 6 관점)

- 이 재현은 “파일이 손상되어 열리지 않음”보다는, **MIP가 temp 파일을 열어야 하는 순간에 파일이 잠금/권한 문제로 열리지 않는 상태**가 1순위로 보인다.
- 특히 Native에서의 `CloseHandle` 치명 예외(0xC0000008)가 같은 타임라인에 존재하므로,
  - **핸들 정리/keeper close 경로 안정화(예외 삼키기/정리 순서 보장)**가 우선 과제로 남는다.

---

## 4.3 (추가 기록) 2026-05-28 16:49 저장(QSAVE) 실패 재현 + CloseHandle 완화 패치

### A) 변경(패치)

- **목적**: `CloseHandle` 경로에서 `0xC0000008` 등 비정상 상황이 발생해도,
  - Native가 **치명 예외를 남기거나 정리 흐름이 중단**되며 후속 저장/닫기 흐름을 망치지 않도록 완화
- **변경 파일**: `eDIAN.Hook.Native\FileHooks.cpp`
- **변경 요약**:
  - `TryTrueCloseHandleNoThrow()` 추가( `TrueCloseHandle` 호출을 SEH로 감싸 예외 방지)
  - `Internal_CloseHandle()`에서 `hOrig`, `hKeeper`, 마지막 `hFile` close를 모두 `TryTrueCloseHandleNoThrow()`로 호출
  - `Hooked_CloseHandle()`의 `__except`에서도 `TryTrueCloseHandleNoThrow()`로 원본 close 수행
- **빌드**:
  - `eDIAN.Hook.Native` Rebuild 성공 (Debug|x64)
  - `eDIAN.Main` Rebuild 성공 (Debug|x64)

### B) 실측(재현) — 저장 단계 게이트 실패

- **시점**: 2026-05-28 **16:49**
- **PID**: ZWCAD **31840**
- **시나리오**: 플러그인 로딩 → `test_01.dwg` 열기 → 간단 변경(ERASE) → **QSAVE**
- **표면 증상**:
  - “도면 `*.dwg`에 파일을 저장할 수 없습니다. 도면을 `*.tmp`에 저장했습니다” 메시지 발생
  - `C:\Users\Administrator\AppData\Local\edian+\mip_data\mip\temp`에 **`_fc9a1245-733c-46bb-a618-dc68cdf47bf2.bak` 노출**
- **로그(핵심)**:
  - `vfs_console_31840.log`:
    - `최종 변경사항 디스크 커밋 완료`까지는 수행됨
    - 동일 구간에서 **`JIT 실체화 수행 (v4.7s Persistent)`**가 발생(저장 마무리 단계와 충돌 가능성 ↑)
    - 이번 PID 로그에서는 **`CRITICAL EXCEPTION in CloseHandle (0xC0000008)` 미관측**
  - `application.log`:
    - `beginSave`(QSAVE)까지는 도달하나, 이후 정상 완료 흐름이 보이지 않음(저장 실패 처리로 분기된 것으로 추정)

### C) 판정/결론

- 본 케이스는 “닫기(MIP 적용)” 단계 이전에 **저장(QSAVE) 단계에서 원본 저장이 실패**하는 케이스로,
  - 닫기(MIP)까지 진행해도 원인 분리가 어려우므로 **저장 단계에서 중단**이 타당
- `CloseHandle 0xC0000008`는 이번 재현에서 핵심 증상으로 관측되지 않았고,
  - 저장 실패의 1순위는 **ZWCAD 저장 원자교체(Replace/Move/Copy) 시퀀스**와
    Native의 **JIT 실체화(독점 핸들/실물 노출 정책)**가 충돌하는 방향이 더 유력

### D) 다음 액션(안전 순서)

- **L3(SaveExposed) 트랙 착수**:
  - QSAVE 구간(`zws*.tmp` 관측 구간)에서 **JIT 실체화/독점 락 정책을 저장 시퀀스에 맞게 조정**
  - 저장 완료 후 “디스크만 기화(vaporize disk only)”로 복귀하는 형태로 수렴(설계 계약: `ROADMAP_PHASE2_VFS_LIFECYCLE.md`)
  - “프리징 0 게이트” 유지(조금이라도 프리징 시 즉시 롤백)

---

## 4.4 (추가 기록) 2026-05-28 17:23 QSAVE 실패 재현 — L3(JIT 억제) 1차 시도 무효(조건 미발동)

### A) 작업 내용(패치 요약)

- **목적**: QSAVE 저장 마무리(원자 교체) 구간에서 `_uuid.dwg`에 대한 **JIT 실체화(share=0 독점)**가 발생하면
  “DWG 저장 불가 → TMP 저장”으로 갈 수 있어, `zws*.tmp` sidecar I/O 직후에는 JIT 실체화를 **5초간 억제**하려고 함.
- **변경 파일**: `eDIAN.Hook.Native\\FileHooks.cpp`
- **핵심 로직**:
  - `mip\\temp\\zws*.tmp` 경로 접근 시각을 `g_lastZwcadSaveSidecarTick`에 저장
  - `_uuid.dwg`의 JIT 실체화 진입 시, (now-lastSidecar)<5000ms면 `"[SAVE-IO] JIT 실체화 억제"` 로그를 남기고 JIT를 스킵
- **빌드**: (VS Debug|x64) 빌드 후 테스트 수행

### B) 실측 결과

- **시점**: 2026-05-28 **17:23**
- **PID**: ZWCAD **16564**
- **표면 증상**: 사용자 제보 — **“DWG 저장 불가 → TMP 저장” 메시지 재발**
- **Managed 로그(`application.log`)**:
  - `beginSave` → `saveComplete`는 발생함 (즉, 프리징은 아니며 저장 명령 자체는 완료 이벤트가 찍힘)
- **Native 로그(`vfs_console_16564.log`) 핵심**:
  - `zwsD731__...tmp` 생성/기화 이후 **커밋 완료**까지 수행됨
  - 그 직후에도 **`JIT 실체화 수행 (v4.7s Persistent)`**가 그대로 발생함
  - 기대했던 `"[SAVE-IO] JIT 실체화 억제"` 로그가 **전혀 없음**

### C) 판정/해석

- L3 1차 시도는 “저장 실패”를 막지 못했고, 무엇보다 **억제 조건 자체가 발동하지 않음**이 로그로 확인됨.
- 원인 후보:
  - `zws*.tmp` 접근 시각 기록이 현재 코드에서 `engine.IsManifestingPath(lpFileName)` 분기 내부에 있어,
    실제 QSAVE sidecar 경로가 그 분기를 타지 않아 `g_lastZwcadSaveSidecarTick`가 갱신되지 않았을 가능성.

### D) 다음 액션(최소 변경 유지)

- `zws*.tmp` 감지/타임스탬프 기록을 **CreateFileW 진입 초기에(분기 밖)**로 이동해 “조건 미발동”을 먼저 해결한 뒤 재실측.

---

## 4.5 (추가 기록) 2026-05-28 17:29 QSAVE 실패 재현 — L3(JIT 억제) 조건 발동했지만 증상 유지

### A) 작업 내용(패치 요약)

- **목적**: 4.4에서 확인된 “조건 미발동”을 제거하여, 저장(QSAVE) 구간에서 JIT 실체화 억제가 실제로 동작하는지 검증
- **변경 파일**: `eDIAN.Hook.Native\\FileHooks.cpp`
- **변경 요약**:
  - `IsZwcadSaveSidecarPath(zws*.tmp)` 감지 시각(`g_lastZwcadSaveSidecarTick`) 기록 위치를
    `engine.IsManifestingPath()` 분기 내부가 아니라 **`CreateFileW` 진입 초기에 무조건 기록**하도록 이동
- **기대 관측**: `vfs_console_{PID}.log`에 `"[SAVE-IO] JIT 실체화 억제(저장 구간, ...ms)"` 라인이 찍혀야 함

### B) 실측 결과

- **시점**: 2026-05-28 **17:29** (사용자 제보: 17:23 테스트 후 재실측)
- **PID**: ZWCAD **30236**
- **표면 증상**: 사용자 제보 — **“DWG 저장 불가 → TMP 저장” 메시지 지속**
- **Managed 로그(`application.log`)**:
  - `beginSave` → `saveComplete` 정상 발생 (프리징 아님)
- **Native 로그(`vfs_console_30236.log`) 핵심**:
  - `zws715A__...tmp` 생성/기화 이후
  - `"[SAVE-IO] JIT 실체화 억제(저장 구간, 0ms)"` **로그가 실제로 발생**
  - 그럼에도 사용자 표면 증상은 그대로 유지

### C) 판정/해석

- “커밋 직후 JIT 실체화(독점 락)”이 저장 실패의 유일 원인이 아니라는 것이 확인됨.
- ZWCAD QSAVE가 사용하는 저장 마무리(원자 교체/백업 생성) 시퀀스가 VFS의 현재 정책(고스트/기화/커밋/sidecar 처리)과 충돌하고 있을 가능성이 큼.
- 다음 단계는 단일 로그만으로는 한계가 있어, **Procmon(선택)으로 QSAVE 구간의 실제 Win32 호출 순서(Replace/Move/Copy/SetDisposition/Rename) 확정**이 필요.

### D) 다음 액션(제안)

- **Procmon 캡처(필요 시)**: QSAVE 클릭 전후 10~20초, `ZWCAD.exe`만 필터 → `CreateFile/CopyFile/MoveFileEx/ReplaceFile/Rename/SetDisposition` 중심으로 저장 시퀀스 확정
- 확정된 시퀀스에 맞춰 `ROADMAP_PHASE2_VFS_LIFECYCLE.md`의 **L3 SaveExposed(저장 구간 실물 노출 + 종료 후 디스크만 기화)**를 “정식 구현”으로 진행하는 것이 필요

---

## 4.6 (추가 기록) 2026-05-28 17:52 Procmon 캡처(1차) — 실패 이벤트/원자교체 시퀀스 미포착(재캡처 필요)

### A) 실측(Procmon)

- **시점**: 2026-05-28 **17:52** (ZWCAD PID **31660**)
- **Procmon 저장물**: `eDIAN.Main\\bin\\x64\\Debug\\logs\\Logfile.CSV`
- **관측(요약)**:
  - `_uuid.dwg` 생성이 관측됨:
    - `C:\\Users\\Administrator\\AppData\\Local\\eDIAN+\\mip_data\\mip\\temp\\_a75bf14c-8057-4c75-8f0f-8ebfaf33f948.dwg`
    - **ShareMode: None(share=0)** + `Disposition: OverwriteIf` 로 `CreateFile SUCCESS`
  - QSAVE sidecar(`zws*.tmp`) 생성/오픈이 관측됨:
    - `...\\mip\\temp\\zwsBDFD.tmp`, `zwsBDFD__{uuid}.tmp` 모두 `CreateFile SUCCESS`
  - 단, 본 CSV 내에는 **Result != SUCCESS(Access Denied/Sharing Violation 등)** 라인이 존재하지 않았고,
    `CopyFile/MoveFileEx/ReplaceFile/Rename/SetDisposition` 류의 **원본 DWG 저장 마무리(원자교체) 시퀀스가 전혀 나타나지 않음**.

### B) Native/Managed 교차 관측(동일 PID)

- `vfs_console_31660.log`:
  - `"[SAVE-IO] JIT 실체화 억제(저장 구간, 0ms)"` **발동**
  - `최종 변경사항 디스크 커밋 완료` **2회** 관측 (저장 구간에서 커밋 자체는 성공)
- `application.log`:
  - `QSAVE`에 대해 `beginSave` → `saveComplete` 이벤트가 기록됨

### C) 판정/해석

- 이번 Procmon CSV는 “표면 증상(저장 불가→TMP 저장)”의 **직접 트리거(실패 Operation/Result)** 를 특정하기에 정보가 부족하다.
  - 즉, **저장 실패를 유발한 Create/Write/Rename/Replace의 실패 이벤트가 캡처/내보내기 단계에서 누락**되었을 가능성이 높다.
- 결론적으로, 현재 데이터만으로는 “VFS의 어떤 훅/정책이 원본 저장을 막았는지”를 확정할 수 없다.

### D) 다음 액션(재캡처 권장 조건)

- Procmon은 가능하면 **PML(원본)** 로 저장하고, 필터는 `ProcessName=ZWCAD.exe`만 유지한 상태에서
  - `CreateFile`, `WriteFile`, `SetEndOfFile`, `CloseFile`, `SetRenameInformationFile`, `SetDispositionInformationFile` 중심으로
  - **원본 DWG 경로(`D:\\test_document\\test_01.dwg`)까지 포함**되도록 재캡처가 필요.

---

## 4.7 (추가 기록) 2026-05-28 18:10~18:15 재실측 + Procmon CSV(TestLog_01) — QSAVE/저장 실패 트리거 미확정(Write/Rename 계열 미포착)

### A) 실측(Procmon)

- **Procmon 저장물**: `eDIAN.Main\\bin\\x64\\Debug\\logs\\TestLog_01.CSV`
- **PID**: ZWCAD **27156**
- **관측(요약)**:
  - 원본 DWG: `D:\\test_document\\test_01.dwg`에 대한 `CreateFile SUCCESS` 다수 관측
  - MIP temp 구간에서 `_uuid.dwg` 생성(share=0) 관측:
    - `...\\mip\\temp\\_32d52fba-52aa-4723-a4da-60f1f30efcd7.dwg` `CreateFile SUCCESS` (ShareMode: None)
  - **QSAVE sidecar** `zws*.tmp` 생성/기화 관측
  - **Result != SUCCESS**는 존재하나(예: `NAME NOT FOUND`, `NAME COLLISION`, `IS DIRECTORY`, `FILE LOCKED WITH ONLY READERS`) 대부분이
    설정/리소스 탐색 또는 디렉터리 처리에 해당하며,
    “DWG 저장 불가 → TMP 저장”을 직접 유발한 것으로 볼 수 있는
    `ACCESS DENIED`/`SHARING VIOLATION` 또는 `Rename/Replace/Disposition` 실패를 **확정할 수 없었음**.

### B) Native/Managed 교차 관측

- `vfs_console_27156.log`:
  - `"[SAVE-IO] JIT 실체화 억제(저장 구간, 0ms)"` 발동
  - `최종 변경사항 디스크 커밋 완료` 반복 관측(저장 구간 커밋 자체는 성공)
- `application.log`(tail 기준):
  - `QUIT` 흐름에서 `beginSave`는 관측되나, 해당 구간의 `saveComplete`는 **확인되지 않음**(저장 실패 처리/중단 분기 가능성)

### C) 판정/다음 액션

- 이번 데이터로는 **저장 실패의 직접 트리거(실패 Operation/Result/Detail)** 를 특정할 수 없으며,
  다음 재캡처에서는 최소한 아래가 포함되어야 한다:
  - **Operation**: `WriteFile`, `SetEndOfFile`, `CloseFile`, `SetRenameInformationFile`, `SetDispositionInformationFile` (필수)
  - **저장 포맷**: 가능하면 **PML(원본)** 권장(내보내기(CSV)에서 누락/축약 리스크 감소)

---

## 4.8 (추가 기록) 2026-05-28 18:30 PML 저장(TestLog_01.PML) — File System 이벤트 미수집(프로파일링만 기록됨)

### A) 현상

- 사용자 재실측 후 `TestLog_01.PML`을 확보했으나, PML을 CSV/XML로 내보내 확인한 결과
  - `Operation`이 **`Process Profiling`만 존재**하고,
  - `CreateFile/WriteFile/CloseFile/SetRenameInformationFile/...` 등 **File System 이벤트가 전혀 존재하지 않음**이 확인됨.

### B) 판정

- 본 PML은 “DWG 저장 불가 → TMP 저장”의 직접 트리거(ACCESS DENIED/SHARING VIOLATION, rename/replace 실패 등)를 복원할 수 없으므로,
  **분석 자료로는 부적합**.

### C) 다음 액션(재캡처 필수 조건)

- Procmon에서 **File System 이벤트 클래스가 실제로 캡처되도록 설정**한 뒤 재캡처가 필요:
  - `CreateFile/WriteFile/CloseFile/SetRenameInformationFile/SetDispositionInformationFile/SetEndOfFile`가 PML에 포함되는지 먼저 확인
  - (필요 시) Profiling 이벤트는 비활성화하여 노이즈를 줄이고, `Drop Filtered Events`를 켜서 로그량을 통제

---

## 4.9 (추가 기록) 2026-05-28 19:09 Procmon PML(Logfile.PML) 재저장 성공 — File System 이벤트 수집 확인(단, 원본 DWG 경로 이벤트는 미포착)

### A) Procmon 저장 설정 교정

- `File > Save...`에서 **`Also include profiling events` 체크 해제** 후,
  `Events displayed using current filter` + `PML`로 재저장하여
  `Process Profiling`만 남던 문제를 해소함.

### B) PML 내 관측(요약)

- `Logfile.PML`을 CSV로 변환(`Logfile.fromPML.csv`)하여 확인한 결과:
  - `ZWCAD.exe`의 `CreateFile` 등 **File System 이벤트가 정상적으로 포함**됨
  - MIP temp 구간에서 `_uuid.dwg` 생성(share=0), LocalTemp sidecar 생성 등이 관측됨
  - 그러나 본 캡처에는 `D:\\test_document\\test_01.dwg` 같은 **원본 DWG 경로 이벤트가 포함되지 않아**, “DWG 저장 불가 → TMP 저장” 트리거를
    원본 저장 관점(원자교체/Rename/Disposition 실패 등)으로는 아직 확정할 수 없음.
  - `ACCESS DENIED`/`SHARING VIOLATION` 계열 실패 이벤트는 본 캡처 범위에서 관측되지 않음.

### C) Managed/CloseFlow 교차(동일 시각대)

- `close_flow.log`에서 `ApplyProtectionCommitDone`/`ApplyProtectionEnd success=True`가 관측되어,
  본 시각대(19:05~19:09)는 **닫기(MIP 적용) 단계는 성공한 흐름**으로 보임.

### D) 다음 액션(추가 캡처 조건)

- 저장 실패 메시지(“DWG 저장 불가 → TMP 저장”)를 재현한 **동일 테스트에서**
  - 필터의 `Path contains`에 `D:\\test_document\\`(디렉터리 단위) 포함을 권장하고,
  - `SetRenameInformationFile`/`SetDispositionInformationFile`뿐 아니라 `WriteFile`/`CloseFile`까지 포함하여
  **원본 DWG 저장 마무리 시퀀스**를 반드시 확보해야 함.

### E) (동일 PML 추가 관측) 19:09~19:10 temp 내부 Rename 시퀀스 확인

- **PML**: `eDIAN.Main\\bin\\x64\\Debug\\logs\\Logfile.PML` (PID **22544**)
- **내보내기**: `Logfile.fromPML.csv` (총 **4130** rows, `CreateFile 4127`, `SetRenameInformationFile 3`)
- **관측(핵심)**: QSAVE 구간에서 `mip\\temp` 내부에서 **Rename(=SetRenameInformationFile)** 시퀀스가 발생함(모두 `SUCCESS`):
  - `zwsCC71.tmp` → `zwsCC71__{uuid}.tmp`
  - `_ef1b53c4-...761e.dwg` → `zwTm298560` → `_ef1b53c4-...761e.bak`
  - 이는 ZWCAD가 저장 구간에서 sidecar/tmp 및 bak 파일을 **rename 기반**으로 정리/확정하는 흐름을 사용함을 시사.
- **미관측(중요)**:
  - `ACCESS DENIED` / `SHARING VIOLATION` 등 “저장 불가 → TMP 저장”을 직접 유발하는 실패 이벤트가 **해당 CSV에서는 관측되지 않음**
  - 특히 **원본 DWG 경로(`D:\\test_document\\test_01.dwg`)가 필터에 포함되지 않아**, 원본 저장 실패 트리거를 본 PML만으로는 확정할 수 없음.
- **Native 교차(`vfs_console_22544.log`)**:
  - `19:09:56` 이후 `_ef1b53c4-...761e.dwg`에 대해
    - 흡수/기화 → `zwsCC71__{uuid}.tmp` 생성/기화
    - 커밋 완료(33235 bytes)
    - `"[SAVE-IO] JIT 실체화 억제"` 발동
    까지 확인됨.

---

## 4.10 (추가 기록) 2026-05-28 19:34 Procmon CSV 확보 — MIP temp 내부 Rename/BAK 시퀀스 확인(원본 DWG 경로는 미포착)

### A) Procmon 저장물

- **파일**: `D:\\Tools\\ProcessMonitor\\Logfile.CSV`
- **PID**: ZWCAD **15924**

### B) 관측(요약)

- MIP temp에서 `_uuid.dwg` 생성(share=0)과 LocalTemp sidecar(`zwTestTmp*`, `ZL*.zw$`) 생성이 관측됨.
- `zws*.tmp` sidecar에 대해 **Rename 시퀀스가 명확히 관측**됨:
  - `SetRenameInformationFile`:
    - `...\\mip\\temp\\zws6E1F.tmp` → `...\\mip\\temp\\zws6E1F__{uuid}.tmp` (**SUCCESS**)
- `_uuid.dwg`에 대해 **Rename + BAK 생성 시퀀스가 관측**됨:
  - `SetRenameInformationFile`:
    - `...\\mip\\temp\\_8f1c...b22.dwg` → `...\\mip\\temp\\zwTm539200` (**SUCCESS**)
    - `...\\mip\\temp\\zwTm539200` → `...\\mip\\temp\\_8f1c...b22.bak` (**SUCCESS**)
- 본 CSV 내에서 `ACCESS DENIED`/`SHARING VIOLATION`은 관측되지 않음.

### C) 판정

- 이번 Procmon 캡처는 “저장 마무리의 일부(Rename/BAK)”를 보여주지만,
  `D:\\test_document\\` 경로 이벤트가 포함되지 않아
  “DWG 저장 불가 → TMP 저장”의 **원본 DWG 저장 실패 트리거**를 아직 확정할 수 없음.

---

## 4.11 (추가 기록) 2026-05-28 20:02 Procmon CSV(Logfile.CSV) 분석 — `_uuid.dwg` “생성 직후 NAME NOT FOUND” 패턴 + MIP temp Passthrough 패치 적용

### A) 입력 자료

- **파일**: `D:\\Tools\\ProcessMonitor\\Logfile.CSV`
- **PID**: ZWCAD **15924**
- **사용자 메시지(표면 증상)**:
  - `...\\mip_data\\mip\\temp\\{uuid}.dwg`에 저장 실패 → `zwsv*.tmp`로 저장되었다는 메시지

### B) 관측(Procmon CSV 핵심)

- `...\\mip_data\\mip\\temp\\_5fc2aaa7-...d614.dwg`에 대해:
  - `CreateFile SUCCESS`로 **Created** 된 직후,
  - 바로 이어지는 `CreateFile(Open, OpenReparsePoint)`가 **`NAME NOT FOUND`** 로 반복됨.
  - 이후 저장 마무리 구간에서 rename 기반 시퀀스가 관측됨:
    - `_uuid.dwg` → `zwTm111530` (**SUCCESS**)
    - `zwTm111530` → `_uuid.bak` (**SUCCESS**)
    - `zwsFB64.tmp` → `zwsFB64__{uuid}.tmp` (**SUCCESS**)

### C) 1차 해석(원인 후보)

- `_uuid.dwg`가 “생성 직후 사라지는(NAME NOT FOUND)” 패턴은
  - VFS가 `mip_data\\mip\\temp\\*.dwg`까지 **Target로 오인해 흡수/기화(삭제)** 하는 경우와 정합성이 높다.
- 특히 Native `FileHooks.cpp`는 `engine.IsTarget()` 분기에서
  - 실물 DWG가 존재하면 읽어 흡수한 뒤 `TrueDeleteFileW()`로 **기화(삭제)** 를 시도한다.
  - `mip\\temp` 작업 파일까지 이 분기를 타면 ZWCAD/MIP가 기대하는 rename/backup/commit 체인을 망가뜨릴 수 있다.

### D) 조치(패치) — `mip_data\\mip\\temp\\` 경로 Passthrough

- **목적**: `mip_data\\mip\\temp\\`는 MIP/ZWCAD가 사용하는 작업 디렉터리이므로,
  - VFS 가상화/기화를 적용하지 않고 **Manifesting path(=passthrough)** 로 취급하여
  - `_uuid.dwg`가 “만들자마자 없어짐” 현상을 제거한다.
- **변경 파일**: `eDIAN.Hook.Native\\FileHooks.cpp`
- **변경 요약**:
  - `IsMipWorkTempPath("\\mip_data\\mip\\temp\\")` 헬퍼 추가
  - `CreateFileW`, `DeleteFileW`에서 `IsMipWorkTempPath || engine.IsManifestingPath`이면 passthrough로 처리

### E) 빌드

- `eDIAN.Hook.Native` Rebuild 성공 (Debug|x64)
- `eDIAN.Main` Rebuild 성공 (Debug|x64) — 단, 실행 중인 ZWCAD로 인해 일부 DLL 삭제 경고(MSB3061) 발생

### F) 다음 실측(필수)

- 동일 시나리오에서 “DWG 저장 불가 → TMP 저장” 메시지가 사라지는지 확인:
  - QSAVE 시 `_uuid.dwg`에 대해 `CreateFile SUCCESS` 이후에 `NAME NOT FOUND` 반복이 사라져야 함
  - 가능하면 Procmon에서 `WriteFile/CloseFile`까지 포함되도록 캡처 확장(저장 마무리 실패 트리거 확정용)


## 4.12 (추가 기록) 2026-05-28 20:40 현장 재검증(Procmon 최소화) — QSAVE/닫기/재오픈 5회 연속 성공(재발 0)

### A) 사용자 현장 판정(요약)

- **시나리오**: `test_01.dwg` 열기 → 간단 수정 → **QSAVE** → **닫기** → **재오픈**
- **반복 횟수**: **5회**
- **결과**:
  - 5회 중 **“tmp로 저장” 메시지 0회**
  - 5회 모두 **재오픈 시 수정 반영됨**
  - 원본 `test_01.dwg` **수정시간(LastWriteTime) 갱신 확인**

### B) 판정

- “DWG 저장 불가 → TMP 저장” 이슈는 **현장 기준 재현 실패(=해결로 간주 가능)** 상태로 업데이트한다.
- 추후 간헐 재발 여부는 동일 체크리스트로 재확인한다.


## 4.2 (추가 기록) 2026-05-28 결정/정리 사항(오늘 대화 핵심)

### A) Native 롤백(복원) 결정

- **결정**: “Managed(플러그인) 소스는 유지, Native만 5단계 완료 시점으로 복원”
- **복원 기준 커밋**: `9832813` (메시지: *Complete Phase 2 steps 4-5: Setup packaging, VFS activation, and Native output fix.*)
- **복원 방식**: `git checkout 9832813 -- eDIAN.Hook.Native`
- **의미**: 이후 L2/L3/L4 등 Native 변경은 **단일 변화로 재도입**하며 영향 범위를 좁힌다.

### B) 문서/기준 정리(단일 기준 확정)

- “문서/기준 혼선”을 줄이기 위해 아래 구조를 확정:
  - **설계 계약(타이밍)**: `ROADMAP_PHASE2_VFS_LIFECYCLE.md`
  - **판정·로그 수집(단일 기준)**: `ZWCAD_VFS_ENGINEERING_GUIDE.md`
  - **진행/체크리스트**: `ROADMAP_PHASE2_VFS.md`
  - **승인/환경 기록**: `PHASE2_KICKOFF_GATE.md`
  - **ZWCAD 특성 외부 조사 노트**: `ZWCAD_VFS_ZWCAD_CHARACTERISTICS_RESEARCH.md`
- 특히 Lifecycle의 로그 SOP 상세(표/PS 예시)는 Engineering Guide로 이관하여 **중복 최소화**함.

### C) “원인 1순위” 판정 업데이트(손상 vs 잠금)

오늘 재현 로그(`vfs_console_24180.log` + `application.log` + `close_flow.log` + `mip_sdk.miplog`) 기준으로,

- 1순위는 “손상된 DWG”라기보다 **MIP가 temp 파일을 열 때의 잠금/권한 문제(Permission denied)** 로 보는 것이 합리적이다.
- 동일 타임라인에서 Native의 `CloseHandle` 치명 예외(0xC0000008)가 관측되므로,
  - “CloseHandle 경로 안정화(예외 방어 + 핸들 정리 순서 보장)”가 Phase 6의 선행 과제로 강화됨.

## 5. 롤백(복원) 관점 제안

Phase 6에서 관측된 바와 같이, `FileHooks.cpp`의 저장/닫기 경로는 **작은 조건 변화도 프리징으로 직결**될 수 있다.  
따라서 “Native를 5단계 VFS ON 직후 기준선으로 복원”한 뒤,

- L2(닫기 Materialize+Release)
- L3(SaveExposed: QSAVE 중 실물 노출, 종료 후 디스크만 기화)

를 **한 번에 하나씩** 재도입하며, 위 §3의 증상(프리징/저장 실패 메시지)을 “단일 변화”로 대응하는 것이 가장 안전하다.

