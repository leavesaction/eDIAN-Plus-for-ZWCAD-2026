using System;

namespace eDIAN.Main.Data
{
    public class UserLicenseData
    {
        public String userName { get; set; } = String.Empty;

        public String userId { get; set; } = String.Empty;

        public String licenseType { get; set; } = String.Empty;  // 라이선스 구분 코드

        public String licenseTypeName { get; set; } = String.Empty;  // 라이선스 구분 코드명

        public bool hasLicense { get; set; } = false;

        public bool isOnlyView { get; set; } = true;
    }
}
