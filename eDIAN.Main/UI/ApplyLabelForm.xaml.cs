using eDIAN.Core;
using eDIAN.Data;
using eDIAN.Main.API;
using eDIAN.Main.Core;
using eDIAN.Main.Data;
using log4net;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace eDIAN.Main.UI
{
    /// <summary>
    /// 사용자 권한 설정 폼
    /// </summary>
    public partial class ApplyLabelForm : UserControl
    {
        private static readonly ILog logger = PluginLogger.getLogger("ApplyLabelForm", "application.log");

        private ProtectionController protectController;

        private DocumentListItem item;

        // ListBox에 바인딩 할 적용 대상자 목록 생성
        private List<UserLicenseItem> userLicenseItems;

        // 레이블 선택 콤보 박스 옵션 목록
        private List<SensitivityLabelsOption> sensitivityLabelOptions = null;

        public List<String> hourList;

        public List<String> minuteList;

        // 숨겨질 때 줄일 높이 저장 값
        private double customLabelFormLastHeight = 0.0;

        private bool isMeasurecustomLabelHeight = false;

        private JObject resultData;

        // 적용 처리 결과 반환
        public JObject applyResult
        {
            get => this.resultData;
        }

        public ApplyLabelForm(ProtectionController protectController, DocumentListItem item)
        {
            this.protectController = protectController;

            this.item = item;

            this.userLicenseItems = new List<UserLicenseItem>();

            this.hourList = new List<String>();

            this.minuteList = new List<String>();

            // UI 초기화
            this.InitializeComponent();
        }

        private void loadForm(object sender, RoutedEventArgs e)
        {
            _ = this.loadFormAsync();
        }

        private async Task loadFormAsync()
        {
            try
            {
            TitleTextBlock.Text = PluginApplication.global.getTitle("apply.label");
            RightTextBlock.Text = PluginApplication.global.getTitle("right");

            ViewCheckBox.Content = PluginApplication.global.getTitle("view");
            ExtractCheckBox.Content = PluginApplication.global.getTitle("copy");
            PrintCheckBox.Content = PluginApplication.global.getTitle("print");
            EditCheckBox.Content = PluginApplication.global.getTitle("save");

            SeekUserTextBox.ToolTip = PluginApplication.global.getMessage("seek.user.info");

            IsApplyLimitDateTime.Content = PluginApplication.global.getTitle("apply.expire.datetime");

            if (isMeasurecustomLabelHeight) 
            {
                return;
            }
           
            // 처음 로드 될때 CustomLabelForm의 높이를 구한다.

            this.customLabelFormLastHeight = CustomLabelForm.ActualHeight;

            isMeasurecustomLabelHeight = true;

            // 시간 선택 콤보박스에 사용할 시간과 분 리스트 초기화

            for (int i = 0; i < 24; i++)
            {
                hourList.Add(i.ToString("D2"));
            }

            for (int i = 0; i < 60; i++)
            {
                minuteList.Add(i.ToString("D2"));
            }


            await this.initializeFormAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                logger.Error("loadFormAsync", ex);
            }
        }


        /// <summary>
        /// 사용자 정의 레이블 폼 세팅
        /// </summary>
        private async Task initializeFormAsync()
        {
            await this.setComboBoxSensitivityLabelAsync().ConfigureAwait(true);

            // 문서에 부여된 사용자, 권한 정보 목록 조회

            List<String> rightList = this.item.source.appliedRightList;      // 적용 권한

            // 권한 체크박스 세팅
            if (rightList != null)
            {
                foreach (String right in rightList)
                {
                    if (right.CompareTo(ViewCheckBox.Tag) == 0)
                    {
                        ViewCheckBox.IsChecked = true;
                    }
                    if (right.CompareTo(PrintCheckBox.Tag) == 0)
                    {
                        PrintCheckBox.IsChecked = true;
                    }
                    if (right.CompareTo(ExtractCheckBox.Tag) == 0)
                    {
                        ExtractCheckBox.IsChecked = true;
                    }
                    if (right.CompareTo(EditCheckBox.Tag) == 0)
                    {
                        EditCheckBox.IsChecked = true;
                    }
                }
            }

            // 이전에 적용된 대상자 아이디 (userPrincipal)

            List<String> appliedUserList = this.item.source.appliedUserList;       

            foreach (UserLicenseData userLicenseData in PluginApplication.userLicenseDataList!)
            {
                bool isChecked = false;

                foreach (String userId in appliedUserList!)
                {
                    if (userId.CompareTo(userLicenseData.userId) == 0)
                    {
                        // 적용 대상자 아이디에 해당하는 체크박스를 check 상태로 세팅
                        isChecked = true;

                        break;
                    }
                }

                // 적용 대상자 추가
                this.userLicenseItems.Add(new UserLicenseItem(isChecked, userLicenseData.userId, userLicenseData.userName, userLicenseData.licenseType));
            }

            // 리스트 박스에 사용자 라이선스 정보 바인딩
            UserListBox.ItemsSource = this.userLicenseItems;

            // 날짜 Picker 및 시간, 분 콤보박스 세팅

            LimitHourComboBox.ItemsSource = this.hourList;
            LimitMinuteComboBox.ItemsSource = this.minuteList;

            String limitDateTime = this.item.source.expireDateTime;

            String limitDate    = "";
            String limitHour    = "00";
            String limitMinute  = "00";

            if (!String.IsNullOrEmpty(limitDateTime))
            {
                IsApplyLimitDateTime.IsChecked = true;

                LimitDateTimeForm.Visibility = Visibility.Visible;

                if (limitDateTime.Length >= 8)
                {
                    limitDate = limitDateTime.Substring(0, 4) + "-" + limitDateTime.Substring(4, 2) + "-" + limitDateTime.Substring(6, 2);
                }

                if (limitDateTime.Length == 14)
                {
                    limitHour = limitDateTime.Substring(8, 2);
                    limitMinute = limitDateTime.Substring(10, 2);
                }
            }
            else
            {
                IsApplyLimitDateTime.IsChecked = false;

                LimitDateTimeForm.Visibility = Visibility.Hidden;

                limitDate = "";
                limitHour = "00";
                limitMinute = "00";
            }

            LimitDatePicker.Text = limitDate;
            LimitHourComboBox.SelectedValue = limitHour;
            LimitMinuteComboBox.SelectedValue = limitMinute;
        }


        /// <summary>
        /// 민감도 레이블 콤보 박스 세팅
        /// </summary>
        private async Task setComboBoxSensitivityLabelAsync()
        {
            if (this.protectController == null || this.item.source == null)
            {
                return;
            }

            List<LabelData> managedLabelDataList = null;

            LabelData defaultLabelData = null;

            if (IApplicationService.instance != null)
            {
                managedLabelDataList = await IApplicationService.instance
                    .CallGetDefaultLabelListAsync()
                    .ConfigureAwait(true);

                if (managedLabelDataList != null)
                {
                    foreach (LabelData labelData in managedLabelDataList)
                    {
                        logger.Debug($" - labelData : {labelData.labelId}, {labelData.labelName}, {labelData.displayName}, {labelData.isDefaultLabel}");

                        if (labelData.isDefaultLabel)
                        {
                            // 기본 레이블을 콤보박스 선택 레이블로 지정

                            defaultLabelData = labelData;

                            break;
                        }
                    }
                }
            }

            String selectedLabelId = this.item.source.labelId;

            if (defaultLabelData != null)
            {
                selectedLabelId = defaultLabelData.labelId;      // 기본 레이블이 있는 경우, 문서에 설정된 레이블보다 기본 레이블을 우선으로 선택 상태로 지정
            }

            // 레이블 옵션 목록 생성

            this.sensitivityLabelOptions = this.protectController.createSensitivityLabelOptions(managedLabelDataList);

            // 레이블 선택 콤보 박스 바인딩

            ComboBoxSensitivityLabel.ItemsSource = sensitivityLabelOptions;
            ComboBoxSensitivityLabel.DisplayMemberPath = nameof(SensitivityLabelsOption.DisplayName);
            ComboBoxSensitivityLabel.SelectedValuePath = nameof(SensitivityLabelsOption.Value);

            // 문서에 이미 설정된 레이블이 있으면 선택 상태 동기화

            if (String.IsNullOrEmpty(selectedLabelId) == false)
            {
                ComboBoxSensitivityLabel.SelectedValue = selectedLabelId;
            }
            else
            {
                ComboBoxSensitivityLabel.SelectedIndex = 0;
            }

            // 기본 레이블이 없는 경우에만 레이블 선택 가능. 기본 레이블이 있는 경우에는 레이블을 변경할 수 없도록 비활성화 처리

            if (defaultLabelData == null)
            {
                ComboBoxSensitivityLabel.IsEnabled = true;
            }
            else
            {
                ComboBoxSensitivityLabel.IsEnabled = false;
            }
        }

        /// <summary>
        /// 레이블 콤보 변경 시 사용자 정의 레이블 폼 표시/숨김 이벤트
        /// </summary>
        private void onChangedSensitivityLabel(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is SensitivityLabelsOption addedOption)
            {
                this.visibleCustomLabelForm(addedOption.Value);
            }
        }

        /// <summary>
        /// 사용자 정의 레이블 폼 표시/숨김 처리
        /// </summary>
        /// <param name="labelId"></param>
        private void visibleCustomLabelForm(String labelId) 
        {
            if (this.protectController == null)
            {
                return;
            }

            Window window = Window.GetWindow(this);

            // 선택된 레이블에 Adhoc 보호가 설정되어 있는지 확인

            bool hasAdhocProtection = false;

            if (String.IsNullOrEmpty(labelId) == false)
            {
                hasAdhocProtection = this.protectController.hasAdhocProtectionInLabel(labelId);
            }

            logger.Debug($" - visibleCustomLabelForm : {labelId} {hasAdhocProtection}");

            if (hasAdhocProtection)
            {
                if (CustomLabelForm.Visibility != Visibility.Visible)
                {
                    CustomLabelForm.Visibility = Visibility.Visible;

                    if (window != null && this.customLabelFormLastHeight > 0)
                    {
                        window.Height += this.customLabelFormLastHeight;
                        // logger.Debug($" - visibleCustomLabelForm visibled : window.Height={window.Height}, customLabelFormLastHeight={this.customLabelFormLastHeight}");
                    }
                }
            }
            else
            {
                if (CustomLabelForm.Visibility == Visibility.Visible)
                {
                    // 이미 초기 측정값이 있음. 여기서는 다시 측정하지 않아도 됨.
                    CustomLabelForm.Visibility = Visibility.Collapsed;

                    if (window != null && this.customLabelFormLastHeight > 0)
                    {
                        window.Height = Math.Max(window.MinHeight, window.Height - this.customLabelFormLastHeight);
                        // logger.Debug($" - visibleCustomLabelForm hidden : window.Height={window.Height}, customLabelFormLastHeight={this.customLabelFormLastHeight}");
                    }
                }
            }
        }
        /// <summary>
        /// 사용자 목록내 사용자 검색 (사용자 이름 또는 사용자 아이디 포함 여부) 이벤트
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void onChangedSeekUserByText(object sender, TextChangedEventArgs e)
        {
            // 사용자 정의 레이블 입력값 변경 시 추가 처리 로직이 필요한 경우 여기에 구현

            List<UserLicenseItem> seekUserLicenseItems = new List<UserLicenseItem>();

            String inputText = "";

            if (e.Source is TextBox textBox)
            {
                inputText = textBox.Text;

                if (String.IsNullOrEmpty(inputText) == true)
                {
                    // 리스트 박스에 사용자 라이선스 정보 바인딩
                    UserListBox.ItemsSource = this.userLicenseItems;

                    return;
                }
            }

            // 1. 이전 레이블 적용 대상자를 무조건 포함

            List<String> appliedUserList = this.item.source.appliedUserList;

            foreach (UserLicenseItem userLicenseItem in this.userLicenseItems!)
            {
                if (userLicenseItem.isChecked == true)
                {
                    seekUserLicenseItems.Add(new UserLicenseItem(userLicenseItem.isChecked, userLicenseItem.userId, userLicenseItem.userName, userLicenseItem.licenseType));
                }
            }

            // 2. 입력된 텍스트가 userName 또는 userId 에 포함되어 있는지 확인하여 리스트에 추가 (중복 방지 포함)

            foreach (UserLicenseItem UserLicenseItem in this.userLicenseItems)
            {
                // 이미 적용된 사용자는 건너뜀 (중복 방지)

                if (seekUserLicenseItems.Any(item => item.userId == UserLicenseItem.userId))
                {
                    continue;
                }

                // 입력된 텍스트가 userName 또는 userId에 포함되어 있는지 확인

                bool checkUserName = !String.IsNullOrEmpty(UserLicenseItem.userName) && UserLicenseItem.userName.Contains(inputText ?? "", StringComparison.OrdinalIgnoreCase);
                bool checkUserId   = !String.IsNullOrEmpty(UserLicenseItem.userId) && UserLicenseItem.userId.Contains(inputText ?? "", StringComparison.OrdinalIgnoreCase);

                if (checkUserName == true || checkUserId == true)
                {
                    // 입력값이 사용자 이름이나 사용자 아이디에 포함되어 있으면 리스트에 추가

                    seekUserLicenseItems.Add(new UserLicenseItem(UserLicenseItem.isChecked, UserLicenseItem.userId, UserLicenseItem.userName, UserLicenseItem.licenseType));
                }
            }

            UserListBox.ItemsSource = seekUserLicenseItems;
        }

        private void onChangedLimitDateTime(object sender, RoutedEventArgs e)
        {
            // 사용자 정의 레이블 검색 텍스트 초기화
            SeekUserTextBox.Text = "";
        }


        /// <summary>
        /// 적용 버튼 클릭 이벤트
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>        
        private void onClickApplyButton(object sender, RoutedEventArgs e)
        {
            _ = this.onClickApplyButtonAsync();
        }

        private async Task onClickApplyButtonAsync()
        {
            try
            {
            if (this.protectController == null || this.item.source == null)
            {
                return;
            }

            this.item.source.labelId = ComboBoxSensitivityLabel.SelectedValue as String ?? "";

            // 선택된 레이블 ID를 가져옴
            if (String.IsNullOrEmpty(this.item.source.labelId))
            {
                MessageHandler.Show("label.id.not.set"); // 레이블 ID가 설정되지 않았습니다.
                return;
            }

            if (String.IsNullOrEmpty(this.item.source.filePath))
            {
                MessageHandler.Show("file.name.not.set");        // 파일 이름이 설정되지 않았습니다.
                return;
            }

            List<String> userList  = null;
            List<String> rightList = null;

            DateTime? expireDateTime = null;

            // 선택한 레이블이 Adhoc Protection 을 포함할 경우에 사용자 정의 레이블링

            if (this.protectController.hasAdhocProtectionInLabel(this.item.source.labelId))
            {
                // 권한 부여 대상자 목록 생성

                userList = new List<String>();

                if (UserListBox.ItemsSource != null)
                {
                    foreach (UserLicenseItem userItem in UserListBox.ItemsSource!)
                    {
                        logger.Debug($" - userItem : {userItem.userId}, {userItem.userName}, {userItem.isChecked}");

                        if (userItem.isChecked)
                        {
                            userList.Add(userItem.userId ?? "");
                        }
                    }
                }

                logger.Debug(userList.ToString());

                // 권한 목록 생성

                rightList = new List<String>();

                if (ViewCheckBox.IsChecked == true) rightList.Add("VIEW");
                if (PrintCheckBox.IsChecked == true) rightList.Add("PRINT");
                if (ExtractCheckBox.IsChecked == true) rightList.Add("EXTRACT");
                if (EditCheckBox.IsChecked == true) rightList.Add("EDIT");

                // 만료 일시 DateTime 생성

                if(IsApplyLimitDateTime.IsChecked == true)
                {
                    String expireDateTimeStr = LimitDatePicker.Text;

                    if (String.IsNullOrEmpty(expireDateTimeStr) == false)
                    {
                        // 날짜, 시간, 분, 초(00초로 고정)
                        expireDateTimeStr = expireDateTimeStr.Replace("-", "");
                        expireDateTimeStr += LimitHourComboBox.SelectedValue != null ? LimitHourComboBox.SelectedValue : "00";
                        expireDateTimeStr += LimitMinuteComboBox.SelectedValue != null ? LimitMinuteComboBox.SelectedValue : "00";
                        expireDateTimeStr += "00";

                        logger.Debug($" >>>>> onClickApplyButton expireDateTimeStr : {expireDateTimeStr}");

                        // ParseExact 사용으로 명확한 형식 지정

                        if (DateTime.TryParseExact(expireDateTimeStr, "yyyyMMddHHmmss", 
                            System.Globalization.CultureInfo.InvariantCulture, 
                            System.Globalization.DateTimeStyles.AssumeLocal, 
                            out DateTime parsedDateTime))
                        {
                            // 현재 일시와 비교해서 이전 일시인 경우 
                            if (parsedDateTime <= DateTime.Now)
                            {
                                MessageHandler.Show("invalid.datetime.value"); // 만료 일시는 현재 시간 이후여야 합니다.

                                return;
                            }

                            expireDateTime = parsedDateTime;
                        }
                        else
                        {
                            MessageHandler.Show("invalid.datetime.format"); // 잘못된 날짜 형식입니다.
                            return;
                        }
                    }
                }

                // 유효성 검사

                if (this.validateInput(userList, rightList) == false)
                {
                    return;
                }
            }

            // 대상 파일에 레이블 적용 처리

            this.resultData = await this.protectController.applyProtectionToFile(item, userList, rightList, expireDateTime).ConfigureAwait(true);

            this.closeForm(true);
            }
            catch (Exception ex)
            {
                logger.Error("onClickApplyButtonAsync", ex);
            }
        }

        /// <summary>
        /// 만료 일시 입력 폼 토글
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void onClickLimitDateTimeCheckBox (object sender, RoutedEventArgs e)
        {
            if (IsApplyLimitDateTime.IsChecked == true)
            {
                LimitDateTimeForm.Visibility = Visibility.Visible;
            }
            else
            {
                LimitDateTimeForm.Visibility = Visibility.Collapsed;
            }
        }


        private bool validateInput(List<String> userList, List<String> rightList)
        {
            if (this.item.source == null)
            {
                MessageHandler.Show("document.info.not.set");       // 문서 정보가 설정되지 않았습니다.

                return false;
            }

            if (userList != null && userList.Count < 1)
            {
                MessageHandler.Show("user.list.not.set"); // 권한 부여 대상자가 선택되지 않았습니다.
                return false;
            }

            if (rightList != null && rightList.Count < 1)
            {
                MessageHandler.Show("right.list.not.set"); // 부여할 권한이 선택되지 않았습니다.
                return false;
            }

            if (IsApplyLimitDateTime.IsChecked == true && LimitDatePicker.Text == null)
            {
                MessageHandler.Show("enter.expire.datetimet"); // 만료 일자를 입력하세요.
                return false;
            }


            return true;
        }

        /// <summary>
        /// 취소 버튼 클릭 이벤트
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void onClickCancelBotton(object sender, RoutedEventArgs e)
        {
            this.closeForm(false);
        }

        /// <summary>
        /// 상단 타이틀 드래그로 팝업 이동
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void onMouseLeftButtonDownTitleBar(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                Window window = Window.GetWindow(this);

                window?.DragMove();
            }
        }

        /// <summary>
        /// 창 닫기 및 결과 설정
        /// </summary>
        /// <param name="dialogResult">확인(true) 또는 취소(false)</param>
        private void closeForm(bool dialogResult)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    Window window = Window.GetWindow(this);
                    if (window != null)
                    {
                        // 취소 시 결과 데이터 초기화
                        if (dialogResult == false)
                        {
                            this.resultData = null;
                        }

                        // 모달 상태 확인 후 DialogResult 설정 (비모달일 경우 예외 방지)
                        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal)
                        {
                            window.DialogResult = dialogResult;
                        }

                        window.Close();
                    }
                }
                catch (Exception ex)
                {
                    logger.Error("closeForm Exception", ex);

                    // 예외 발생 시에도 창은 닫도록 시도
                    try
                    {
                        Window.GetWindow(this)?.Close();
                    }
                    catch (Exception)
                    {
                        logger.Debug("closeForm fallback Close failed.");
                    }
                }
            }));
        }
    }

    public class UserLicenseItem 
    {
        public bool isChecked { get; set; } = false;

        public String licenseType = String.Empty;

        public BitmapImage IconLicenseType { get; set; } = null;
        
        public String userName { get; set; } = String.Empty;
        
        public String userId { get; set; } = String.Empty;

        public UserLicenseItem(bool isChecked, String userId, String userName, String licenseType) 
        {
            this.isChecked = isChecked;
            this.userId = userId;
            this.userName = userName;

            this.licenseType = licenseType;

            if (licenseType != null && licenseType.Equals("F"))
            {
                IconLicenseType = PluginFormManager.ToBitmapImage(Properties.Resources.icon_popup_license_premium);
            }
            else if (licenseType != null && licenseType.Equals("D"))
            {
                IconLicenseType = PluginFormManager.ToBitmapImage(Properties.Resources.icon_popup_license_basic);
            }
            else
            {
                IconLicenseType = PluginFormManager.ToBitmapImage(Properties.Resources.icon_popup_license_viewer);
            }
        }
    }
}