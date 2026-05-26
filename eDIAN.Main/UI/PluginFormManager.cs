using ZwSoft.ZwCAD.Windows;
using eDIAN.Core;
using eDIAN.Data;
using eDIAN.Main.Data;
using log4net;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Window = System.Windows.Window;
using System;

namespace eDIAN.Main.UI
{
    public class PluginFormManager
    {
        private static readonly ILog logger = PluginLogger.getLogger("PluginFormManager", "application.log");

        private static PaletteSet paletteSet;

        public PluginFormManager()
        {

        }

        /// <summary>
        /// 메인 폼을 생성 후 도킹 팔렛에 추가
        /// </summary>
        public void loadMainForm()
        {
            if(paletteSet == null)
            {
                // * 팔렛이 존재하지 않는 경우, 새로 생성

                paletteSet = new PaletteSet("")
                {
                    DockEnabled = DockSides.Left | DockSides.Right,
                    Style = PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowPropertiesMenu
                };

                // 메인 폼을 팔렛에 추가
                MainForm mainForm = new MainForm();

                ElementHost host = new ElementHost
                {
                    Child = mainForm,
                    Dock = System.Windows.Forms.DockStyle.Fill
                };

                paletteSet.Add("Documents", host);

                // 팔렛을 화면 좌측에 도킹
                paletteSet.Dock = DockSides.Left;

                // 팔렛을 보이도록 설정
                paletteSet.Visible = true;
            }
            else 
            {
                // * 이미 팔렛이 존재하는 경우, 팔렛을 보이도록 설정

                paletteSet.Visible = true;

                try
                {
                    paletteSet.Activate(0);
                }
                catch (Exception ex)
                {
                    logger.Debug("paletteSet.Activate(0) failed.", ex);
                }

                return;
            }
        }

        /// <summary>
        /// JustificationMessageForm 을 팝업으로 띄우고, 사용자가 입력한 텍스트를 반환.
        /// 취소 또는 빈 문자열이면 null 반환.
        /// </summary>
        /// <returns>입력된 정당성 사유 문자열 또는 null</returns>
        public String setJustificationMessageForm()
        {
            ApplyJustificationForm applyJustificationForm = new ApplyJustificationForm();

            // CAD 화면을 Owner로 지정한 ApplyJustificationForm 윈도우 폼을 생성
            Window windowForm = this.createWindowForm(applyJustificationForm, CommonConstants.CAD_MAIN_WINDOW_HANDLE);

            // 모달 다이얼로그로 표시
            bool result = windowForm.ShowDialog() ?? false;

            String reasonMessage = String.Empty;

            if (result == true)
            {
                String text = applyJustificationForm.JustificationText?.Trim() ?? String.Empty;

                reasonMessage = String.IsNullOrEmpty(text) ? String.Empty : text;
            }

            return reasonMessage;
        }

        /// <summary>
        /// 사용자 정의 레이블 설정 폼 표시
        /// </summary>
        /// <param name="protectController">ProtectContorller 객체</param>
        /// <param name="item">레이블을 설정 할 문서 정보가 포함된 열린 문서 목록 항목</param>
        /// <returns></returns>
        public JObject applyLabelForm(ProtectionController protectController, DocumentListItem item)
        {
            ApplyLabelForm applyLabelForm = new ApplyLabelForm(protectController, item);

            // CCAD 화면을 Owner로 지정한 ApplyLabelForm 윈도우 폼을 생성
            Window windowForm = this.createWindowForm(applyLabelForm, CommonConstants.CAD_MAIN_WINDOW_HANDLE);

            // 사용자 정의 레이블 설정 폼을 모달 다이얼로그로 표시
            bool result = windowForm.ShowDialog() ?? false;

            // 사용자 정의 레이블 설정 결과 반환
            return applyLabelForm.applyResult ?? new JObject();
        }

        /// <summary>
        /// 플러그인 설정 폼 표시
        /// </summary>
        /// <returns></returns>
        public void applyPluginSettingForm()
        {
            ApplyPluginSettingForm applyPluginSettingForm = new ApplyPluginSettingForm();

            // CCAD 화면을 Owner로 지정한 ApplyPluginSettingForm 윈도우 폼을 생성

            Window windowForm = this.createWindowForm(applyPluginSettingForm, CommonConstants.CAD_MAIN_WINDOW_HANDLE);

            windowForm.ShowDialog();
        }

        /// <summary>
        /// 전달 받은 화면에 속한 폼을 생성(표시)
        /// </summary>
        /// <param name="contentControl">생성할 폼 객체</param>
        /// <param name="parentFormHandle">상위 화면의 handle 값</param>
        /// <returns>생성된 폼 객체</returns>
        /// <exception cref="ArgumentNullException"></exception>
        private Window createWindowForm(ContentControl contentControl, IntPtr parentFormHandle)
        {
            if(contentControl == null)
            {
                throw new ArgumentNullException(nameof(contentControl));
            }

            Window windowForm = new Window
            {
                Content = contentControl,
                WindowStyle = WindowStyle.None,     // 팝업 타이틀 바를 숨김
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = System.Windows.Media.Brushes.Transparent,
                AllowsTransparency = true,
                Owner = null
            };

            try
            {
                if (parentFormHandle != IntPtr.Zero)
                {
                    // 전달 받은 화면 Handle로 생성된 폼의 Owner를 저정  

                    WindowInteropHelper helper = new WindowInteropHelper(windowForm)
                    {
                        Owner = parentFormHandle
                    };
                }
            }
            catch(Exception ex )
            {
                logger.Error("createWindowForm Exception", ex);
            }

            return windowForm;
        }


        /// <summary>
        /// 리소스에 지정된 이미지 값을 BitmapImage 로 변환 
        /// </summary>
        /// <param name="bytes">리소스에 저장된 이미지 데이터</param>
        /// <returns></returns>
        public static BitmapImage ToBitmapImage(Byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return null!;
            }

            BitmapImage image = new BitmapImage();

            using (MemoryStream ms = new MemoryStream(bytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad; // 메모리에 모두 로드
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze(); // 스레드 세이프
            }

            return image;
        }
    }
}
