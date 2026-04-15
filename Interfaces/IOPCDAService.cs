using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using OPCGatewayTool.Models;

namespace OPCGatewayTool.Interfaces
{
    /// <summary>
    /// OPC DA 服務介面，定義 OPC DA 用戶端連線、瀏覽、項目訂閱的契約。
    /// </summary>
    public interface IOPCDAService : IDisposable
    {
        // ----- 事件 -----
        event EventHandler<OPCDAItem> DataChanged;
        event EventHandler<bool> ConnectionStatusChanged;
        event EventHandler<string> LogMessage;

        // ----- 狀態屬性 -----
        ObservableCollection<OPCDAItem> Items { get; }
        bool IsConnected { get; }
        string ServerName { get; }
        string HostName { get; }
        int ReconnectIntervalSeconds { get; set; }

        // ----- 連線 -----
        Task<bool> ConnectAsync(string serverName, string hostName = "localhost", int timeoutSeconds = 10);
        void Disconnect();

        // ----- 瀏覽 -----
        List<string> GetAvailableServers(string hostName = "localhost");
        Task<List<string>> BrowseItemsAsync(string parentPath = "");
        List<string> BrowseItems(string parentPath = "");
        List<OPCTreeNode> BrowseItemsAsTree(string parentPath = "");
        void LoadChildNodes(OPCTreeNode parentNode);
        List<string> SearchItems(string searchTerm);

        // ----- 項目管理 -----
        bool AddItem(string itemId);
        bool RemoveItem(string itemId);
    }
}
