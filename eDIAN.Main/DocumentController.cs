using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.PlottingServices;
using eDIAN.Core;
using eDIAN.Data;
using eDIAN.Main.API;
using eDIAN.Main.Core;
using eDIAN.Main.Data;
using log4net;
using System.Collections.Concurrent;
using System.IO;
using CadApplication = ZwSoft.ZwCAD.ApplicationServices.Application;
using CadCoreApplication = ZwSoft.ZwCAD.ApplicationServices.Core.Application;
using System;
using System.Collections.Generic;

namespace eDIAN.Main
{
    public class DocumentController
    {
        private static readonly ILog logger = PluginLogger.getLogger("DocumentController", "application.log");

        private bool _isUpdatingMenuBar = false;    // 이벤트 내부에서 MENUBAR를 강제 변경 중인지 표시하는 플래그

        // [Audit Log] 플롯/출력 이벤트 감시를 위한 리액터 매니저
        private PlotReactorManager _plotReactorManager = null;

        public event EventHandler<MessageEventArgs> OnReceiveErrorMessage;             // 알림 메세시 수신될 때 메세지 팝업 이벤트 (showNotificationWindow 호출)
        public event EventHandler<ProtectedDocumentEventArgs> OnDestroyDocument;       // 보호된 도면이 닫힐 때 백업 파일에 보호 적용 이벤트 (backupProtectionToTempFile 호출)

        // 문서 메뉴 관리자
        private ApplicationController applicationController;

        // Destroy 시 파일 명이 중복으로 존재할 경우 Destroy 전에 지정된 ProtectedDocument 객체를 받아 destory 하기 위한 대기열 저장
        private readonly ConcurrentDictionary<string, Queue<ProtectedDocument>> _pendingDestroyQueue = new(StringComparer.OrdinalIgnoreCase);

        public DocumentController()
        {
            this.applicationController = ApplicationController.getInstance();

            // [Audit Log] PlotReactorManager 감사 로깅 이벤트 등록
            try
            {
                _plotReactorManager = new PlotReactorManager();
                _plotReactorManager.BeginDocument += OnBeginDocument;
                _plotReactorManager.BeginPage += OnBeginPage;
                logger.Info("Successfully registered PlotReactorManager for audit logging (BeginDocument, BeginPage).");
            }
            catch (Exception ex)
            {
                logger.Error("Failed to register PlotReactorManager", ex);
            }
        }

        /// <summary>
        /// 시스템 변수 변경 감지 이벤트 핸들러  
        /// CAD의 이벤트가 발생시 해당 이벤트 명에 해당하는 값을 패치하거나 한다. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void systemVariableChanged(object sender, ZwSoft.ZwCAD.ApplicationServices.SystemVariableChangedEventArgs e)
        {
            logger.Debug("---------------------------------------------------");
            logger.Debug(" > CAD Application Event : SystemVariableChanged");
            logger.Debug("---------------------------------------------------");
            logger.Debug($" - SystemVariable : {e.Name}");

            // 변경된 시스템 변수값

            object changedSystemVariable = CadApplication.GetSystemVariable(e.Name);

            if (changedSystemVariable == null)
            {
                logger.Debug($" - {e.Name}'s SystemVariable changed to null");
                logger.Debug("---------------------------------------------------- SystemVariableChanged\n");

                return;
            }

            if (String.Equals(e.Name, "ShowFullPathInTitle"))
            {
                CadApplication.SetSystemVariable("ShowFullPathInTitle", false);
                logger.Debug($" - {e.Name}'s variable setted false");
            }

            // UNLOCK 상태에서 클래식 메뉴바 변경시 원복 (화면에서 제어 불가)

            if (String.Equals(e.Name, "MENUBAR"))
            {
                if (_isUpdatingMenuBar == false && DocumentConstants.UNLOCK_CLASSIC_MENU == false)
                {
                    // 변경 방지 상태인 경우 

                    int currentValue = Convert.ToInt32(changedSystemVariable);

                    if (currentValue != DocumentConstants.LAST_CLASSIC_MENU_VISIBLE)
                    {
                        // 변경된 값이 마지막으로 저장된 값과 다른 경우

                        try
                        {
                            _isUpdatingMenuBar = true; // 재귀 방지 (업데이트 상태(true) 로 변경)

                            // 마지막으로 저장된 값으로 복원 (이때 이벤트가 한번 더 실행되는데 _isUpdatingMenuBar 가 true 이므로 실행 안됨.)
                            CadCoreApplication.SetSystemVariable(e.Name, DocumentConstants.LAST_CLASSIC_MENU_VISIBLE);

                            logger.Debug($" - MENUBAR is {currentValue}, reset to {DocumentConstants.LAST_CLASSIC_MENU_VISIBLE}");
                        }
                        finally
                        {
                            _isUpdatingMenuBar = false; // 업데이트 전(false)로 변경 (이벤트가 한번 더 실행 되고 나서 원래 변경 상태로 변경해야 새로운 이벤트가 실행된다)
                        }
                    }
                }
                else
                {
                    if (DocumentConstants.UNLOCK_CLASSIC_MENU == true)
                    {
                        logger.Debug(" - Locked MENUBAR");
                    }
                    else if (_isUpdatingMenuBar == true)
                    {
                        logger.Debug(" - Skip changing MENUBAR.");
                    }
                }
            }

            logger.Debug($" - {e.Name} = {changedSystemVariable.ToString()}");
            logger.Debug("---------------------------------------------------- SystemVariableChanged\n");
        }

        /// <summary>
        /// [DocumentManager 이벤트 핸들러] 문서 생성
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e">CAD에서 전달 받은 생성된 문서에 대한 이벤트 인수</param>
        public void documentCreated(object sender, DocumentCollectionEventArgs e)
        {
            logger.Debug("----------------------------------------------------");
            logger.Debug(" > CAD Document Event : documentCreated");
            logger.Debug("----------------------------------------------------");
            logger.Debug($" - Created Document : '...\\{Path.GetFileName(e.Document.Name)}'");

            //            this.attachDatabaseEvents(e.Document.Database);

            // 인수로 전달 받은 객체의 이벤트 추가

            e.Document.CommandWillStart += commandWillStart;
            e.Document.CommandEnded += commandEnded;
            e.Document.CommandCancelled += commandCancelled;
            e.Document.CommandFailed += commandFailed;
            e.Document.Database.BeginSave += beginSave;
            e.Document.Database.SaveComplete += saveComplete;

            ProtectedDocument protectedDocument = PluginApplication.documentHandler.getOpenDocument(e.Document);

            if (protectedDocument == null)
            {
                protectedDocument = new ProtectedDocument(e.Document);
            }

            // 보호된 문서가 아니고 임시 파일도 아닌 경우 
            if (protectedDocument.isProtected == false && FileManager.isTemporaryFile(protectedDocument.filePath) == false)
            {
                logger.Debug($"documentCreated : {protectedDocument.ToString()}");

                // 열린 문서 목록에 추가 
                PluginApplication.documentHandler.addOpenDocument(protectedDocument);
            }

            logger.Debug("documentCreated ----------------------------------------------------\n");
        }

        /// <summary>
        /// [DocumentManager 이벤트 핸들러]열린 문서를 닫을 때 발생하는 이벤트 핸들러
        /// </summary>
        /// <param name="sender"></param>   
        /// <param name="e"></param>
        public void documentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            logger.Info("----------------------------------------------------");
            logger.Info(" > CAD Document Event : documentToBeDestroyed");
            logger.Info("----------------------------------------------------");

            try
            {
                String docName = e.Document?.Name ?? string.Empty;

                logger.Debug($" - Document Name        : '...\\{Path.GetFileName(docName)}'");

                if (e.Document != null)
                {
                    ProtectedDocument protectedDocument = PluginApplication.documentHandler.getOpenDocument(e.Document);

                    CloseFlowDiagnostics.LogPhaseByPath(docName, CloseFlowDiagnostics.ClosePhase.DocumentToBeDestroyed,
                        protectedDocument != null
                            ? $"needLabeling={protectedDocument.needLabeling} edit={protectedDocument.isEdit}"
                            : "protectedDocument=null");

                    // 존재하는 경우 대기열에 저장
                    if (protectedDocument != null)
                    {
                        Queue<ProtectedDocument> queue = _pendingDestroyQueue.GetOrAdd(docName, _ => new Queue<ProtectedDocument>());
                        queue.Enqueue(protectedDocument);
                    }

                    // 이벤트 핸들러 제거
                    e.Document.CommandWillStart -= commandWillStart;
                    e.Document.CommandEnded -= commandEnded;
                    e.Document.CommandCancelled -= commandCancelled;
                    e.Document.CommandFailed -= commandFailed;
                    e.Document.Database.BeginSave -= beginSave;
                    e.Document.Database.SaveComplete -= saveComplete;
                }
            }
            catch (Exception ex)
            {
                logger.Error("Exception in documentToBeDestroyed: ", ex);
            }

            logger.Debug("documentToBeDestroyed ----------------------------------------------------\n");
        }

        /// <summary>
        /// [DocumentManager 이벤트 핸들러]열린 문서를 닫을 때 발생하는 이벤트 핸들러
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void documentDestroyed(object sender, DocumentDestroyedEventArgs e)
        {
            try
            {
                logger.Info("----------------------------------------------------");
                logger.Info(" > CAD Document Event : documentDestroyed");
                logger.Info("----------------------------------------------------");
                
                string fileName = e.FileName ?? string.Empty;
                logger.Debug($" - Document : '...\\{Path.GetFileName(fileName)}', HashCode : {e.GetHashCode()}");

                // 파일명으로 찾지 않고, 큐에서 방금 닫힌 문서를 넘겨받음
                if (_pendingDestroyQueue.TryGetValue(fileName, out var queue) && queue.Count > 0)
                {
                    ProtectedDocument protectedDocument = queue.Dequeue();

                    if (protectedDocument == null)
                    {
                        logger.Debug($"protectedDocument is null.");

                        return;
                    }

                    if (protectedDocument.isProtected)
                    {
                        // 보호 적용된 파일일 경우

                        CloseFlowDiagnostics.LogPhaseByPath(fileName, CloseFlowDiagnostics.ClosePhase.DocumentDestroyed,
                            $"needLabeling={protectedDocument.needLabeling} edit={protectedDocument.isEdit}");

                        if (protectedDocument.needLabeling)
                        {
                            // 임시파일에 보호 적용을 한다.(파일명이 중복 되는 경우 없음)
                            // 보호된 도면이 닫힐 때 event 호출 (applyProtectionToTempFile). 

                            CloseFlowDiagnostics.LogPhaseByPath(fileName, CloseFlowDiagnostics.ClosePhase.DestroyBranchApplyProtection, null);
                            OnDestroyDocument?.Invoke(this, new ProtectedDocumentEventArgs(protectedDocument));
                        }
                        else 
                        {
                            if (!String.IsNullOrWhiteSpace(protectedDocument.decryptedTemporaryFilePath))
                            {
                                CloseFlowDiagnostics.LogPhaseByPath(fileName, CloseFlowDiagnostics.ClosePhase.DestroyBranchDeleteFiles,
                                    $"temp='{Path.GetFileName(protectedDocument.decryptedTemporaryFilePath)}'");
                                // 기존 임시 파일 삭제
                                FileManager.deleteFilesByName(protectedDocument.decryptedTemporaryFilePath);
                            }
                        }
                    }

                    CloseFlowDiagnostics.LogPhaseByPath(fileName, CloseFlowDiagnostics.ClosePhase.RemoveOpenDocument, null);
                    // 열린 문서 목록에서 제거
                    PluginApplication.documentHandler.removeOpenDocument(protectedDocument);
                    CloseFlowDiagnostics.CompleteByPath(fileName);
                }
                else
                {
                    logger.Debug($" - Dequeue failed! No matching key in _pendingDestroyQueue for '{fileName}'");
                }
            }
            catch (Exception ex)
            {
                logger.Error("Exception in documentDestroyed: ", ex);
            }

            logger.Debug("documentDestroyed ---------------------------------------------------- \n");
        }

        /// <summary>
        /// CAD 도면 파일이 활성화될 때 이벤트 처리 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e">활성화 된 도면정보</param>
        public void documentActivated(object sender, DocumentCollectionEventArgs e)
        {
            logger.Debug("----------------------------------------------------");
            logger.Debug(" > CAD Document Event : documentActivated");
            logger.Debug("----------------------------------------------------");

            String result = "";

            Document activeDocument = e.Document;

            if (activeDocument != null)
            {
                logger.Debug($" - Actived Document : '...\\{Path.GetFileName(activeDocument.Name)}'");

                // 파일 보안 문서 정보(보호 처리된 문서) 목록에서 전달 받은 파일명으로 해당 파일 보안 정보 검색

                ProtectedDocument protectedDocument = PluginApplication.documentHandler.getOpenDocument(e.Document);

                if (protectedDocument == null)
                {
                    protectedDocument = new ProtectedDocument(e.Document);
                }

                if (protectedDocument.isProtected)
                {
                    // # 보안이 적용된 문서
                    logger.Debug(" - Protected document.");

                    if (!protectedDocument.isPrint && !protectedDocument.isOwner)
                    {
                        // 화면 캡처 방지 적용 (출력 권한이 없고 소유자도 아닌 경우)
                        result = DisplayProtector.protectScreen(CommonConstants.CAD_MAIN_WINDOW_HANDLE, true);
                    }
                    else
                    {
                        // 화면 캡처 방지 해제 (출력 권한이 있거나 소유자인 경우)
                        result = DisplayProtector.protectScreen(CommonConstants.CAD_MAIN_WINDOW_HANDLE, false);
                    }

                    // 보호된 도면이 활성화 될 때 권한에 따라 활성화 비활성화 처리
                    this.applicationController.setApplicationMenuByLabels(protectedDocument);
                }
                else
                {
                    // # 보안 적용 되지 않은 문서

                    logger.Debug(" - Not protected document.");

                    // 화면 캡처 방지 해제 (보호 적용된 도면이 아닌 경우)
                    result = DisplayProtector.protectScreen(CommonConstants.CAD_MAIN_WINDOW_HANDLE, false);

                    // 보호된 도면이 아닌 경우 메뉴 활성화
                    this.applicationController.setApplicationMenuByLabels(null);
                }

                // 활성화 된 열린 파일 Row 반전 처리
                PluginApplication.documentHandler.activeOpenDocument(protectedDocument);
            }
            else
            {
                logger.Debug($" - Document is not exist.");

                // 화면 캡처 방지 해제 (도면이 없는 경우)

                result = DisplayProtector.protectScreen(CommonConstants.CAD_MAIN_WINDOW_HANDLE, false);

                // 도면이 없는 경우 메뉴 활성화
                this.applicationController.setApplicationMenuByLabels(null);
            }

            logger.Debug("documentActivated ----------------------------------------------------\n");
        }

        /// <summary>
        /// [DocumentManager 이벤트 핸들러] CAD 어플리케이션에서의 저장(공유), 복사, 출력 명령에 대한 이벤트 제어
        /// (권한이 없는 경우 처리를 중단하고 경고 메세지를 출력한다)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void documentLockModeChanged(object sender, DocumentLockModeChangedEventArgs e)
        {
            String command = e.GlobalCommandName;

            if (e.Document == null || String.IsNullOrEmpty(command) || e.Document.Window == null)
            {
                return;
            }

            ProtectedDocument protectedDocument = PluginApplication.documentHandler.getOpenDocument(e.Document);

            if (protectedDocument == null)
            {
                protectedDocument = new ProtectedDocument(e.Document);
            }

            this.printLog(command, "----------------------------------------------------");
            this.printLog(command, " > CAD Document Event : documentLockModeChanged");
            this.printLog(command, "----------------------------------------------------");
            this.printLog(command, $" - Document Path  : '...\\{Path.GetFileName(e.Document.Name)}'");
            this.printLog(command, $" - Global Command : '{command}'");

            String errorMessage = "";

            if (e.GlobalCommandName.Equals("QUIT"))
            {
                // 저장되지 않은 문서가 있을 경우 종료 불가 처리
                if (CadApplication.DocumentManager != null)
                {
                    foreach (Document document in CadApplication.DocumentManager)
                    {
                        bool isUpdated = Convert.ToInt16(CadCoreApplication.GetSystemVariable("DBMOD")) != 0 ? true : false;

                        if (isUpdated)
                        {
                            e.Veto();

                            errorMessage = PluginApplication.global.getMessage("error.unsaved.documents"); // 변경된 문서가 있습니다. 저장 한 후 종료해 주시기 바랍니다.

                            this.printLog(command, $" - {errorMessage}");

                            this.OnReceiveErrorMessage?.Invoke(this, new MessageEventArgs(errorMessage, "Warning"));       // 경고 메세지 폼 호출

                            break;
                        }
                    }
                }
            }

            if (e.GlobalCommandName.Equals("CLOSE"))
            {
                // 저장되지 않은 문서는 닫지 못하게 처리
                if (protectedDocument.isProtected && protectedDocument.isUpdated == true)
                {
                    e.Veto();

                    errorMessage = PluginApplication.global.getMessage("error.modified.documents"); // 문서 내용이 변경되었습니다. 먼저 저장해 주시기 바랍니다.

                    this.printLog(command, $" - {errorMessage}");

                    this.OnReceiveErrorMessage?.Invoke(this, new MessageEventArgs(errorMessage, "Warning"));       // 경고 메세지 폼 호출
                }
            }

            // 다른 이름으로 저장 명령 처리 불가
            if (command.Equals("SAVEAS") && protectedDocument.isProtected)
            {
                e.Veto();
                
                if(IApplicationService.instance != null)
                {
                    // 서비스 서버에 저장 없음 오류 전송 
                    IApplicationService.FireAndForgetSaveUserActionLog(protectedDocument.contentId, protectedDocument.filePath, "Edit(Save) : " + command, "-402");
                }
 
                errorMessage = PluginApplication.global.getMessage("error.saveas.protected.document"); // 암호화된 문서는 다른 이름으로 저장 할 수 없습니다.

                this.printLog(command, $" - {errorMessage}");

                this.OnReceiveErrorMessage?.Invoke(this, new MessageEventArgs(errorMessage, "Warning"));       // 경고 메세지 폼 호출
            }

            // 자동 저장 명령 처리 불가
            if (command.Contains("AUTO_SAVE") && protectedDocument.isProtected)
            {
                e.Veto();

                errorMessage = $"암호화 문서는 자동 저장이 되지 않습니다.";

                this.printLog(command, $" - {errorMessage}");
            }

            // 저장 관련 명령어
            if (this.isContainCommand(command, "Save") && protectedDocument.isProtected)
            {
                if (!protectedDocument.isEdit)
                {
                    e.Veto();

                    if (IApplicationService.instance != null)
                    {
                        // 서비스 서버에 저장 없음 오류 전송
                        IApplicationService.FireAndForgetSaveUserActionLog(protectedDocument.contentId, protectedDocument.filePath, "Edit(Save) : " + command, "-402");
                    }

                    errorMessage = PluginApplication.global.getMessage("error.deny.save.document"); // 해당 도면을 저장할 수 있는 권한이 없습니다.

                    this.OnReceiveErrorMessage?.Invoke(this, new MessageEventArgs(errorMessage, "Warning"));
                }
                else if (protectedDocument.isEdit)
                {
                    // 저장 명령어가 실행되면 needLabeling true로 설정 (문서가 닫힐때 레이블이 적용됨.)

                    protectedDocument.needLabeling = true;
                }
            }

            // 공유 관련 명령어  

            if (this.isContainCommand(command, "Publish") && protectedDocument.isProtected && !protectedDocument.isEdit)
            {
                e.Veto();

                if (IApplicationService.instance != null)
                {
                    // 서비스 서버에 공유/배포 권한 없음 오류 전송
                    IApplicationService.FireAndForgetSaveUserActionLog(protectedDocument.contentId, protectedDocument.filePath, "Edit(Publish) : " + command, "-402");
                }

                errorMessage = PluginApplication.global.getMessage("error.deny.publish.document");  // 해당 도면을 공유할 수 있는 권한이 없습니다.

                this.printLog(command, $" - {errorMessage}");

                this.OnReceiveErrorMessage?.Invoke(this, new MessageEventArgs(errorMessage, "Warning"));       // 이벤트 발생. 경고 메세지 폼 호출
            }

            // 복사 관련 명령어
            if (this.isContainCommand(command, "Copy") && protectedDocument.isProtected && !protectedDocument.isExtract)
            {
                e.Veto();

                if (IApplicationService.instance != null)
                {
                    // 서비스 서버에 권한 없음 오류 전송
                    IApplicationService.FireAndForgetSaveUserActionLog(protectedDocument.contentId, protectedDocument.filePath, "Extract : " + command, "-402");
                }

                errorMessage = PluginApplication.global.getMessage("error.deny.copy.document"); // 해당 도면의 요소를 복사할 수 있는 권한이 없습니다.

                this.printLog(command, $" - {errorMessage}");

                this.OnReceiveErrorMessage?.Invoke(this, new MessageEventArgs(errorMessage, "Warning"));       // 이벤트 발생. 경고 메세지 폼 호출
            }

            // 출력 관련 명령어

            if (this.isContainCommand(command, "Print") && protectedDocument.isProtected && !protectedDocument.isPrint)
            {
                e.Veto();

                if (IApplicationService.instance != null)
                {
                    // 서비스 서버에 권한 없음 오류 전송
                    IApplicationService.FireAndForgetSaveUserActionLog(protectedDocument.contentId, protectedDocument.filePath, "Print : " + command, "-402");
                }

                errorMessage = PluginApplication.global.getMessage("error.deny.print.document");  // 해당 도면을 출력할 수 있는 권한이 없습니다.

                this.printLog(command, $" - {errorMessage}");

                this.OnReceiveErrorMessage?.Invoke(this, new MessageEventArgs(errorMessage, "Warning"));       // 이벤트 발생. 경고 메세지 폼 호출
            }

            this.printLog(command, $"--------------------------------------------------- documentLockModeChanged : {command} \n");
        }

        /// <summary>
        /// CAD 어플리케이션 종료 시작 이벤트 핸들러
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void quitWillStart(Object sender, EventArgs e)
        {
            logger.Debug("---------------------------------------------------");
            logger.Debug(" >> CAD Application Event : quitWillStart");
            logger.Debug("---------------------------------------------------");

            // [Audit Log] PlotReactorManager 이벤트 해제
            if (_plotReactorManager != null)
            {
                try
                {
                    _plotReactorManager.BeginDocument -= OnBeginDocument;
                    _plotReactorManager.BeginPage -= OnBeginPage;
                    _plotReactorManager = null;
                    logger.Info("Successfully unsubscribed PlotReactorManager.");
                }
                catch (Exception ex)
                {
                    logger.Error("Failed to dispose PlotReactorManager", ex);
                }
            }

            // 모든 메뉴 상태 복구
            this.applicationController.setApplicationMenuByLabels(null);

            logger.Debug("quitWillStart ---------------------------------------------------\n");
        }

        /// <summary>
        /// CAD 명령 실행 시작 시 이벤트 핸들러 (Empty)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void commandWillStart(object sender, CommandEventArgs e)
        {
            logger.Debug("---------------------------------------------------");
            logger.Debug(" >> CAD Document Event : commandWillStart");
            logger.Debug("---------------------------------------------------");
            logger.Debug($" - Global Command : '{e.GlobalCommandName}'");

            logger.Debug("commandWillStart ---------------------------------------------------\n");
        }

        /// <summary>
        /// CAD 명령 실행 종료 시 이벤트 핸들러
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void commandEnded(object sender, CommandEventArgs e)
        {
            logger.Debug("---------------------------------------------------");
            logger.Debug(" >> CAD Document Event : commandEnded");
            logger.Debug("---------------------------------------------------");
            logger.Debug($" - Global Command : '{e.GlobalCommandName}'");

            Document document = CadApplication.DocumentManager.MdiActiveDocument;

            ProtectedDocument protectedDocument = PluginApplication.documentHandler.getOpenDocument(document);

            if (protectedDocument == null)
            {
                protectedDocument = new ProtectedDocument(document);
            }

            if (e.GlobalCommandName.Equals("SAVE") || e.GlobalCommandName.Equals("SAVEAS") || e.GlobalCommandName.Equals("QSAVE"))
            {
                // 저장 명령이 실행된 경우 업데이트 여부를 false로 변경 (파일 명 옆의 * 제거)

                protectedDocument.isUpdated = false;
            }

            if (!e.GlobalCommandName.Equals("OPEN"))
            {
                // Open 명령이 아닌 경우 변경된 문서 정보를 업데이트

                PluginApplication.documentHandler.replaceOpenDocument(protectedDocument);
            }

            logger.Debug("commandEnded ---------------------------------------------------\n");
        }

        /// <summary>
        /// CAD 명령 실행 취소 시 이벤트 핸들러
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void commandCancelled(object sender, CommandEventArgs e)
        {
            logger.Debug("---------------------------------------------------");
            logger.Debug(" >> CAD Document Event : commandCancelled");
            logger.Debug("---------------------------------------------------");
            logger.Debug($" - Global Command : '{e.GlobalCommandName}'");

            logger.Debug("commandCancelled ---------------------------------------------------\n");
        }

        /// <summary>
        /// CAD 명령 실행 실패 시 이벤트 핸들러
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void commandFailed(object sender, CommandEventArgs e)
        {
            logger.Debug("---------------------------------------------------");
            logger.Debug(" >> CAD Document Event : commandFailed");
            logger.Debug("---------------------------------------------------");
            logger.Debug($" - Global Command : '{e.GlobalCommandName}'");

            logger.Debug("commandFailed ---------------------------------------------------\n");
        }

        /// <summary>
        /// 파일 저장 시작 시 이벤트 핸들러
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void beginSave(object sender, DatabaseIOEventArgs e)
        {
            logger.Debug("---------------------------------------------------");
            logger.Debug(" >> CAD Document Event : beginSave");
            logger.Debug("---------------------------------------------------");

            Document doc = CadApplication.DocumentManager.MdiActiveDocument;

            ProtectedDocument activedDocument = PluginApplication.documentHandler.getOpenDocument(doc.Window.Handle);

            if (activedDocument != null)
            {
                if (activedDocument.isEdit == false)
                {
                    String errorMessage = PluginApplication.global.getMessage("error.deny.save.document");      // 해당 도면을 저장할 수 있는 권한이 없습니다.

                    logger.Debug($" - {errorMessage}");

                    this.OnReceiveErrorMessage?.Invoke(this, new MessageEventArgs(errorMessage, "Warning"));    // 이벤트 발생. 경고 메세지 폼 호출
                }
                if (activedDocument.isEdit == true)
                {
                    activedDocument.needLabeling = true;    // 저장 명령어가 실행되면 needLabeling을 true로 설정 (실제로 파일이 저장된다)
                }
            }

            // 임시 경로에 대한 everyone 사용자 권한 허용
            FileManager.setUserMipTempWritePermissions(true);

            logger.Debug("beginSave ---------------------------------------------------\n");
        }

        /// <summary>
        /// 파일 저장 완료시 이벤트 핸들러
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void saveComplete(object sender, DatabaseIOEventArgs e)
        {
            logger.Debug("---------------------------------------------------");
            logger.Debug(" >> CAD Document Event : saveComplete");
            logger.Debug("---------------------------------------------------");

            // 임시 경로에 대한 everyone 사용자 권한 해제
            FileManager.setUserMipTempWritePermissions(false);

            logger.Debug("saveComplete ---------------------------------------------------\n");
        }

        /// <summary>
        /// 열려 있는 CAD 문서 정보와 보호화된 문서 정보를 닫는다.
        /// </summary>
        public void closeDocuments()
        {
            foreach (Document cadDocument in CadApplication.DocumentManager)
            {
                ProtectedDocument protectedDocument = PluginApplication.documentHandler.getOpenDocument(cadDocument);

                if (protectedDocument != null)
                {
                    PluginApplication.documentHandler.removeOpenDocument(protectedDocument);

                    logger.Debug($" - closeDocuments : {protectedDocument.ToString()}");
                }

                cadDocument.CloseAndDiscard();
            }
        }

        // 전달 받은 명령어가 명령어 종류에 해당하는 명령어 셋에 포함되는지 체크 
        private bool isContainCommand(String eventCommand, String commandType)
        {
            if (String.IsNullOrEmpty(eventCommand) || String.IsNullOrEmpty(commandType))
            {
                return false;
            }

            bool result = false;

            List<String> commandList = DocumentConstants.DICTIONARY_OF_COMMAND.TryGetValue(commandType, out List<String> commands) ? commands : null;

            // 출력 관련 명령어    
            foreach (String command in commandList!)
            {
                if (command.Contains(eventCommand))  // compareTo
                {
                    result = true;
                    break;
                }
            }

            return result;
        }

        // 주요 명령어 여부 체크
        private void printLog(String command, String message)
        {
            if (String.IsNullOrEmpty(command) || command.Equals("#") || command.Equals(""))
            {
                return;
            }

            foreach (KeyValuePair<String, List<String>> pairSet in DocumentConstants.DICTIONARY_OF_COMMAND)
            {

                // 저장 관련 명령어
                foreach (String compareCommand in pairSet.Value!)
                {
                    if (compareCommand.Contains(command))
                    {
                        logger.Debug(message);

                        break;
                    }
                }
            }
        }

        #region Plot Audit Log Event Handlers

        private void OnBeginDocument(object sender, BeginDocumentEventArgs e)
        {
            try
            {
                logger.Info($"[Audit Log] Begin Document | DocumentName: {e.DocumentName} | OutputFileName: {e.FileName} | PlotToFile: {e.PlotToFile}");
            }
            catch (Exception ex)
            {
                logger.Error("Error in OnBeginDocument event handler", ex);
            }
        }

        private void OnBeginPage(object sender, BeginPageEventArgs e)
        {
            try
            {
                string layoutName = "Unknown";
                if (e.PlotInfo != null && e.PlotInfo.Layout != ObjectId.Null)
                {
                    Database db = e.PlotInfo.Layout.Database ?? HostApplicationServices.WorkingDatabase;
                    if (db != null)
                    {
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            Layout layout = tr.GetObject(e.PlotInfo.Layout, OpenMode.ForRead) as Layout;
                            if (layout != null)
                            {
                                layoutName = layout.LayoutName;
                            }
                            tr.Commit();
                        }
                    }
                }
                logger.Info($"[Audit Log] Begin Page | LayoutName: {layoutName}");
            }
            catch (Exception ex)
            {
                logger.Error("Error in OnBeginPage event handler", ex);
            }
        }

        #endregion
    }
}


