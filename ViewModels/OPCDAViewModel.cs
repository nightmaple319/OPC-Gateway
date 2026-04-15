using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using OPCGatewayTool.Interfaces;
using OPCGatewayTool.Models;
using NLog;

namespace OPCGatewayTool.ViewModels
{
    /// <summary>
    /// OPC DA 相關的子 ViewModel：連線、瀏覽、搜尋、即時數據。
    /// </summary>
    public class OPCDAViewModel : ObservableObject
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IOPCDAService _opcDaService;
        private readonly LogViewModel _log;

        private OPCDAConfig _config;
        private bool _isConnected;
        private string _searchTerm = "";

        public OPCDAViewModel(IOPCDAService opcDaService, LogViewModel log)
        {
            _opcDaService = opcDaService ?? throw new ArgumentNullException(nameof(opcDaService));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            AvailableServers = new ObservableCollection<string>();
            OPCDAItems = new ObservableCollection<OPCDAItem>();
            OPCTreeNodes = new ObservableCollection<OPCTreeNode>();
            RealTimeData = new ObservableCollection<OPCDAItem>();

            // 初始化命令
            ConnectCommand = new AsyncRelayCommand(ConnectAsync);
            DisconnectCommand = new RelayCommand(Disconnect);
            ScanServersCommand = new AsyncRelayCommand(ScanServersAsync);
            BrowseItemsCommand = new AsyncRelayCommand(BrowseItemsAsync);
            SearchItemsCommand = new AsyncRelayCommand(SearchItemsAsync);
            ClearSearchCommand = new RelayCommand(ClearSearch);

            // 訂閱服務事件
            _opcDaService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _opcDaService.DataChanged += OnDataChanged;
            _opcDaService.LogMessage += OnLogMessage;
        }

        #region Properties

        public OPCDAConfig Config
        {
            get => _config;
            set => SetProperty(ref _config, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(ConnectionStatus));
                }
            }
        }

        public string ConnectionStatus => IsConnected ? "已連接" : "未連接";

        public int ItemCount => OPCDAItems?.Count ?? 0;

        public string SearchTerm
        {
            get => _searchTerm;
            set => SetProperty(ref _searchTerm, value);
        }

        public ObservableCollection<string> AvailableServers { get; }
        public ObservableCollection<OPCDAItem> OPCDAItems { get; }
        public ObservableCollection<OPCTreeNode> OPCTreeNodes { get; }
        public ObservableCollection<OPCDAItem> RealTimeData { get; }

        #endregion

        #region Commands

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand ScanServersCommand { get; }
        public ICommand BrowseItemsCommand { get; }
        public ICommand SearchItemsCommand { get; }
        public ICommand ClearSearchCommand { get; }

        #endregion

        #region Public Methods

        /// <summary>
        /// 由 MainViewModel 載入配置時呼叫，更新 OPCDAConfig 參考並同步到服務。
        /// </summary>
        public void UpdateConfig(OPCDAConfig config)
        {
            Config = config;
        }

        /// <summary>
        /// 載入預設 Available Servers。
        /// </summary>
        public void LoadDefaultAvailableServers()
        {
            AvailableServers.Clear();
            AvailableServers.Add("Matrikon.OPC.Simulation.1");
            AvailableServers.Add("Matrikon.OPC.Simulation");
        }

        /// <summary>
        /// 由 UI Timer 定期呼叫，用於觸發計數屬性重新讀取。
        /// </summary>
        public void RefreshCounts()
        {
            OnPropertyChanged(nameof(ItemCount));
        }

        /// <summary>
        /// 由 MainWindow.xaml.cs 的 TreeViewItem_Expanded 呼叫。
        /// </summary>
        public void LoadChildNodes(OPCTreeNode node)
        {
            try
            {
                if (!IsConnected || node == null || node.IsLoaded || !node.HasChildren)
                    return;

                _opcDaService.LoadChildNodes(node);
                _log.AddMessage($"載入了節點 '{node.Name}' 的 {node.Children.Count} 個子項目");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"載入子節點時發生錯誤: {node?.FullPath}");
                _log.AddMessage($"載入子節點錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 取得樹狀視圖中被勾選的節點（供 MappingViewModel 使用）。
        /// </summary>
        public IEnumerable<OPCTreeNode> GetSelectedTreeItems()
        {
            return GetSelectedTreeItemsRecursive(OPCTreeNodes);
        }

        private IEnumerable<OPCTreeNode> GetSelectedTreeItemsRecursive(IEnumerable<OPCTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsSelected)
                {
                    yield return node;
                }

                foreach (var child in GetSelectedTreeItemsRecursive(node.Children))
                {
                    yield return child;
                }
            }
        }

        /// <summary>
        /// 取消訂閱所有服務事件（Dispose 時呼叫）。
        /// </summary>
        public void UnsubscribeAll()
        {
            _opcDaService.ConnectionStatusChanged -= OnConnectionStatusChanged;
            _opcDaService.DataChanged -= OnDataChanged;
            _opcDaService.LogMessage -= OnLogMessage;
        }

        #endregion

        #region Command Implementations

        private async Task ConnectAsync()
        {
            try
            {
                _log.AddMessage("正在連接 OPC DA 伺服器...");

                _opcDaService.ReconnectIntervalSeconds = Config.ReconnectIntervalSeconds;
                var success = await _opcDaService.ConnectAsync(
                    Config.ServerName,
                    Config.HostName,
                    Config.ConnectionTimeoutSeconds);

                if (success)
                {
                    OPCDAItems.Clear();
                    foreach (var item in _opcDaService.Items)
                    {
                        OPCDAItems.Add(item);
                    }
                    _log.AddMessage("OPC DA 伺服器連接成功");
                }
                else
                {
                    _log.AddMessage("OPC DA 伺服器連接失敗");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "連接 OPC DA 時發生錯誤");
                _log.AddMessage($"連接錯誤: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            try
            {
                _opcDaService.Disconnect();
                OPCDAItems.Clear();
                RealTimeData.Clear();
                _log.AddMessage("OPC DA 伺服器已斷開連接");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "斷開 OPC DA 連接時發生錯誤");
                _log.AddMessage($"斷開連接錯誤: {ex.Message}");
            }
        }

        private async Task ScanServersAsync()
        {
            try
            {
                _log.AddMessage("正在掃描 OPC DA 伺服器...");

                var servers = await Task.Run(() => _opcDaService.GetAvailableServers(Config.HostName));

                AvailableServers.Clear();
                foreach (var server in servers)
                {
                    AvailableServers.Add(server);
                }

                _log.AddMessage($"找到 {servers.Count} 個 OPC DA 伺服器");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "掃描 OPC DA 伺服器時發生錯誤");
                _log.AddMessage($"掃描錯誤: {ex.Message}");
            }
        }

        private async Task BrowseItemsAsync()
        {
            try
            {
                if (!IsConnected)
                {
                    _log.AddMessage("請先連接 OPC DA 伺服器");
                    return;
                }

                _log.AddMessage("正在瀏覽 OPC DA 項目...");

                var items = await _opcDaService.BrowseItemsAsync();

                OPCDAItems.Clear();
                foreach (var itemId in items.Take(50))
                {
                    OPCDAItems.Add(new OPCDAItem { ItemId = itemId });
                }

                if (OPCTreeNodes.Count == 0)
                {
                    try
                    {
                        var treeNodes = await Task.Run(() => _opcDaService.BrowseItemsAsTree());

                        foreach (var node in treeNodes.Take(20))
                        {
                            OPCTreeNodes.Add(node);
                        }

                        _log.AddMessage($"找到 {items.Count} 個項目，{treeNodes.Count} 個樹節點");
                    }
                    catch (Exception treeEx)
                    {
                        logger.Error(treeEx, "載入樹狀結構時發生錯誤");
                        _log.AddMessage($"樹狀結構載入失敗: {treeEx.Message}");
                        _log.AddMessage($"找到 {items.Count} 個項目（僅平面列表）");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "瀏覽 OPC DA 項目時發生錯誤");
                _log.AddMessage($"瀏覽錯誤: {ex.Message}");
            }
        }

        private async Task SearchItemsAsync()
        {
            try
            {
                if (!IsConnected)
                {
                    _log.AddMessage("請先連接 OPC DA 伺服器");
                    return;
                }

                if (string.IsNullOrWhiteSpace(SearchTerm))
                {
                    _log.AddMessage("請輸入搜尋關鍵字");
                    return;
                }

                _log.AddMessage($"正在搜尋包含 '{SearchTerm}' 的項目...");

                var searchResults = await Task.Run(() => _opcDaService.SearchItems(SearchTerm));

                OPCDAItems.Clear();
                foreach (var itemId in searchResults)
                {
                    OPCDAItems.Add(new OPCDAItem { ItemId = itemId });
                }

                _log.AddMessage($"找到 {searchResults.Count} 個符合條件的項目");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "搜尋項目時發生錯誤");
                _log.AddMessage($"搜尋錯誤: {ex.Message}");
            }
        }

        private void ClearSearch()
        {
            SearchTerm = "";
            _ = Task.Run(async () => await BrowseItemsAsync());
        }

        /// <summary>
        /// 讓 MappingViewModel 在添加項目後，同步到 OPC DA 服務。
        /// </summary>
        public bool AddItem(string itemId)
        {
            return _opcDaService.AddItem(itemId);
        }

        #endregion

        #region Event Handlers

        private void OnConnectionStatusChanged(object sender, bool isConnected)
        {
            IsConnected = isConnected;
        }

        private void OnDataChanged(object sender, OPCDAItem item)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                // 更新即時數據
                var existingItem = RealTimeData.FirstOrDefault(i => i.ItemId == item.ItemId);
                if (existingItem != null)
                {
                    existingItem.Value = item.Value;
                    existingItem.Quality = item.Quality;
                    existingItem.Timestamp = item.Timestamp;
                    existingItem.DataType = item.DataType;
                }
                else
                {
                    RealTimeData.Add(new OPCDAItem
                    {
                        ItemId = item.ItemId,
                        Value = item.Value,
                        Quality = item.Quality,
                        Timestamp = item.Timestamp,
                        DataType = item.DataType
                    });
                }

                // 同步更新 OPCDAItems
                var opcItem = OPCDAItems.FirstOrDefault(i => i.ItemId == item.ItemId);
                if (opcItem != null)
                {
                    opcItem.Value = item.Value;
                    opcItem.Quality = item.Quality;
                    opcItem.Timestamp = item.Timestamp;
                    opcItem.DataType = item.DataType;
                }
            });
        }

        private void OnLogMessage(object sender, string message)
        {
            _log.AddMessage(message);
        }

        #endregion
    }
}
