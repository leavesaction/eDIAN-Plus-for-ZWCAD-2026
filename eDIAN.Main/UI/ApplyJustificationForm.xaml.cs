using System.Windows;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;
using eDIAN.Main.Core;
using eDIAN.Data;
using System;

namespace eDIAN.Main.UI
{
    /// <summary>
    /// JustificationMessageForm 제어
    /// </summary>
    public partial class ApplyJustificationForm : UserControl
    {
        public String JustificationText
        {
            get => JustificationTextBox.Text;
        }

        public ApplyJustificationForm()
        {
            InitializeComponent();

            TitleTextBlock.Text = PluginApplication.global.getTitle("apply.justification");                                // 정당성 입력
            JustificationMessageTextBlock.Text = PluginApplication.global.getMessage("enter.justification.reason");        // 레이블 하향 적용에 대한 사유를 입력하세요.
        }

        private void onClickApplyJustification(object sender, RoutedEventArgs e)
        {
            String justificationMessage = JustificationTextBox.Text;

            if (!String.IsNullOrEmpty(justificationMessage))
            {
                this.closeForm(true);
            }
            else 
            {
                MessageHandler.Show("justification.required");        // 정당성 사유를 입력하세요.

                return;
            }
        }

        private void onClickCloseForm(object sender, RoutedEventArgs e)
        {
            this.closeForm(false);
        }

        // 상단 타이틀 드래그로 팝업 이동
        private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                Window window = Window.GetWindow(this);

                window?.DragMove();
            }
        }

        private void closeForm(bool toggle) 
        {
            Window window = Window.GetWindow(this);

            if (window != null)
            {
                // 취소 시 창 닫기 (DialogResult = false)

                window.DialogResult = toggle;
                window.Close();
            }
        }
    }
}
