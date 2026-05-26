using eDIAN.Data;
using System.Windows;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;

namespace eDIAN.Main.UI
{
    public partial class ApplyPluginSettingForm : UserControl
    {
        public ApplyPluginSettingForm()
        {
            InitializeComponent();

            HeaderTitle.Text    = PluginApplication.global.getTitle("title.setting");
            PluginFullName.Text = CommonConstants.APPLICATION_NAME + " " + CommonConstants.APPLICATION_VERSION;
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

        // 닫기 버튼 클릭 시 폼 닫기
        public void onClickCloseForm(object sender, RoutedEventArgs e)
        {
            Window window = Window.GetWindow(this);

            if (window != null)
            {
                window.Close();
            }

        }
    }
}
