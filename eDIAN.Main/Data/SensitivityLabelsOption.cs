using System;

namespace eDIAN.Main.Data
{
    public class SensitivityLabelsOption
    {
        public String DisplayName { get; set; }
        public String Value { get; set; }
        public String sequence { get; set; } = String.Empty;
        public bool isDefaultLabel { get; set; } = false;

        public SensitivityLabelsOption(String displayName, String value, String sequence = null, bool isDefaultLabel = false)
        {
            this.DisplayName = displayName;
            this.Value = value;
            this.sequence = sequence;
            this.isDefaultLabel = isDefaultLabel;
        }
    }
}
