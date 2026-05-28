# ZWCAD 임시파일/저장 특성 — 외부 자료 조사 노트 (VFS 관점)

본 문서는 ZWCAD 임시파일/백업 파일 규칙에 대한 **외부 자료 조사 요약**이며, Phase 2 VFS(PhantomVfs) 적용 시 **오해 방지** 및 **실측 항목 보강**을 목적으로 한다.  
단일 판정 기준은 `ZWCAD_VFS_ENGINEERING_GUIDE.md`를 따른다.

## 1. 핵심 결론(요약)

- **ZWCAD는 “zws” 문자열을 자동저장 파일명에도 사용**한다.  
  - 외부 문서 기준 자동저장 파일 예: `filename_zws********.zs$`  
  - 따라서, 내부 구현/로그에서 보이는 `zws*.tmp`를 **자동저장(.zs$/.zw$)과 동일한 개념으로 단정하면 위험**하다.
- **자동저장/백업 확장자는 복구 관점(.zs$/.zw$/.sv$/.bak)** 으로 설명되어 있으며, 이는 VFS의 “저장 완료/원자 교체”와는 별개 축이다.
- Plot/Publish의 “manifest 폴더 규칙/부속 exe 명”은 **공식 문서만으로는 부족**하며, VFS 적용에는 **Procmon/Native 로그 실측이 더 중요**하다.

## 2. ZWCAD 백업/자동저장 파일 규칙(외부 자료 기반)

### 2.1 자동저장 파일(Autosave)

- 확장자: **`.zs$`, `.zw$`**, (FAQ에 따라 `.sv$`도 자동저장으로 사용 가능)  
- 파일명 예: **`filename_zws********.zs$`**  
- 위치: **Options에서 설정되는 Automatic Save File Location** 또는 시스템 temp(`%temp%`)  
- 특징: **정상 종료 시 자동 삭제될 수 있으나**, 크래시/비정상 종료 시 남을 수 있음  
- 복구: 파일을 안전한 위치로 복사 후 확장자를 `.dwg`로 바꿔 열기

참고:
- `How to Organize, Save, and Share CAD Files in ZWCAD` (`https://www.zwsoft.com/support/zwcad-instruction-command/drawing-management`)
- `How to Find and Open ZWCAD Backup Files?` (`https://www.zwsoft.com/support/zwcad-base-faq/574`)
- `File extension SV$, what is SV$? How to open SV$ file?` (`https://www.zwsoft.com/support/zwcad-base-faq/607`)
- Confluence: `How to restore the automatically saved drawing files in ZWCAD` (`https://confluence.zwcad.com/pages/viewpage.action?pageId=181092515`)

### 2.2 수동 저장 백업(Backup)

- 확장자: **`.bak`**
- 생성: “저장할 때마다(Create a backup copy with each save)” 옵션을 통해 동일 폴더에 생성(이전 상태 스냅샷)
- 복구: `.bak` → `.dwg`로 변경 후 열기

## 3. VFS(PhantomVfs) 적용 시 의미(실무 포인트)

### 3.1 “zws” 문자열 혼동 방지

외부 문서에서 `filename_zws********.zs$`처럼 “zws”가 자동저장 파일명에 포함되므로,

- **VFS에서 `zws` 프리픽스를 ‘QSAVE sidecar’로만 해석하지 말 것**
- 실제 저장 시퀀스는 **Native 로그(`[SAVE-IO]`) + Procmon(선택)** 으로 확정해야 한다.

### 3.2 타겟/비타겟(확장자) 정책 검증 필요

자동저장 파일(`.zs$/.zw$/.sv$`)은 “복구용 DWG 데이터”를 담을 수 있으므로,

- **저장소 정책상 가상화 대상인지(보호 경계)**
- **실제 생성 위치가 `mip\temp`인지, 별도 autosave 경로인지**

를 먼저 실측으로 확인해야 한다.

> 현재 Phase 2의 핵심 타겟은 `mip\temp\_uuid.dwg` 및 ZWCAD 저장 시 sidecar/교체 시퀀스이며, autosave까지 즉시 범위에 넣는 것은 리스크가 크다. (필요 시 백로그로 분리 권장)

### 3.3 Plot/Publish는 외부 문서만으로 불충분

외부 자료는 Smart Batch Plot(명령/기능) 중심이며,

- 실제 부속 exe 명,
- temp/manifest 경로 패턴,
- 환경변수(세션) 상속,

은 VFS 적용을 위해 **실측 기반으로만 확정**해야 한다.

## 4. 실측 체크리스트(외부 조사 반영)

Phase 6 실측에서 아래를 추가로 확인하면 “ZWCAD 특성”을 빠르게 수렴할 수 있다.

- **Autosave 경로 확인**: ZWCAD에서 `SAVEFILEPATH` 값 확인 (Options의 Automatic Save File Location과 일치하는지)
- **Autosave 파일명 패턴 확인**: `*_zws*.zs$` 생성 여부 및 생성 위치
- **QSAVE 시퀀스와의 구분**: 동일 세션에서 “자동저장(.zs$)”과 “저장 sidecar(예: zws*.tmp)”가 동시에 보이는지, 호출 순서 차이가 있는지

## 5. 비고(한계)

- 본 문서는 “외부 문서로 확인 가능한 범위”만 요약했다.  
  VFS 튜닝에 필요한 **실제 Win32 API 호출 순서/파일 교체 방식**은 반드시 실측으로 확정해야 한다.

