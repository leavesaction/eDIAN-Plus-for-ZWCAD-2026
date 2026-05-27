using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Runtime;
using eDIAN.Core;
using eDIAN.Data;
using eDIAN.Hook;
using eDIAN.Main.API;
using eDIAN.Main.Core;
using eDIAN.Main.Data;
using eDIAN.Main.UI;
using log4net;
using Microsoft.Identity.Client;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using CadApplication = ZwSoft.ZwCAD.ApplicationServices.Application;
using System.Collections.Generic;
using System;

namespace eDIAN.Main
{
    public class PluginApplication : IExtensionApplication
    {
        private readonly ILog logger = PluginLogger.getLogger("PlugInApplication", "plugin.log");

        // 플러그인 데이터
        public static DocumentHandler documentHandler;

        // 폼 관리자
        public static PluginFormManager pluginFormManager;

        // 다국어 지원 타이틀, 메세지 셋
        public static Global global;

        // 로그인 사용자 라이선스 정보  
        public static UserLicenseData userLicenseData;

        // 서비스 서버 연결 상태 (기본값 : DISCONNECT)
        public static CommonConstants.ConnectStatus connectStatus;

        // MIP 인증 상태
        public static CommonConstants.AuthStatus authStatus;

        // MIP 인증 결과 정보 (로그인 성공 시 정보 포함, 로그아웃 시 null)
        public static AuthenticationResult authenticationResult;

        // 사용자 레이블 부여시 대상 사용자 목록 (레이블을 부여할 때 선택할 수 있도록 제공)
        public static List<UserLicenseData> userLicenseDataList;

        // 기본 도면을 Idle 시점에 한 번만 닫기 위한 플래그
        private static bool _defaultDocClosed;


        static PluginApplication()
        {
            documentHandler = new DocumentHandler();

            pluginFormManager = new PluginFormManager();

            global = new Global();

            userLicenseData = new UserLicenseData();

            connectStatus = CommonConstants.ConnectStatus.DISCONNECT;

            authStatus = CommonConstants.AuthStatus.NONE;

            userLicenseDataList = new List<UserLicenseData>();

            _defaultDocClosed = false;

        }

        public void Initialize()
        {
            logger.Info("PlugInApplication Initialize called.");

            try
            {
                // 1. CAD 초기 설정
                this.initializeCAD();

                // 2. 플러그인 환경 초기화
                PluginInitializer pluginInitializer = new PluginInitializer();

                if (!pluginInitializer.InitializeDirectory())
                {
                    logger.Error("PluginInitializer InitializeDirectory failed.");

                    return;
                }

                // 3. 통합 플러그인 폼 표시
                pluginFormManager.loadMainForm();

                // 4. [VFS Renaissance v3.0] 하이브리드 네이티브 훅 엔진 가동 (Phase 6b: 6a 닫기 안정화 후 재활성)
                VfsInterceptor.Install();
            }
            catch (System.Exception ex)
            {
                logger.Error($"PluginApplication Exception : {ex}");
            }

            logger.Info("PlugInApplication Initialize completed.");
        }

        /// <summary>
        /// CAD 초기 설정
        /// </summary>
        private void initializeCAD()
        {
            try
            {
                // CAD 메인 윈도우 핸들 저장
                CommonConstants.CAD_MAIN_WINDOW_HANDLE = CadApplication.MainWindow.Handle;

                // 지원 로케일 설정 (기본값은 영어로 설정)
                if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ko", StringComparison.OrdinalIgnoreCase))
                {
                    CommonConstants.GLOBAL_KEY = "KOR";
                }

                // 어플리케이션 시작 탭 활성화 : (STARTMODE - 0:비활성화, 1:비활성화)
                CadApplication.SetSystemVariable("STARTMODE", 0);

                // 어플리케이션 시작 옵션 : (STARTUP - 0 : 빈 도면 열기, 1 : 시작 대화 상자, 2 : 시작 탭이 표시, 3 : 새 도면을 열거나 작성하면 시작 탭이 표시되고 리본이 미리 로드)
                CadApplication.SetSystemVariable("STARTUP", 0);

                // 어플리케이션 파일 탭 숨김
                CadApplication.DocumentManager.MdiActiveDocument.SendStringToExecute("FILETABCLOSE ", true, false, false);

                // CAD 실행 시 열리는 기본 도면 닫기 
                CadApplication.Idle += this.closeDefaultDocument;
            }
            catch (System.Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// AutoCAD가 Idle 상태가 되었을 때 한 번만 호출해서 기본 도면을 닫는다.
        /// </summary>
        private void closeDefaultDocument(object sender, System.EventArgs e)
        {
            // 여러 번 들어올 수 있으므로, 한 번 실행 후 바로 해제

            if (_defaultDocClosed)
            {
                CadApplication.Idle -= this.closeDefaultDocument;
                return;
            }

            try
            {
                DocumentCollection docCollection = CadApplication.DocumentManager;

                // 열려 있는 도면이 없으면 바로 종료

                if (docCollection.Count == 0)
                {
                    logger.Warn("closeDefaultDocument: documents is empty.");
                    return;
                }

                // 현재 활성 도면(기본 도면) 가져오기
                Document defaultDoc = docCollection.MdiActiveDocument ?? docCollection.CurrentDocument;

                if (defaultDoc == null || defaultDoc.IsDisposed || defaultDoc.UnmanagedObject == IntPtr.Zero)
                {
                    logger.Warn("closeDefaultDocument: default document is null or already disposed.");
                    return;
                }

                logger.Info($"closeDefaultDocument: Closing default document '{defaultDoc.Name}'.");


                // 기본 문서 닫기
                defaultDoc.CloseAndDiscard();

                // 리본 메뉴 추가
                // ApplicationController.ensureRibbon();

                _defaultDocClosed = true;
            }
            catch (System.Runtime.InteropServices.COMException comEx)
            {
                logger.Error("closeDefaultDocument COMException : 도면이 사용 중이어서 닫을 수 없습니다. AutoCAD가 안정된 후 다시 시도해야 할 수 있습니다.", comEx);
            }
            catch (System.Exception ex)
            {
                logger.Error($"OnApplicationIdle Exception : {ex}");
            }
            finally
            {
                // 어떤 경우든 Idle 핸들러는 제거 (무한 호출 방지)
                CadApplication.Idle -= this.closeDefaultDocument;
            }
        }

        public void Terminate()
        {
            // MIP 임시 파일 삭제
            FileManager.deleteMipTempFiles();

            // VFS 훅 엔진 해제
            VfsInterceptor.Uninstall();

            logger.Info("PlugInApplication Terminate completed.");
        }

        public void Dispose()
        {
            logger.Info("PlugInApplication Dispose called.");
        }
    }
}