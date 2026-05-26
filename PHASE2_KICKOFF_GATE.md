# Phase 2 VFS — 착수 게이트 기록

| 항목 | 내용 |
|------|------|
| **일자** | 2026-05-26 |
| **요청** | 사용자 — «착수 게이트 진행» |
| **브랜치** | `feature/phase2-vfs` |
| **로드맵** | `ROADMAP_PHASE2_VFS.md` §0, §5 0단계 |

---

## 1. 승인 범위

| 구분 | 단계 | 승인 | 비고 |
|------|------|------|------|
| **즉시 진행** | 1~3 | **승인** | Hook/Native 소스·솔루션 등록, net48 빌드 연동, `using` 정리 — **`VfsInterceptor` 비활성 유지** |
| **단계별 재확인** | 4 | **완료** | MSI 설치·Hook DLL·ZWCAD 레지스트리 LOADER 회귀 없음 (2026-05-26) |
| **단계별 재확인** | 5 | **완료** | VFS ON·ZWCAD Debug 기동·`vfs_console` ZWCAD.exe 확인 (2026-05-26) |
| **사용자 실측** | 6~7 | 박부장 | ZWCAD Plot/Publish·세션 소거·`HookConstants` 보정 |

---

## 2. AutoCAD 이관 원본 (고정)

| 용도 | 커밋 | 날짜 | 메시지 |
|------|------|------|--------|
| **Hook/Native 소스 (HEAD)** | `e24afc9538df433d65d9fa62f64a87eebd6c3324` (`e24afc9`) | 2026-05-22 | Finish edian+ for AutoCAD 2026 |
| **Managed Phase 1 포팅 기준** | 동일 `e24afc9` | 2026-05-22 | `ROADMAP_PLUGIN_BASELINE.md` §5 |
| **Native VFS 코어 (참고)** | `dd532eeb9a747cb1d4d3e22b6ec0d4a312745eb2` | 2026-05-20 | feat(vfs): Phase 3 VFS core modularization (`VfsEngine.cpp` 등) |

- **저장소**: [eDIAN-Plus-for-AutoCAD-2026](https://github.com/leavesaction/eDIAN-Plus-for-AutoCAD-2026)
- **로컬 경로**: `D:\workspace_vs\eDIAN Plus for AutoCAD 2026`
- **복사 대상**: `eDIAN.Hook\`, `eDIAN.Hook.Native\` (소스·vcxproj만, `bin`/`obj`/`x64` 빌드 산출물 제외)

> AutoCAD `main` HEAD가 `e24afc9`이므로 Phase 2 이관 원본은 **별도 태그 없이 이 커밋**을 사용한다.

---

## 3. ZWCAD Phase 1 회귀 (착수 시점)

| 검증 | 결과 | 수행 |
|------|------|------|
| **Release \| x64** MSBuild 전체 Rebuild | **성공** | 에이전트 (2026-05-26) |
| 산출물 `eDIAN.Main\bin\x64\Release\eDIAN.dll` | 생성 확인 | 동일 |
| ZWCAD 로드·팔레트·MIP·Service | **통과** | 박부장 (Phase 1 + 회귀 재확인) |
| `IS_FILE_ACL=false` 이후 실기 | **통과** | 박부장 (2026-05-26) — 보호 DWG 열기 등 |
| **실기 회귀 (0단계)** | **완료** | 박부장 확인 (2026-05-26) |

### 3.1 `IS_FILE_ACL=false` 실기 시 유의 (기록)

- 과거 `IS_FILE_ACL=true` 사용 시 `…\mip_data\mip\temp`에 **Everyone Deny** ACL이 남을 수 있음.
- `false`로 전환해도 코드는 ACL을 **제거하지 않음** → MIP `Permission denied` / temp 접근 거부 가능.
- **조치**: temp 폴더 보안에서 Everyone( Deny ) 규칙 수동 제거 후 정상 (본 PC 실측).

**빌드 명령** (기록용):

```powershell
cd "D:\workspace_vs\eDIAN Plus for ZWCAD 2026"
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" /t:Rebuild /p:Configuration=Release /p:Platform="x64" /m /v:m
```

---

## 4. 실측 환경

| 항목 | 값 |
|------|-----|
| ZWCAD 설치 | `C:\Program Files\ZWSOFT\ZWCAD 2026\` |
| 실행 파일 | `ZWCAD.exe` — **26.11.0.20039** |
| Managed 참조 | `ZwManaged.dll`, `ZwDatabaseMgd.dll` (동일 경로) |
| 플랫폼 | **x64** |
| `PARENT_PROGRAM_VERSION` | `ZWCAD 2026` → Native **ZWCAD 프로필** (`HookConstants`) |

### 4.1 Phase 6 실측 시나리오 (체크리스트)

- [ ] 보호 DWG 열기·저장·닫기·재보호
- [ ] Plot / Publish (부속 exe Procmon → `ZWCAD_TRUSTED_PROCESSES`)
- [ ] `%LocalAppData%\...\temp\` 경로 샘플 → `ZWCAD_TEMP_PATTERNS`
- [ ] 정상 종료·강제 종료 후 VFS 세션 폴더 소거
- [ ] `eDIAN.Service.exe` Pipe 단절 후 소거 (~600ms, Stealth VFS 기준)

---

## 5. 개발 환경

| 항목 | 상태 |
|------|------|
| Visual Studio 18 MSBuild (amd64) | 사용 (`build_standard.mdc`) |
| C++ Desktop (Native vcxproj) | 이관 1단계부터 필요 — **VS에 v143/x64 네이티브 도구 확인** |
| Git 브랜치 `feature/phase2-vfs` | 생성·push |

---

## 6. 다음 작업

**다음** — 로드맵 **6단계**: Plot/Publish Procmon, `HookConstants` ZWCAD 프로필, 세션 폴더 소거.

---

*Gate: 0~4 완료, 5단계 VFS ON 코드 반영 (2026-05-26). 브랜치 `feature/phase2-vfs`.*
