
using eDIAN.Data;
using System;

namespace eDIAN.Main.Data
{
    public class ServiceRequestData
    {
        public ServiceRequestData() { }
        public String userId { get; set; } = String.Empty;
        public String licType { get; set; } = String.Empty;
        public String tenantId { get; set; } = String.Empty;
        public String clientId { get; set; } = String.Empty;
        public String appVersion { get; set; } = String.Empty;
        public String appType { get; set; } = "CAD";
        public String programVersion { get; set; } = String.Empty;
        public String pcIp { get; set; } = String.Empty;
        public String macAddr { get; set; } = String.Empty;
        public String connectMethod { get; set; } = String.Empty;
        public String contentId { get; set; } = String.Empty;
        public String filePath { get; set; } = String.Empty;
        public String command { get; set; } = String.Empty;
        public String errorOccurrenceYn { get; set; } = String.Empty;
        public String errorCode { get; set; } = String.Empty;
        public String errorYn { get; set; } = String.Empty;
    }
}
