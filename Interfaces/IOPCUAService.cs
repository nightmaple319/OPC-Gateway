using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using OPCGatewayTool.Models;

namespace OPCGatewayTool.Interfaces
{
    /// <summary>
    /// OPC UA 服務介面，定義 OPC UA 伺服器啟停、節點管理、客戶端監控的契約。
    /// </summary>
    public interface IOPCUAService : IDisposable
    {
        // ----- 事件 -----
        event EventHandler<bool> ServerStatusChanged;
        event EventHandler<int> ClientCountChanged;
        event EventHandler<string> LogMessage;
        event EventHandler<ClientInfo> ClientConnected;
        event EventHandler<string> ClientDisconnected;

        // ----- 狀態屬性 -----
        ObservableCollection<OPCUANode> Nodes { get; }
        ObservableCollection<ClientInfo> ConnectedClients { get; }
        bool IsRunning { get; }
        int ConnectedClientCount { get; }
        string EndpointUrl { get; }

        // ----- 伺服器控制 -----
        Task<bool> StartAsync(OPCUAConfig config);
        void Stop();

        // ----- 節點管理 -----
        bool AddNode(string itemId, string browseName, object value, Type dataType);
        bool RemoveNode(string itemId);
        bool UpdateNodeValue(string itemId, object value, DateTime timestamp);
    }
}
