using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using eDIAN.Core;
using eDIAN.Data;
using eDIAN.Hook;
using eDIAN.Main.API;
using eDIAN.Main.Core;
using eDIAN.Main.Data;
using log4net;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CadApplication = ZwSoft.ZwCAD.ApplicationServices.Application;
using CadCoreApplication = ZwSoft.ZwCAD.ApplicationServices.Core.Application;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
using System;
using System.Windows.Forms;
using System.Threading.Tasks;


namespace eDIAN.Main.UI
{
    /// <summary>
    /// MainForm.xaml에 대한 로직
    /// </summary>
    public partial class MainForm : UserControl
    {
        private static ILog logger = PluginLogger.getLogger("MainForm", "application.log");

        // 서비스 클라이언트 
        private ServiceClient serviceClient;

        // MIP 인증 보안 처리 객체
        private ProtectionController protectionController;

        // 문서 관리 객체
        private DocumentController documentController;

        public MainForm()
        {
            logger.Debug("======================================================");
            logger.Debug("MainForm Constructor");
            logger.Debug("======================================================");

            //////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // 메인 어플리케이션의 패키지 버전이 플러그인이 사용하는 것보다 낮은 버전일 경우 플러그인의 상위 패키지로 대체
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////

            PluginInitializer.preloadAssembly();

            //////////////////////////////////////////////////////////////////////////////////////////////////////////////

            // 1. 서비스 클라이언트 기동 및 이벤트 등록 (서비스 서버 기동 후 클라이언트 접속)

            this.serviceClient = new ServiceClient();

            this.serviceClient.OnConnectToServiceServer += this.closeDocuments;

            // 2. 메인 폼 컴포넌트 초기화 및 이벤트 핸들러 등록

            this.InitializeComponent();

            this.Loaded += this.loadedForm;
            this.IsVisibleChanged += this.changeVisibleForm;
            this.Unloaded += this.unloadedForm;

            // 팔렛에 초기화된 열린 문서 목록 바인딩 

            DocumentList.ItemsSource = DocumentHandler.itemList;

            // 여기서 역순 정렬 (예: indexNumber 기준 내림차순)

            ICollectionView view = CollectionViewSource.GetDefaultView(DocumentList.ItemsSource);

            view.SortDescriptions.Clear();

            ListSortDirection sorting = ListSortDirection.Descending;

            if (CommonConstants.IS_ASC == true)
            {
                // 오름차순 정렬 세팅
                sorting = ListSortDirection.Ascending;
            }

            view.SortDescriptions.Add(new SortDescription(nameof(DocumentListItem.id), sorting));

            // 4. 보안 문서 정보 처리 객체 생성 및 이벤트 핸들러 등록

            this.documentController = new DocumentController();

            this.documentController.OnReceiveErrorMessage += this.showNotificationWindow;
            this.documentController.OnDestroyDocument += this.applyProtectionToTempFile;

            // 5. CAD 관련 이벤트 핸들러 등록

            CadApplication.SystemVariableChanged += this.documentController.systemVariableChanged;
            CadApplication.QuitWillStart += this.documentController.quitWillStart;

            CadApplication.DocumentManager.DocumentCreated += this.documentController.documentCreated;
            CadApplication.DocumentManager.DocumentToBeDestroyed += this.documentController.documentToBeDestroyed;
            CadApplication.DocumentManager.DocumentDestroyed += this.documentController.documentDestroyed;
            CadApplication.DocumentManager.DocumentActivated += this.documentController.documentActivated;
            CadApplication.DocumentManager.DocumentLockModeChanged += this.documentController.documentLockModeChanged;

            // 6. MIP 인증 보안 처리 객체 생성 및  이벤트 핸들러 등록

            this.protectionController = new ProtectionController();

            this.protectionController.OnSignout += this.closeDocuments;
            this.protectionController.OnChangeAuthStatus += this.changeFormByAuthstatus;
            this.protectionController.OnRaiseError += this.showNotificationWindow;

            // 7. 메인 컨트롤러 (서비스 클라이언트, MIP 인증 보안 처리 객체, 문서 관리 객체 등 전달)   이벤트 처리

            PluginApplication.documentHandler.OnActivedDocument += this.activeDocument;

            // 클립보드 내용 변경 이벤트 핸들러 등록
            ClipboardNotification.OnUpdateClipboard += this.updateClipboard;

            // 8. 클래식 메뉴 표시 여부 초기화 

            DocumentConstants.CLASSIC_MENU_VISIBLE = Convert.ToInt32(CadCoreApplication.GetSystemVariable("MENUBAR"));

            DocumentConstants.LAST_CLASSIC_MENU_VISIBLE = DocumentConstants.CLASSIC_MENU_VISIBLE;


            logger.Debug("======================================================DocumentForm \n");
        }

        /// <summary>
        /// 폼이 로드될 때 이벤트 (MIP 인증 수행, 서비스 서버 접속, 열린 문서 정보로 열린 문서 목록 초기화 등)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void loadedForm(object sender, RoutedEventArgs e)
        {
            _ = this.loadedFormAsync();
        }

        private async Task loadedFormAsync()
        {
            try
            {
                logger.Debug("======================================================");
                logger.Debug("MainForm Loaded");
                logger.Debug("======================================================");

                await this.changeFormByAuthStatusAsync().ConfigureAwait(true);

                await this.protectionController.createApplication().ConfigureAwait(true);

                FileManager.setEveryoneMipTempPermission(false);

                await this.protectionController.executeAuthentificationAsync(CommonConstants.CAD_MAIN_WINDOW_HANDLE).ConfigureAwait(true);

                await this.serviceClient.connectToServiceServer().ConfigureAwait(true);

                logger.Debug("====================================================== loadedForm \n");
            }
            catch (Exception ex)
            {
                logger.Error("loadedFormAsync", ex);
            }
        }

        /// <summary>
        /// 폼이 보여질 때 이벤트 (열린 CAD 문서와 보안 문서 정보로 열린 문서 목록 초기화) 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void changeVisibleForm(Object sender, DependencyPropertyChangedEventArgs e)
        {
            logger.Debug("======================================================");
            logger.Debug("MainForm IsVisibleChanged");
            logger.Debug("======================================================");

            if (this.IsVisible)
            {
                // 폼이 보여질 때 열린 CAD 문서와 보안 문서 정보로 열린 문서 목록 초기화

                this.loadDocuments();
            }

            logger.Debug("====================================================== changeVisibleForm\n");
        }

        /// <summary>
        /// 폼이 닫힐 때 이벤트 (폼 리소스 해제, 이벤트 핸들러 해제, 열린 문서 닫기, MIP temp 파일 삭제 등)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void unloadedForm(object sender, RoutedEventArgs e)
        {
            logger.Debug("======================================================");
            logger.Debug("MainForm Loaded");
            logger.Debug("======================================================");

            // 클래식 메뉴바를 원래 세팅된 형태로 되돌린다. 
            CadCoreApplication.SetSystemVariable("MENUBAR", DocumentConstants.CLASSIC_MENU_VISIBLE);

            // MIP temp 경로에 있는 모든 임시 파일 삭제
            FileManager.deleteMipTempFiles();

            // 열려 있는 CAD 문서 파일을 모두 닫는다.
            this.documentController.closeDocuments();

            // 메인 폼의 이벤트 핸들러 제거 

            // 폼 이벤트 핸들러 등록

            this.Loaded -= this.loadedForm;
            this.IsVisibleChanged -= this.changeVisibleForm;
            this.Unloaded -= this.unloadedForm;

            // 보안 문서 정보 활성화 이벤트 핸들러 해제 
            this.documentController.OnReceiveErrorMessage -= this.showNotificationWindow;
            this.documentController.OnDestroyDocument -= this.applyProtectionToTempFile;

            // CAD 관련 이벤트 해제
            CadApplication.SystemVariableChanged -= this.documentController.systemVariableChanged;
            CadApplication.QuitWillStart -= this.documentController.quitWillStart;

            CadApplication.DocumentManager.DocumentCreated -= this.documentController.documentCreated;
            CadApplication.DocumentManager.DocumentToBeDestroyed -= this.documentController.documentToBeDestroyed;
            CadApplication.DocumentManager.DocumentDestroyed -= this.documentController.documentDestroyed;
            CadApplication.DocumentManager.DocumentActivated -= this.documentController.documentActivated;
            CadApplication.DocumentManager.DocumentLockModeChanged -= this.documentController.documentLockModeChanged;

            // 클립보드 내용 변경 이벤트 핸들러 해제
            ClipboardNotification.OnUpdateClipboard -= this.updateClipboard;

            // MIP 인증 보안 처리 관련 객체 이벤트 해제
            this.protectionController.OnSignout -= this.closeDocuments;
            this.protectionController.OnChangeAuthStatus -= this.changeFormByAuthstatus;
            this.protectionController.OnRaiseError -= this.showNotificationWindow;

            // 파일 활성화, 변경시 UI 처리

            PluginApplication.documentHandler.OnActivedDocument -= this.activeDocument;

            // MIP 인증 보안 처리 관련 객체 리소스 릴리즈
            this.protectionController.release();

            // 서비스 클라이언트 이벤트 삭제

            this.serviceClient.OnConnectToServiceServer -= this.closeDocuments;

            logger.Debug("====================================================== unloadedForm\n");
        }

        /// <summary>
        /// 열려져 있는 CAD 문서와 전달 받은 ProtectedDocumentCollection 에 해당하는 정보로 DocumentListItem 목록 생성 
        /// </summary>
        private void loadDocuments()
        {
            foreach (Document document in CadApplication.DocumentManager)
            {
                if (document == null)
                {
                    // 열린 CAD 문서 목록에 Document 객체가 없을 경우 건너뜀
                    continue;
                }

                // 파일 경로 기준으로 검색
                ProtectedDocument protectedDocument = PluginApplication.documentHandler.getOpenDocument(document);

                if (protectedDocument == null)
                {
                    // 검색 결과가 없을 경우 Document 객체를 이용해 새로 생성
                    protectedDocument = new ProtectedDocument(document);
                }

                // DocumentListItem 생성해 목록에 추가
                DocumentHandler.itemList.Add(new DocumentListItem(Path.GetFileName(protectedDocument.filePath), protectedDocument));

                logger.Debug(DocumentHandler.toString());
            }

            // 추가된 문서 활성화 

            if (DocumentList.Items.Count > 0)
            {
                DocumentList.SelectedIndex = DocumentList.Items.Count - 1;
            }
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// 목록 선택 아이템이 변경 될 경우 (마우스 클릭)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void onChangedDocument(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DocumentList.SelectedItem is not DocumentListItem selectedItem)
            {
                return;
            }

            ProtectedDocument pd = selectedItem.source;

            if (pd == null || string.IsNullOrEmpty(pd.filePath))
            {
                return;
            }

            // 파일 경로로 CAD 문서 찾기 (searchDocument 내부가 이미 경로/ProtectedDocument 기반 검색이라면 그대로 사용)

            Document doc = PluginApplication.documentHandler.searchDocument(pd);

            if (doc != null)
            {
                CadApplication.DocumentManager.MdiActiveDocument = doc;
            }
        }

        /// <summary>
        /// 닫기 (x) 버튼 클릭 시 해당 문서 닫기
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void onClickButtonClose(object sender, RoutedEventArgs e)
        {
            logger.Debug("======================================================");
            logger.Debug("* MainForm Event : onClickButtonClose");
            logger.Debug("======================================================");

            DocumentListItem item = (sender as System.Windows.Controls.Button)?.DataContext as DocumentListItem;

            if (item == null)
            {
                return;
            }

            // 실제 AutoCAD 문서 찾기

            ProtectedDocument protectedDocument = item.source;

            Document document = PluginApplication.documentHandler.searchDocument(protectedDocument);

            if (protectedDocument == null || document == null)
            {
                DocumentHandler.itemList.Remove(item);

                return;
            }

            this.closeDocument(document, protectedDocument);

            logger.Debug("====================================================== onClickButtonClose\n");
        }

        // 파일 닫기 처리 (저장 여부 확인, 권한에 따른 저장 가능 여부 확인, 예외 처리 등)
        private void closeDocument(Document document, ProtectedDocument protectedDocument)
        {
            if (document == null || protectedDocument == null)
            {
                return;
            }

            try
            {
                bool isClosedAndSaved = false;

                using (document.LockDocument())
                {
                    if (protectedDocument.isUpdated == true)
                    {
                        if (protectedDocument.isProtected == true && protectedDocument.isEdit == false)
                        {
                            MessageHandler.Show("error.deny.save.document"); // 도면이 변경되었으나 저장할 수 있는 권한이 없습니다.
                        }
                        else
                        {
                            if (protectedDocument.isProtected == true && protectedDocument.isEdit == true)
                            {
                                protectedDocument.needLabeling = true;
                            }

                            // 변경사항이 있을 경우 Confirm 대화상자 표시     "파일을 저장하시겠습니까?", "도면 저장"

                            String confirmMessage = Path.GetFileName(protectedDocument.filePath) + " " + PluginApplication.global.getMessage("confirm.save.document");

                            DialogResult result = MessageHandler.Show(confirmMessage, "save.document", "Question");

                            if (result != DialogResult.Cancel)
                            {
                                if (result == DialogResult.Yes)
                                {
                                    // Yes 버튼 클릭 : 저장 후 닫기  
                                    isClosedAndSaved = true;
                                }
                                else
                                {
                                    // No 버튼 클릭 : 저장하지 않고 닫기
                                    isClosedAndSaved = false;
                                }
                            }
                            else
                            {
                                return;
                            }
                        }
                    }
                }

                if (isClosedAndSaved)
                {
                    String filePath = document.Name;

                    // 저장 후 닫기 상태인 경우 

                    if (protectedDocument.isProtected == false && protectedDocument.isNewFile == true)
                    {
                        // 신규 파일인 경우 파일 저장 팝업 표시

                        using (SaveFileDialog sfd = new SaveFileDialog())
                        {
                            sfd.Title = "도면 파일 저장";
                            sfd.FileName = Path.GetFileName(filePath) ?? "";
                            sfd.Filter = "도면파일 (*.dwg)|*.dwg|암호화 도면파일 (*.pdwg)|*.pdwg";

                            sfd.OverwritePrompt = true; // 동일 이름 존재 시 확인창 표시

                            DialogResult dr = sfd.ShowDialog();

                            if (dr == DialogResult.OK)
                            {
                                filePath = sfd.FileName;
                            }
                            else
                            {
                                return;
                            }
                        }
                    }

                    if (!String.IsNullOrEmpty(filePath))
                    {
                        // 파일 명이 존재하는 경우 "저장만" 수행 (문서는 닫지 않음)
                        using (document.LockDocument())
                        {
                            document.Database.SaveAs(filePath,
                                true,                       // bak 파일 생성 여부
                                DwgVersion.Current,          // 현재 버전으로 저장
                                document.Database.SecurityParameters
                            );
                        }
                    }
                }

                if (document != null && !document.IsDisposed && document.UnmanagedObject != IntPtr.Zero)
                {
                    try
                    {
                        document.CloseAndDiscard(); // 저장하지 않고 닫기
                    }
                    catch (Exception e)
                    {
                        logger.Error(" - closeDocument: CloseAndDiscard() failed, trying Close() instead.", e);
                    }
                }
            }
            catch (COMException ex)
            {
                // 도면이 사용 중일 때 등 COM 예외에 대한 별도 처리

                String message = $"해당 도면이 AutoCAD에서 사용 중이어서 닫을 수 없습니다.\n\n도면 작업(명령 실행 등)이 완료된 후 다시 시도해 주세요.";

                logger.Error(message, ex);

                MessageHandler.Show(message, "Warning");
            }
            catch (System.Exception ex)
            {
                String message = $"도면을 닫는 중 오류가 발생했습니다.";

                logger.Error(message, ex);

                MessageHandler.Show(message, "Error");
            }
        }

        /// <summary>
        /// 설정 버튼 클릭 이벤트 (설정 창 팝업)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void onClickButtonSetting(object sender, RoutedEventArgs e)
        {
            PluginApplication.pluginFormManager.applyPluginSettingForm();
        }

        /// <summary>
        /// 로그인 버튼 클릭 이벤트 (로그인 창 호출 또는 로그인 처리 로직)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void onClickButtonLogin(object sender, RoutedEventArgs e)
        {
            _ = this.onClickButtonLoginAsync();
        }

        private async Task onClickButtonLoginAsync()
        {
            try
            {
                logger.Debug("======================================================");
                logger.Debug("* MainForm Event : onClickButtonLogin");
                logger.Debug("======================================================");

                await this.protectionController.executeAuthentificationAsync(CommonConstants.CAD_MAIN_WINDOW_HANDLE).ConfigureAwait(true);

                logger.Debug("====================================================== onClickButtonLogin\n");
            }
            catch (Exception ex)
            {
                logger.Error("onClickButtonLoginAsync", ex);
            }
        }

        /// <summary>
        /// 파일 열기 버튼 클릭 이벤트 (파일 열기 창 팝업)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void onClickButtonOpenFile(object sender, RoutedEventArgs e)
        {
            _ = this.onClickButtonOpenFileAsync();
        }

        private async Task onClickButtonOpenFileAsync()
        {
            try
            {
                logger.Debug("======================================================");
                logger.Debug("* MainForm Event : onClickButtonOpenFile ");
                logger.Debug("======================================================");

                if (PluginApplication.connectStatus == CommonConstants.ConnectStatus.DISCONNECT)
                {
                    MessageHandler.Show("service.server.wait");
                    return;
                }

                String filename = this.showFileOpenDialog();

                if (!String.IsNullOrEmpty(filename))
                {
                    await this.protectionController.openDocumentFile(filename).ConfigureAwait(true);
                }

                logger.Debug("====================================================== onClickButtonOpenFile\n");
            }
            catch (Exception ex)
            {
                logger.Error("onClickButtonOpenFileAsync", ex);
            }
        }

        /// <summary>
        /// 적용 버튼 클릭 이벤트 (레이블 적용 폼 팝업)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void onClickButtonApply(Object sender, RoutedEventArgs e)
        {

            DocumentListItem item = (sender as System.Windows.Controls.Button)?.DataContext as DocumentListItem;

            if (item == null)
            {
                return;
            }

            // 클릭한 버튼이 속한 ItemList에서 ProtectedDocument 를 구한다.

            ProtectedDocument activeProtectedDocument = item.source;

            if (activeProtectedDocument == null)
            {
                return;
            }

            if (!File.Exists(activeProtectedDocument.filePath))
            {
                MessageHandler.Show("file.save.first");        // 파일을 먼저 저장하세요.

                // logger.Debug($"  - activeFilePath({activeProtectedDocument.filePath}) is not Exists.");

                return;
            }

            if (activeProtectedDocument.isUpdated)
            {
                MessageHandler.Show("protected.document.changed");   // 보호된 도면이 변경되었습니다. 먼저 변경내용을 저장하세요.        
                return;
            }

            // 정당성 사유 입력 창을 팝업하고 입력 받은 메세지를 세팅 (테스트)
            // String test = PluginApplication.pluginFormManager.setJustificationMessageForm();

            // 레이블 적용 폼 팝업

            JObject result = PluginApplication.pluginFormManager.applyLabelForm(this.protectionController, item);

            // 처리 결과 메세지 표시

            if (result != null && result.HasValues)
            {
                String errorCode = Convert.ToString(result["errorCode"]);
                String errorMessage = Convert.ToString(result["errorMessage"]);
                String resultMessage = Convert.ToString(result["resultMessage"]);

                // logger.Debug($" - clickBtnSetLabel : {resultMessage} [{errorCode}] {errorMessage}");

                MessageHandler.Show(resultMessage, errorCode == "0" ? "Info" : "Error");
            }

            logger.Debug("====================================================== onClickButtonApply\n");

        }

        /// <summary>
        /// [ClipboardNotification 이벤트 핸들러] 클립보드에 변경(파일 요소 추가. 즉 복사)시 
        /// 클립보드에 보안 파일 요소가 복사되어 포함된 경우 클립보드 내용 클리어
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void updateClipboard(Object sender, EventArgs e)
        {
            try
            {
                if (!System.Windows.Clipboard.ContainsFileDropList())
                {
                    return;
                }

                bool isProtectedDocument = false;

                foreach (String item in System.Windows.Clipboard.GetFileDropList())
                {
                    // logger.Debug($">>>>> updateClipboard item : " + item);

                    if (PluginApplication.documentHandler.getOpenDocument(item) != null)
                    {
                        logger.Warn($"클립보드에 보호 적용된 파일({item}) 이 복사되었습니다.");

                        isProtectedDocument = true;

                        break;
                    }
                }

                if (isProtectedDocument)
                {
                    System.Windows.Clipboard.Clear();

                    logger.Warn($"보호 적용된 파일은 클립보드에 복사할 수 없습니다.");

                    this.showMessage($"보호 적용된 파일은 클립보드에 복사할 수 없습니다.", "Error");
                }

            }
            catch (Exception ex)
            {
                logger.Error($" - updateClipboard : {ex.Message}");
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private Point _dragStartPoint;
        private bool _isDragging;

        private void previewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;
        }

        private void previewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point mousePos = e.GetPosition(null);

            Vector diff = mousePos - _dragStartPoint;

            // 약간 움직였는지 판단 (클릭과 구분)

            if (!_isDragging && (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                _isDragging = true;

                if (sender is not ListBox listBox)
                {
                    return;
                }

                ListBoxItem listBoxItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);

                if (listBoxItem == null)
                {
                    return;
                }

                DocumentListItem sourceItem = (DocumentListItem)listBox.ItemContainerGenerator.ItemFromContainer(listBoxItem);

                if (sourceItem == null)
                {
                    return;
                }

                DragDrop.DoDragDrop(listBoxItem, sourceItem, DragDropEffects.Move);
            }
        }

        private void dragOverListItem(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(DocumentListItem)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            if (sender is not ListBox listBox)
            {
                return;
            }

            // 현재 마우스 위치 기준으로 대상 ListBoxItem 찾기

            Point position = e.GetPosition(listBox);

            DependencyObject dependencyObject = VisualTreeHelper.HitTest(listBox, position).VisualHit;

            if (dependencyObject == null)
            {
                return;
            }

            ListBoxItem currentItemContainer = FindAncestor<ListBoxItem>(dependencyObject);

            // 이전에 하이라이트했던 항목이 있고, 그 항목이 지금 대상이 아니면 색 원복
            if (_highlightedItem != null && !ReferenceEquals(_highlightedItem, currentItemContainer))
            {
                _highlightedItem.ClearValue(BackgroundProperty);
                _highlightedItem.ClearValue(ForegroundProperty);
                _highlightedItem = null;
            }

            // 새 대상이 없으면 종료
            if (currentItemContainer == null)
            {
                return;
            }

            // 이미 하이라이트하고 있는 항목이면 그대로
            if (ReferenceEquals(_highlightedItem, currentItemContainer))
            {
                return;
            }

            // 새 대상 항목 색상 변경
            _highlightedItem = currentItemContainer;
            _highlightedItem.Background = System.Windows.Media.Brushes.LightBlue;   // 원하는 색
        }

        /// <summary>
        /// Drag 한 항목을 놓을때 처리 이벤트 (실제 교체 작업)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void dropListItem(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(DocumentListItem)))
            {
                return;
            }

            DocumentListItem sourceItem = (DocumentListItem)e.Data.GetData(typeof(DocumentListItem));

            logger.Debug($">>>>> sourceItem : {sourceItem.source.ToString()}");

            if (sender is not ListBox listBox)
            {
                return;
            }

            DocumentListItem targetItem = GetItemAtPoint(listBox, e.GetPosition(listBox));

            if (targetItem == null || Object.ReferenceEquals(sourceItem, targetItem))
            {
                // 드롭 시에도 하이라이트 해제
                if (_highlightedItem != null)
                {
                    _highlightedItem.ClearValue(BackgroundProperty);
                    _highlightedItem.ClearValue(ForegroundProperty);
                    _highlightedItem = null;
                }

                return;
            }

            logger.Debug($">>>>> targetItem : {targetItem.source.ToString()}");

            // !!!! 위치 변경 처리 

            int sourceIndex = PluginApplication.documentHandler.getOpenDocumentIndex(sourceItem.source);
            int targetIndex = PluginApplication.documentHandler.getOpenDocumentIndex(targetItem.source);

            DocumentHandler.itemList[sourceIndex] = new DocumentListItem(targetItem.id, Path.GetFileName(sourceItem.source.filePath), sourceItem.source);
            DocumentHandler.itemList[targetIndex] = new DocumentListItem(sourceItem.id, Path.GetFileName(targetItem.source.filePath), targetItem.source);

            this.activedDocument();

            // 드롭 이후 하이라이트 해제
            if (_highlightedItem != null)
            {
                _highlightedItem.ClearValue(BackgroundProperty);
                _highlightedItem.ClearValue(ForegroundProperty);
                _highlightedItem = null;
            }
        }

        private ListBoxItem _highlightedItem;

        // 특정 좌표에 있는 ListBoxItem 찾기
        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T typed)
                    return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static DocumentListItem GetItemAtPoint(ListBox listBox, Point point)
        {
            HitTestResult hitTestResult = VisualTreeHelper.HitTest(listBox, point);
            if (hitTestResult == null)
                return null;

            var listBoxItem = FindAncestor<ListBoxItem>(hitTestResult.VisualHit);
            if (listBoxItem == null)
                return null;

            return (DocumentListItem)listBox.ItemContainerGenerator.ItemFromContainer(listBoxItem);
        }
    }
}