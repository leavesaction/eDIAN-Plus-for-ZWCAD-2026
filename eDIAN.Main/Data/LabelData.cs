using System;

namespace eDIAN.Main.Data
{
    public class LabelData
    {
        public String targetType { get; set; } = String.Empty;
        public String targetId { get; set; } = String.Empty;
        public String labelId { get; set; } = String.Empty;
        public String labelName { get; set; } = String.Empty;
        public String displayName { get; set; } = String.Empty;
        public String sequence { get; set; } = String.Empty;
        public bool isDefaultLabel { get; set; } = false;
    }
}
