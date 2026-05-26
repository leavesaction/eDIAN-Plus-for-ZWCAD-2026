using System;
using System.Collections.Generic;

namespace eDIAN.Data
{
    public class DocumentConstants
    {
        // 클래식 메뉴 표시 여부
        public static int CLASSIC_MENU_VISIBLE;

        // 클래식 메뉴 잠금 여부
        public static bool UNLOCK_CLASSIC_MENU;

        // 마지막 클래식 메뉴 표시 상태
        public static int LAST_CLASSIC_MENU_VISIBLE;

        // 권한 제어 관련 버튼 ID 사전
        public static Dictionary<String, List<String>> DICTIONARY_OF_ID;

        // 권한 제어 관련 명령어 사전
        public static Dictionary<String, List<String>> DICTIONARY_OF_COMMAND;

        // 클래식 메뉴 인덱스 사전
        public static Dictionary<String, List<String>> DICTIONARY_MENU_INDEX;

        static DocumentConstants()
        {
            // 클래식 메뉴 잠금 여부
            UNLOCK_CLASSIC_MENU = true;

            // 마지막 클래식 메뉴 표시 상태
            LAST_CLASSIC_MENU_VISIBLE = -1;

            ///////////////////////////////
            // 권한 관련 메뉴 아이디 사전
            //////////////////////////////
            
            DICTIONARY_OF_ID = new Dictionary<String, List<String>>();

            DICTIONARY_OF_ID.Add("Common", new List<String>() 
            {
                "ACAD.ID_TABCOLLABORATE", 		// 공동 작업
	            "EXPRESS.RBN_0047", 			// Express Tools
	            "FEATUREDAPPS.RBN_190_37149"	// 주요 응용프로그램
            });

            DICTIONARY_OF_ID.Add("Save", new List<String>() 
            {
                "ACQATSAVE", 			// 저장
	            "ACQATSAVEAS",          // 다른 이름으로 저장
	            "ACQATSAVETOWEBMOB",    // 웹 및 모바일에 저장
	            "ACOPEN",               // 열기
	            "ACSAVE",               // 저장
	            "ACSAVEAS",             // 다른 이름으로 저장
	            "ACEXPORT",             // 내보내기 
                "QSAVE", 
                "SAVEAS", 
                "SAVE", 
                "EXPORT" 
            });

            DICTIONARY_OF_ID.Add("Copy", new List<String>() 
            {
                "ID_COPY", 						// 복사
	            "ID_RBN_SPLITBTN_PASTE_0001",   // 붙여넣기
	            "ID_PASTECLIP",                 // 클립 붙여넣기
	            "ID_PASTEBLOC",                 // 블록으로 붙여넣기    
	            "ID_PASTEHLNK",                 // 하이퍼링크로 붙여넣기
	            "ID_PASTEORIG",                 // 원래의 좌표로 붙여넣기
	            "ID_PASTESP",                   // 선택하여 붙여넣기
	            "RBN_242_DDD3E",                // 잘라내기 드롭다운
	            "ID_CUTCLIP",                   // 잘라내기
	            "ID_CUTBASECLIP",               // 기준점을 사용하여 잘라내기
	            "RBN_242_A6505",                // 복사 드롭다운
	            "ID_COPYCLIP",                  // 클립 복사
	            "ID_COPYBASE"                   // 기준점을 사용하여 복사
            });
            DICTIONARY_OF_ID.Add("Print", new List<String>() 
            {
                "ACQATPLOT",                // 플롯
                "ACQATPUBLISH", 		    // 배치 플롯
	            "ACQATLAYER", 				// 도면층
	            "ACQATMATCHPROP", 			// 특성 일치	
                "ACQATPLOTPREV", 			// 플롯 미리보기
	            "ACQATPROPERTIES",			// 특성
	            "ACQATRENDER",              // 렌더
	            "ACQATSSM",                 // 시트 세트 관리자
	            "ACQATWORKSPACE",           // 작업 공간
                "ACPRINT",                  // 인쇄
                "ACAD.ID_TABOUTPUT",
                "PLOT", 
                "PRINT" 
            });
            DICTIONARY_OF_ID.Add("Publish", new List<String>()
            {
	            "ACPUBLISH",                // 게시
	            "ID_DATALINKMGR",           // 데이터링크
	            "ID_UPDATEDATALINKSALL",    //  - 원본에서 다운로드 
	            "ID_WRITEDATALINKS",        //  - 원본에서 업로드
	            "ID_EATTEXT",               // 데이터 추출
                "PUBLISH",                  // 게시
                "SHARE" 
            });

            /////////////////////////////
            // 권한 관련 명령어 사전
            /////////////////////////////
            
            DICTIONARY_OF_COMMAND = new Dictionary<String, List<String>>();

            // 저장/내보내기 관련
            DICTIONARY_OF_COMMAND.Add("Common", new List<String>()
            {

            });

            // 저장/내보내기 관련
            DICTIONARY_OF_COMMAND.Add("Save", new List<String>()
            {
                "QSAVE",            // 빠른 저장(대화상자 없이 즉시 저장)
                "SAVE",             // 현재 도면 저장(필요 시 저장 대화상자 표시)
                "SAVEAS",           // 다른 이름/형식으로 저장(대화상자)
                "-SAVEAS",          // 다른 이름/형식으로 저장(명령행)
                "+SAVEAS",          // 다른 이름/형식으로 저장(대화상자 강제)
                "SAVEALL",          // 열린 모든 도면 저장
                "SAVETOWEBMOBILE",  // 웹 & 모바일에 저장
                "BSAVE",            // 블록 편집기에서 블록 변경 내용 저장
                "WBLOCK",           // 선택 객체/블록을 새 DWG로 저장
                "-WBLOCK",          // WBLOCK의 명령행 버전
                "BLOCKREPLACE",     // REPLACE BLOCK
                "EXPORT",           // 다양한 포맷으로 내보내기
                "EXPORTPDF",        // PDF로 내보내기
                "EXPORTDWF",        // DWF로 내보내기
                "EXPORTDWFX",       // DWFx로 내보내기
                "DGNEXPORT",        // MicroStation DGN으로 내보내기
                "3DDWF",            // 3D DWF로 내보내기
                "DWGCONVERT"        // DWG 버전 일괄 변환/저장
            });

            // 출력 관련 명령
            DICTIONARY_OF_COMMAND.Add("Print", new List<String>()
            {
                "PREVIEW",          // 인쇄 미리보기
                "VIEWPLOTDETAILS",  // 플롯 세부 정보 보기
                "PAGESETUP",        // 페이지 설정
                "3DPRINTSERVICE",   // 3D 프린트 서비스로 업로드
                "3DPRINT",          // 3D 프린터로 출력
                "BATCHPLOT",        // 배치 플롯(버전에 따라 제공)
                "PLOTTERMANAGER",   // 플로터 관리자 폴더 열기
                "STYLESMANAGER",    // 플롯 스타일 관리자(CTB/STB) 열기
                "PLOTSTYLE",        // 플롯 스타일 테이블 설정
                "ETRANSMIT",        // 전송 패키지 만들기(도면/참조/폰트 포함)
                "-PLOT",            // 플롯(인쇄) 명령행 버전
                "PLOT",             // 플롯(인쇄) 대화상자 열기
                "PRINT"            // 인쇄(일부 환경에서 PLOT 별칭)
            });

            // 복사 관련 명령
            DICTIONARY_OF_COMMAND.Add("Copy", new List<String>()
            {
                "COPY",             // 객체 복사
                "COPYBASE",         // 기준점 지정 후 복사
                "NCOPY",            // 중첩 객체 복사
                "COPYCLIP",         // 클립보드로 복사
                "COPYCLIPSETTINGS", // 클립보드 복사 설정
                "CUTCLIP",          // 클립보드로 잘라내기
                "PASTECLIP",        // 붙여넣기
                "PASTEBLOCK",       // 블록으로 붙여넣기
                "PASTEORIG",        // 원점(0,0)에 붙여넣기
                ".MOCORO",          // Move/Copy/Rotate
                "PASTESPEC",        // 선택하여 붙여넣기(대화상자)
                "-PASTESPEC",        // 선택하여 붙여넣기(명령행)
                "CLIP"
            });

            // 게시/공유 관련 명령
            DICTIONARY_OF_COMMAND.Add("Publish", new List<String>()
            {
                "PUBLISH",          // 게시(시트 세트/다중 도면 게시)
                "-PUBLISH",         // 게시(명령행 버전)
                "ETRANSMIT",        // 전송 패키지 생성 및 공유
                "ARCHIVE",          // 아카이브 패키지 생성
                "SHAREVIEW",        // 공유 보기 생성(Autodesk Viewer)
                "SHARE",            // 공유(프로젝트/링크 등)
                "PUSHTODOCSOPEN",   // Push to Autodesk DOCS
                "SHOWURLS",         // Show URLs
                "CHURLS",           // Change URLs
                "REPURLS",          // Find and Replace URLs
                "SEND",             // 전송
                "MAIL"              // SEND 명령의 별칭
            });

            // 기타
            DICTIONARY_OF_COMMAND.Add("Etc", new List<String>()
            {
                "OPEN",             // 도면 열기
                "DOCINIT",          // 새 도면 시작(템플릿 선택)
                "QUIT",             // 프로그램 종료
                "CLOSE"             // 현재 도면 닫기
            });

            ////////////////////////////
            // 클래식 메뉴 인덱스 사전
            ////////////////////////////  

            DICTIONARY_MENU_INDEX = new Dictionary<String, List<String>>();

            DICTIONARY_MENU_INDEX.Add( "PUBLISH", new List<String>()
            {
                "24:18",             // 파일 > 전자 전송(T)
                "24:19",             // 파일 > 전송(D)
            });
        }
    }
}
