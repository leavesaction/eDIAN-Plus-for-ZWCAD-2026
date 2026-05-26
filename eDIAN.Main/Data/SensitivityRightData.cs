using System;

namespace eDIAN.Main.Data
{
    public class SensitivityRightData
    {
        public String DisplayText { get; set; } = String.Empty;
        public String RightName { get; set; } = String.Empty;
        public bool IsEnabled { get; set; } = true;
        public bool IsChecked { get; set; } = false;
        public SensitivityRightData(String displayText, String rightName, bool isEnabled, bool isChecked)
        {
            DisplayText = displayText;
            RightName = rightName;
            IsEnabled = isEnabled;
            IsChecked = isChecked;
        }
    }
}
