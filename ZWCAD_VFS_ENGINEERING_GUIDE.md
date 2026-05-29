# ZWCAD VFS Engineering Guide (PhantomVfs) — 단일 기준

본 문서는 **eDIAN Plus for ZWCAD 2026**에서 Phase 2 VFS(PhantomVfs)를 운영·튜닝·실측할 때의 **단일 기준**입니다.  
AutoCAD 저장소 `D:\workspace_vs\eDIAN Plus for AutoCAD 2026\.document\` 문서들을 **ZWCAD 실측 관점으로 재구성**했습니다.

## 관련 문서(역할 분담)

- **진행 상태/마일스톤**: `ROADMAP_PHASE2_VFS.md`
- **설계 계약(실물 노출·기화·종료)**: `ROADMAP_PHASE2_VFS_LIFECYCLE.md`
- **착수/승인·환경 기록**: `PHASE2_KICKOFF_GATE.md`
- **외부 조사(ZWCAD 특성 노트)**: `ZWCAD_VFS_ZWCAD_CHARACTERISTICS_RESEARCH.md`

## 0. 목적

- **실측(박부장) / 개발(에이전트)** 간 “성공 판정·원인 분류·로그 수집”을 표준화한다.
- **프리징/크래시 방지**를 1순위로 두고, 기능 개선은 그 다음 단계로 제한한다.
- “디스크 I/O가 보이면 실패” 같은 오해를 막기 위해, **의도된 디스크 I/O**와 **비정상 우회**를 구분한다.

## 1. 시스템 구성(요약)

- **Managed**: `eDIAN.Main`(net48, ZWCAD API) + `eDIAN.Hook`(P/Invoke)
- **Native**: `eDIAN.Hook.Native.dll` (PhantomVfs) — **IAT 후킹 19 API**, MMF 기반 가상 편집, 고스트 핸들, manifest/JIT, 세션 소거

### 1.1 Native 설정 주입(Host/Trusted/TempPattern)

- `CommonConstants.PARENT_PROGRAM_VERSION = "ZWCAD 2026"`
- `HookConstants.IsZWCAD()` → `GetNativeConfigString()` → `NativeMethods.InitializeVfs(..., lpConfigString, ...)`
- Native: `VfsEngine::Initialize()`가 `lpConfigString`을 파싱해 `AccessGuard`에 적용

> 본 저장소 기준으로 “ACAD 프로필이 주입되어 문제”는 코드 경로상 성립하지 않는다.

## 2. 후킹 표면(19 API) — 무엇을 믿고 무엇을 의심해야 하나

**후킹 대상(19개)**은 `DetoursInterceptor.cpp`의 `g_HookEntries[]`가 단일 기준이다.  
핵심은 아래 3범주다.

- **핸들 기반**(가상 I/O 핵심): `CreateFileW`, `ReadFile`, `WriteFile`, `SetFilePointerEx`, `GetFileSizeEx`, `GetFileType`, `CloseHandle`, `CreateFileMappingW`
- **경로 기반**(원자 저장/정리): `GetFileAttributesW`, `DeleteFileW`, `MoveFileExW`, `ReplaceFileW`, `CopyFileW`, `CopyFileExW`, `MoveFileW`, `RemoveDirectoryW`
- **프로세스/세션**: `CreateProcessW`, `TerminateProcess`, `ExitProcess`

### 2.1 사각지대(중요)

- **ANSI API**(`CreateFileA`, `fopen` 등)는 **미후킹**이다. LISP/구형 모듈이 우회하면 VFS가 기대한 경로로 흘러가지 않을 수 있다.
- `NtCreateFile` 같은 **저수준 진입점**도 미후킹이다.

## 3. 디스크 I/O 판정 규칙(ETW/Procmon/로그 공통)

디스크에 흔적이 보인다고 항상 실패가 아니다. 아래 분류로 판정한다.

- **[A] 의도된 실파일 I/O (정상)**  
  - 고스트 `GHT*.tmp` (DELETE_ON_CLOSE)  
  - manifest/Plot/Publish 경로 (`zwpublish_`, `zwplot_` 등)  
  - CloseHandle의 `.dwg` 커밋(CREATE_ALWAYS)  
  - 세션/메타파일(`.vfs_metadata`) 생성·소거
- **[B] 비대상 확장자 passthrough (정책상 허용, 보안 검토 대상)**  
  - `mip\temp` 아래 `.ies` 등 (IsTarget 목록 밖)
- **[C] 신뢰 프로세스/비보호 경로 I/O (정상 가능)**  
  - `ZwPublish.exe` 등 (단, env 상속/세션 일치 여부는 별도 확인)
- **[D] 비정상(조사 필요)**  
  - IsTarget `.dwg/.tmp`인데 Hook/MMF 경유 로그가 거의 없고, 원본 경로에 대량 Write  
  - ANSI/저수준 우회, 또는 후킹 설치/재패치 실패 가능

## 4. 검증 시나리오(수렴/회귀) — ZWCAD 버전

아래는 “실측 합격” 기준이다. 실패 시 §5 SOP로 로그를 모아 원인 분류한다.

### P0(필수): 안정성·기본 기능

- **P0-1 후킹 설치 확인**: `vfs_console_{PID}.log`에 `VFS Engine Restructured` 헤더
- **P0-2 보호 DWG 열기** (인계 **I1**): `실물 데이터 흡수`·기화 후 고스트 편집; Open 직후~편집 중 `_uuid.dwg` 디스크 실물 **없음**(또는 극단적 단시간)
- **P0-3 편집 → QSAVE/SAVE → 닫기** (인계 **I3·I5·I6**): QSAVE 중만 실물·완료 후 디스크 기화(`[SAVE-IO]`); 닫기 `[CLOSE]` materialize + `close_flow` ApplyProtection **성공**; 재오픈 시 변경 유지; MIP 후 temp·dwl 삭제
- **P0-4 자식 프로세스 세션 상속**(Publish/Plot): 자식에서 `Inherited Dynamic Session Sandbox from Environment` 또는 최소 동일 세션 폴더 사용
- **P0-5 비신뢰 Copy 차단**: 탐색기/외부에서 temp 복사 실패(`[Security Guard]`)
- **P0-6 종료 소거**: 종료 시 `[Vaporize]` 및 세션 폴더 삭제

> 인계 조건 정의: `ROADMAP_PHASE2_VFS_LIFECYCLE.md` §3.2 (I1~I6). P0 실패 시 §5 로그 교차 검증으로 **어느 인계가 깨졌는지** 분류한다.

### P1: 저장/Plot 강화

- **P1-1 QSAVE sidecar**: ZWCAD `zws*.tmp` 사용 구간에서 프리징 없이 완료(※ “프리징 0 게이트” 참조)
- **P1-2 MoveFileExW atomic save**: 저장 시 storage 키 리디렉션이 정상 동작
- **P1-3 zwpublish/zwplot manifest**: Plot/Publish 시 manifest 경로에서 충돌/Sharing violation 없이 완료

### P2: 안정성(스트레스)

- 멀티 탭 동시 Open, 대용량 DWG, Taskkill 후 재기동, 후킹 후 DLL 추가 로드(재패치)

## 5. 로그 교차 검증 SOP(실측 실패 시)

원칙: 단일 로그만으로 단정하지 않는다. **같은 시각·같은 temp UUID·같은 CorrelationId**로 묶는다.

- **로그 위치(단일 기준)**:

| 로그 | 경로 | 주로 보는 것 |
|------|------|-------------|
| **Native VFS** | `eDIAN.Main\bin\x64\Debug\logs\vfs_console_{PID}.log` | `[CLOSE]`, `[SAVE-IO]`, CopyFile, materialize, 바이트 수 |
| **Managed** | `...\logs\application.log` | `openDocumentFile`, `applyProtectionToTempFile`, UI 메시지 대응 스택 |
| **닫기 흐름** | `...\logs\close_flow.log` | `[CLOSE-FLOW]` 단계, ApplyProtection 성공/실패 |
| **MIP SDK** | `%LocalAppData%\edian+\mip_data\mip\logs\mip_sdk.miplog` | `file_commit_async`, `OpenInput`, FileIOError, CorrelationId |
| **(선택) Procmon** | (수동 캡처) | 실물 존재·순서 확정이 꼭 필요할 때만 |

### 5.1 증상 → 로그 매핑(빠른 분류)

| 사용자 증상 | 1차 | 2차 | 3차 | VFS에서 확인할 키워드 |
|-------------|-----|-----|-----|----------------------|
| 저장 후 닫기, 재오픈 시 **변경 없음** | `close_flow` ApplyProtectionFailed | `application` `applyProtectionToTempFile` IOException | `mip_sdk` Failed to open file | `[CLOSE]` 선행 바이트 수 vs CopyFile 바이트 수 |
| 팔레트 닫기, MIP 실패 | `close_flow` | `mip_sdk` | `vfs_console` | temp 실물 없음 시점 |
| **저장 후 닫기**, 재오픈 **오류** | `application` `openDocumentFile` + `Document.Open` | `mip_sdk` `file_commit_async` Commit.Result | `vfs_console` 닫기 직전 | commit 성공 vs CAD 거부(Argument/COMException) |
| EPDF 정상 | `application` EXPORTPDF | (닫기 MIP 경로 아님) | — | `[EXTERNAL]` 구간, `[CLOSE]`와 분리 |
| 레이블 적용 정상 | `application` pdwg 경로 | `mip_sdk` | — | temp 실패해도 우회 가능 |

### 5.2 Managed 메시지 ↔ 코드(빠른 탐색)

| UI 메시지(리소스) | application.log | 코드 위치 |
|-------------------|-----------------|-----------|
| 도면 파일을 여는 중 오류… | `getProtectionInfo Excception` @ `Document.Open` | `ProtectionController.openDocumentFile` (~772) |
| (닫기) MIP 실패 | `applyProtectionToTempFile` | `ProtectionController.applyProtectionToTempFile` (~1608) |
| 도면 파일을 열 수 없습니다 | `Failed to open document` | `ProtectionController.openDocumentFile` |

### 5.3 교차 검증 체크리스트(보고 시 포함)

1. **타임라인** — 저장/닫기/재오픈 각각 초(가능하면 ms)  
2. **temp UUID** — 전 로그에서 동일 파일명(`_uuid.dwg`) 사용 여부  
3. **바이트 수** — VFS `[CLOSE]`/CopyFile vs MIP `Input.FileSize`  
4. **결론 분류** — VFS 타이밍 / MIP 파일 없음 / MIP commit 후 CAD 거부 / 기타  
5. **Procmon** — 순서·실물 확정이 필요할 때만 명시

### 5.4 PowerShell 빠른 검색 예

```powershell
$uuid = "bded0629"   # 실측 temp 일부
$logs = @(
  "D:\workspace_vs\eDIAN Plus for ZWCAD 2026\eDIAN.Main\bin\x64\Debug\logs\application.log",
  "D:\workspace_vs\eDIAN Plus for ZWCAD 2026\eDIAN.Main\bin\x64\Debug\logs\close_flow.log",
  "D:\workspace_vs\eDIAN Plus for ZWCAD 2026\eDIAN.Main\bin\x64\Debug\logs\vfs_console_25288.log",
  "$env:LOCALAPPDATA\edian+\mip_data\mip\logs\mip_sdk.miplog"
)
$logs | ForEach-Object {
  Write-Host "=== $_ ==="
  Select-String $_ -Pattern $uuid,"ApplyProtection","\[CLOSE\]","\[SAVE-IO\]","openDocumentFile","Failed to open"
}
```

> “실물 노출·기화 타이밍 계약”은 `ROADMAP_PHASE2_VFS_LIFECYCLE.md`가 설계 단일 기준이다. 본 문서는 **판정/수집** 기준만 제공한다.

## 6. “프리징 0” 게이트(최우선 규칙)

다음 중 하나라도 발생하면 해당 빌드는 **즉시 폐기/롤백 후 재설계**한다.

- QSAVE/SAVE 클릭 시 CAD UI 프리징/미응답
- `application.log`가 `beginSave`에서 끊기고 `saveComplete`로 진행되지 않음(저장 경로 정지)

기능 개선은 이 게이트 통과 후에만 진행한다.

---

## 출처(읽기 전용, AutoCAD repo)

- `.\.document\[설계]PhantomVfs_설계서.md`
- `.\.document\[설계]PhantomVfs_검증_시나리오.md`
- `.\.document\[분석]Windows_IO_API_Guide.md`
- `.\.document\[분석]CAD_IO_Trace_Analysis.md`
- `.\.document\[최종]PhantomVfs 구축 결과 보고서.md`
- `.\.document\[분석]AutoCAD 와 ZWCAD의 가상화 호환성 보고서.md`

