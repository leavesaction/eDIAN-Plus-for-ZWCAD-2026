# eDIAN Plus for ZWCAD 2026 — Agent 지침

Cursor 에이전트가 **본 워크스페이스(ZWCAD 솔루션)** 에서 따를 최소 SOP입니다.  
PhantomVfs 설계·AutoCAD VFS 실측은 **별도 저장소**를 참조합니다.

## 역할

| 역할 | 설명 |
|------|------|
| **사용자 (박부장)** | 우선순위·승인·**ZWCAD 실측 검증** (빌드·현장 테스트) |
| **에이전트** | ZWCAD **Managed 플러그인** 구현·이관·로그 분석. Phase 2 시 Hook/Native 이관 지원 |

## 본 솔루션 범위

| 항목 | 내용 |
|------|------|
| **솔루션** | `eDIAN Plus for ZWCAD 2026.sln` |
| **프로젝트** | Main, Data, Core, Service, Service.Core, Setup — **Hook / Hook.Native 없음** (Phase 1) |
| **TFM** | .NET Framework **4.8** (`packages.config`, ZWCAD `ZwSoft.*` HintPath) |
| **UI** | XAML `MainForm` + `PluginFormManager` (WinForm UI 미사용) |
| **Phase 1** | **완료** — VFS 없이 MIP·팔레트·Service 동작 확인 |
| **Phase 2** | `eDIAN.Hook` + `eDIAN.Hook.Native` 이관 — **착수 전 사용자 확인** |

## ZWCAD에서 반드시 유지 (덮어쓰기 금지)

1. **`PluginInitializer.preloadAssembly`** — `Microsoft.IdentityModel.Abstractions` 8.16.0.0 (`31bf3856ad364e35`) + `app.config` bindingRedirect  
2. **`eDIAN.Setup.vdproj`** — ZWSOFT 레지스트리, `bin\x64\Release` 배포 경로 (AutoCAD Setup으로 **교체하지 않음**)  
3. **net48 / packages.config / ZWCAD DLL HintPath** — AutoCAD(net8) csproj·PackageReference 패턴으로 **일괄 이관하지 않음**  
4. **`VfsInterceptor`** — Phase 1 동안 `PluginApplication`에서 **비활성(주석)** 유지. Phase 2에서만 활성화  

## AutoCAD 저장소 (참조만)

| 용도 | 경로 |
|------|------|
| 플러그인 baseline·로드맵 | `D:\workspace_vs\eDIAN Plus for AutoCAD 2026\ROADMAP_PLUGIN_BASELINE.md` |
| PhantomVfs·분석 설계 | `...\AutoCAD 2026\.document\` |
| Native·VFS 소스 (Phase 2 원본) | `...\AutoCAD 2026\eDIAN.Hook`, `eDIAN.Hook.Native` |

본 워크스페이스에 `.document`가 없으면 위 경로를 **명시**해 달라고 요청하거나 사용자가 파일을 열어 둡니다.

## 필수 규칙

1. **빌드**: `.cursor/rules/build_standard.mdc` — VS 18 MSBuild, `Platform=x64`, 출력 `bin\x64\{Configuration}\`  
2. **가상화**: Phase 2 전 **Hook/Native/Setup DLL 목록 변경**은 사용자 확인 후 진행  
3. **로그** (실측 시): `eDIAN.Main\bin\x64\Debug\logs\` — `plugin.log`, `application.log`, `service_*.log`  
4. **크래시·VFS 실측·EOD**: AutoCAD 워크스페이스의 스킬·규칙 사용 (필요 시 해당 창에서 작업)

## 빌드·배포 순서 (요약)

1. `eDIAN.Main` (및 의존 프로젝트) — **Release \| x64**  
2. `eDIAN.Setup` — MSI 생성 (`Release\eDIAN.Setup.msi`)  
3. Setup 출력 경로는 **`..\eDIAN.Main\bin\x64\Release\`** (net8 하위 폴더 **아님**)

---
*최종 업데이트: 2026-05-26 — ZWCAD 워크스페이스 최소 Agent 환경*
