using System;

namespace eDIAN.Main.Data
{
    /// <summary>
    /// 어셈블리 로딩시 필요한 상세 정보
    /// </summary>
    public class AssemblyData
    {
        public String Name { get; }              // 어셈블리명
        public String PublicKeyToken { get; }    // 토근 값
        public Version MinVersion { get; }       // 적용될 최소 버전

        public AssemblyData(String name, String publicKeyToken, Version minVersion)
        {
            this.Name = name;
            this.PublicKeyToken = publicKeyToken;
            this.MinVersion = minVersion;
        }
    }
}
