using ZwSoft.ZwCAD.ApplicationServices;
using eDIAN.Data;
using eDIAN.Main.API;
using eDIAN.Main.Core;
using eDIAN.Main.Data;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using CadApplication = ZwSoft.ZwCAD.ApplicationServices.Application;
using Image = System.Windows.Controls.Image;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using System;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace eDIAN.Main.UI
{
    public partial class MainForm
    {
        private String lastAccessPath = "";

        /// <summary>
        /// 플러그인 자체 파일 열기 창 팝업
        /// </summary>
        /// <returns></returns>
        public String showFileOpenDialog()
        {
            logger.Debug("----------------------------------------------------");
            logger.Debug("* MainForm : showFileOpenDialog ");
            logger.Debug("----------------------------------------------------");

            String result = "";

            //파일오픈창 생성 및 설정

            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = PluginApplication.global.getTitle("open.document") ;        // 도면 열기
                ofd.FileName = "";
                ofd.Filter = PluginApplication.global.getTitle("document.type");        // 도면 파일 (*.dwg)|*.dwg|암호화된 도면 파일 (*.pdwg)|*.pdwg

                if (String.IsNullOrEmpty(this.lastAccessPath) || this.lastAccessPath.Equals(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)))
                {
                    ofd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);        // 열기 기본 경로 설정
                }
                else 
                {
                    ofd.InitialDirectory = this.lastAccessPath;
                }

                //파일 오픈창 로드

                DialogResult dr = ofd.ShowDialog();

                //OK버튼 클릭시

                if (dr == DialogResult.OK)
                {
                    result = ofd.FileName;
                }
                else if (dr == DialogResult.Cancel)
                {
                    result = "";
                }

                this.lastAccessPath = Path.GetDirectoryName(result) ?? "";
            }

            logger.Debug($" - Opened file : '...\\{Path.GetFileName(result)}'");

            return result;
        }

        /// <summary>
        /// 플러그인 자체 파일 저장하기 창 팝업
        /// </summary>
        /// <returns>선택된 파일 전체 경로 (취소 시 빈 문자열)</returns>
        public String ShowFileSaveDialog()
        {
            logger.Debug("----------------------------------------------------");
            logger.Debug("* MainForm : ShowFileSaveDialog ");
            logger.Debug("----------------------------------------------------");

            String result = String.Empty;

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Title = PluginApplication.global.getTitle("save.document");        // 도면 저장;
                sfd.FileName = "";
                sfd.Filter = PluginApplication.global.getTitle("document.type");        // 도면 파일 (*.dwg)|*.dwg|암호화된 도면 파일 (*.pdwg)|*.pdwg

                if (String.IsNullOrEmpty(this.lastAccessPath) || this.lastAccessPath.Equals(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)))
                {
                    sfd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);        // 열기 기본 경로 설정
                }
                else
                {
                    sfd.InitialDirectory = this.lastAccessPath;
                }

                sfd.OverwritePrompt = true; // 동일 이름 존재 시 확인창 표시

                DialogResult dr = sfd.ShowDialog();

                if (dr == DialogResult.OK)
                {
                    result = sfd.FileName;
                }
                else if (dr == DialogResult.Cancel)
                {
                    result = String.Empty;
                }

                this.lastAccessPath = Path.GetDirectoryName(result) ?? "";
            }

            logger.Debug($" - Saved file : '...\\{Path.GetFileName(result)}'");

            return result;
        }

        /// <summary>
        /// 인증 상태에 따른 UI 업데이트 (로그인/로그아웃 버튼 텍스트, 사용자 정보, 파일 열기 버튼 활성화)
        /// </summary>
        /// <param name="state"></param>
        private async Task changeFormByAuthStatusAsync()
        {
            List<UserLicenseData> licenseList = null;

            if (PluginApplication.authStatus == CommonConstants.AuthStatus.AUTH && IApplicationService.instance != null)
            {
                licenseList = await IApplicationService.instance
                    .CallGetUserLicenseDataListAsync()
                    .ConfigureAwait(false);
            }

            await Dispatcher.InvokeAsync(() => applyChangeFormByAuthStatusUi(licenseList));
        }

        /// <summary>
        /// WPF 컨트롤 갱신 — UI 스레드에서만 호출.
        /// </summary>
        private void applyChangeFormByAuthStatusUi(List<UserLicenseData> licenseList)
        {
            if (PluginApplication.authStatus == CommonConstants.AuthStatus.AUTH)
            {
                logger.Debug($"identification : {PluginApplication.userLicenseData.userId}, {PluginApplication.userLicenseData.userName}");

                if (PluginApplication.userLicenseData.hasLicense)
                {
                    logger.Debug("- Microsoft Information Protection 인증 성공. 관리 시스템에 등록된 사용자");
                }
                else
                {
                    logger.Debug("- Microsoft Information Protection 인증 성공. 관리 시스템에 등록되지 않은 사용자");
                }

                String licenseType = PluginApplication.userLicenseData.licenseType ?? "";

                if (licenseType.Equals("F"))
                {
                    IconLicenseType.Source = PluginFormManager.ToBitmapImage(Properties.Resources.icon_license_premium);
                }
                else if (licenseType.Equals("D"))
                {
                    IconLicenseType.Source = PluginFormManager.ToBitmapImage(Properties.Resources.icon_license_basic);
                }
                else
                {
                    IconLicenseType.Source = PluginFormManager.ToBitmapImage(Properties.Resources.icon_license_viewer);
                }

                UserName.Text = PluginApplication.userLicenseData.userName ?? "";
                UserName.ToolTip = PluginApplication.userLicenseData.userId ?? "";

                ImageAuthentification.Source = PluginFormManager.ToBitmapImage(Properties.Resources.image_logout_on);
                ImageAuthentification.Tag = "image_logout_on";

                ButtonOpenFile.Visibility = Visibility.Visible;

                PluginApplication.userLicenseDataList = licenseList;
            }
            else
            {
                logger.Debug($"identification : none");

                IconLicenseType.Source = null;

                UserName.Text = "";
                UserName.ToolTip = "";

                ImageAuthentification.Source = PluginFormManager.ToBitmapImage(Properties.Resources.image_login_on);
                ImageAuthentification.Tag = "image_login_on";

                ButtonOpenFile.Visibility = Visibility.Hidden;

                PluginApplication.userLicenseDataList = null;
            }

            for (int i = 0; i < DocumentHandler.itemList.Count; i++)
            {
                if (!DocumentHandler.itemList[i].source.isProtected && !DocumentHandler.itemList[i].source.isNewFile)
                {
                    DocumentHandler.itemList[i].source.Dispose();

                    DocumentHandler.itemList[i] = new DocumentListItem(DocumentHandler.itemList[i].id, Path.GetFileName(DocumentHandler.itemList[i].source.filePath), DocumentHandler.itemList[i].source);

                    logger.Debug(DocumentHandler.toString());
                }
            }
        }

        /// <summary>
        /// 로그인/로그아웃 버튼 마우스 오버 이벤트 (이미지 반전)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void toggleImageAuthentification(Object sender, MouseEventArgs e)
        {
            Image image = sender as Image;

            if (image == null)
            {
                return;
            }

            if(image.Tag == null)
            {
                return;
            }

            if(image.Tag.Equals("image_login_on"))
            {
                image.Source = PluginFormManager.ToBitmapImage(Properties.Resources.image_login_over);
                image.Tag = "image_login_over";
            }
            else if(image.Tag.Equals("image_logout_on"))
            {
                image.Source = PluginFormManager.ToBitmapImage(Properties.Resources.image_logout_over);
                image.Tag = "image_logout_over";
            }
            else if (image.Tag.Equals("image_login_over"))
            {
                image.Source = PluginFormManager.ToBitmapImage(Properties.Resources.image_login_on);
                image.Tag = "image_login_on";
            }
            else if (image.Tag.Equals("image_logout_over"))
            {
                image.Source = PluginFormManager.ToBitmapImage(Properties.Resources.image_logout_on);
                image.Tag = "image_logout_on";
            }
        }

        /// <summary>
        /// 권한 없음. 메세지 표시
        /// </summary>
        /// <param name="errorMessage">출력할 메세지</param>
        private void showMessage(String errorMessage, String messageKind = null)
        {
            if (String.IsNullOrEmpty(errorMessage))
            {
                errorMessage = PluginApplication.global.getMessage("need.auth");   // 수행 권한이 없습니다.;
            }

            MessageHandler.Show(errorMessage, messageKind);
        }

        /******************************************************************************************************************/

        /// <summary>
        /// 로그인 상태에 따른 메인 UI 구성 변경
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void changeFormByAuthstatus(Object sender, EventArgs e)
        {
            _ = this.changeFormByAuthStatusSafeAsync();
        }

        private async Task changeFormByAuthStatusSafeAsync()
        {
            try
            {
                await this.changeFormByAuthStatusAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error("changeFormByAuthStatusAsync", ex);
            }
        }

        /// <summary>
        /// 전달 받은 메세지 출력 이벤트
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void showNotificationWindow(Object sender, MessageEventArgs e)
        {
            // 권한 제한 메세지 출력
            this.showMessage(e.message, e.messageKind);
        }

        /// <summary>
        /// 열린 모든 파일을 닫는 이벤트
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void closeDocuments(Object sender, EventArgs e)
        {
            // 열려 있는 CAD 문서 파일을 모두 닫는다.
            this.documentController.closeDocuments();
        }

        /// <summary>
        /// 보호된 파일에 대한 백업 생성 이벤트
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void applyProtectionToTempFile(Object sender, ProtectedDocumentEventArgs e)
        {
            _ = this.applyProtectionToTempFileAsync(e);
        }

        private async Task applyProtectionToTempFileAsync(ProtectedDocumentEventArgs e)
        {
            if (e.protectedDocument == null)
            {
                return;
            }

            try
            {
                await this.protectionController.applyProtectionToTempFile(e.protectedDocument).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error("applyProtectionToTempFileAsync", ex);
            }
        }

        /// <summary>
        /// 활성화 된 문서에 대한 리스트 폼 활성화 처리 이벤트 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void activeDocument(Object sender, ProtectedDocumentEventArgs e) 
        {
            this.activedDocument();
        }

        // 활성화 된 문서를 열린 문서 목록에서 선택 상태로 반전
        private void activedDocument()
        {            
            try
            {
                Document activeDoc = CadApplication.DocumentManager.MdiActiveDocument;

                if (activeDoc == null || DocumentList.Items.Count == 0)
                {
                    return;
                }

                // 파일 경로 기준으로 DocumentListItem 찾기

                for (int i = 0; i < DocumentList.Items.Count; i++)
                {
                    DocumentListItem item = (DocumentListItem)DocumentList.Items[i];

                    if (item?.source == null || string.IsNullOrEmpty(item.source.filePath))
                    {
                        continue;
                    }

                    bool isMatch = (item.source.filePath.Equals(activeDoc.Name, StringComparison.OrdinalIgnoreCase) || item.source.decryptedTemporaryFilePath.Equals(activeDoc.Name, StringComparison.OrdinalIgnoreCase))
                        && item.source.hashCode == activeDoc.GetHashCode()
                        && item.source.isReadOnly == activeDoc.IsReadOnly;

                    // 경로 비교 (대소문자 무시, 필요하면 Path.GetFullPath 등으로 보정)
                    if (isMatch)
                    {
                        // 리스트 쪽 선택 인덱스 동기화
                        int viewIndex = i;    // this.getIndexBySorting(i);

                        if (viewIndex >= 0 && viewIndex < DocumentList.Items.Count)
                        {
                            DocumentList.SelectedIndex = viewIndex;
                        }

                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error("activeDocument error", ex);
            }

        }
    }

    /// <summary>
    /// 전체 폭 * 비율 값을 계산하는 컨버터.
    /// ConverterParameter 로 0.7 같은 비율(double)을 넘깁니다.
    /// 필요하면 아이콘/닫기 버튼 폭(고정값)을 더 빼는 식으로 조정 가능합니다.
    /// </summary>
    public class WidthRatioConverter : IValueConverter
    {
        public Object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not double totalWidth)
                return System.Windows.Data.Binding.DoNothing;

            double ratio = 1.0;

            if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double r))
            {
                ratio = r;
            }

            // 전체 폭 * 비율
            double result = totalWidth * ratio;

            // 좌우 여백, 아이콘(25), 닫기(24) 등 고려해서 조금 빼고 싶으면 여기서 조정
            // 예: result -= 40;
            if (result < 0)
                result = 0;

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}