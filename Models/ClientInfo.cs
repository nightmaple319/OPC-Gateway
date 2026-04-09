using System;

namespace OPCGatewayTool.Models
{
    public class ClientInfo
    {
        public string SessionId { get; set; }
        public string ClientName { get; set; }
        public string EndpointUrl { get; set; }
        public DateTime ConnectedTime { get; set; }
        public string UserIdentity { get; set; }
        public int SubscriptionCount { get; set; }
        public TimeSpan SessionTimeout { get; set; }
        
        public string DisplayName => $"{ClientName} ({SessionId})";
        public string ConnectedTimeString => ConnectedTime.ToString("yyyy-MM-dd HH:mm:ss");
        public string SessionTimeoutString => $"{SessionTimeout.TotalMinutes:F0} 分鐘";
    }
}