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
- **확정 실패**:
  - “커밋 조건 축소(isModified only)”는 저장 프리징을 유발 → **폐기**
- **미해결(다음 단계에서 재설계 필요)**:
  - 저장 시 `.dwg` 저장 실패 → `.tmp`로 저장 메시지
  - 보호 DWG 닫기→재오픈 무효/손상(본 기록에서는 상세 로그를 재정리하지 않았으며, 이후 Native 롤백 후 L2/L3를 단일 변경으로 재도입하며 재현/확정 필요)

## 5. 롤백(복원) 관점 제안

Phase 6에서 관측된 바와 같이, `FileHooks.cpp`의 저장/닫기 경로는 **작은 조건 변화도 프리징으로 직결**될 수 있다.  
따라서 “Native를 5단계 VFS ON 직후 기준선으로 복원”한 뒤,

- L2(닫기 Materialize+Release)
- L3(SaveExposed: QSAVE 중 실물 노출, 종료 후 디스크만 기화)

를 **한 번에 하나씩** 재도입하며, 위 §3의 증상(프리징/저장 실패 메시지)을 “단일 변화”로 대응하는 것이 가장 안전하다.

