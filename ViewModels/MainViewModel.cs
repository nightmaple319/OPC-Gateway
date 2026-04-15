using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using OPCGatewayTool.Interfaces;
using OPCGatewayTool.Models;
using OPCGatewayTool.Services;
using NLog;
using Newtonsoft.Json;
using System.IO;
using Microsoft.Win32;

namespace OPCGatewayTool.ViewModels
{
    public class MainViewModel : ObservableObject, IDisposable
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IOPCDAService _opcDaService;
        private readonly IOPCUAService _opcUaService;
        private readonly DataMappingService _dataMappingService;
        private readonly DispatcherTimer _uiUpdateTimer;
        private GatewayConfig _config;
        
        private bool _isOPCDAConnected;
        private bool _isOPCUARunning;
        private bool _isMappingEnabled;
        private readonly StringBuilder _logBuilder = new StringBuilder();
        private string _logMessages = "";
        private DateTime _currentTime = DateTime.Now;
        private bool _disposed;
        private string _searchTerm = "";

        public MainViewModel()
        {
            try
            {
                // 初始化服務
                _opcDaService = new TitaniumOPCDAService();
                _opcUaService = new SimpleRealOPCUAService();
                _dataMappingService = new DataMappingService(_opcDaService, _opcUaService);
                
                logger.Info("所有服務初始化完成");
                
                // 初始化配置
                _config = new GatewayConfig();
                
                // 初始化集合
                AvailableOPCDAServers = new ObservableCollection<string>();
                OPCDAItems = new ObservableCollection<OPCDAItem>();
                OPCTreeNodes = new ObservableCollection<OPCTreeNode>();
                ItemMappings = new ObservableCollection<ItemMapping>();
                RealTimeData = new ObservableCollection<OPCDAItem>();
                
                // 初始化命令
                InitializeCommands();
                
                // 訂閱事件
                SubscribeToEvents();
                
                // 啟動UI更新計時器
                _uiUpdateTimer = new DispatcherTimer();
                _uiUpdateTimer.Interval = TimeSpan.FromSeconds(1);
                _uiUpdateTimer.Tick += UpdateUI;
                _uiUpdateTimer.Start();
                
                // 預設配置
                LoadDefaultConfiguration();
                
                logger.Info("MainViewModel 初始化完成");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "MainViewModel 初始化失敗");
                throw;
            }
        }

        #region Properties

        public GatewayConfig Config
        {
            get => _config;
            set => SetProperty(ref _config, value);
        }

        public OPCDAConfig OPCDAConfig => _config.OPCDAConfig;
        public OPCUAConfig OPCUAConfig => _config.OPCUAConfig;

        public bool IsOPCDAConnected
        {
            get => _isOPCDAConnected;
            private set
            {
                if (SetProperty(ref _isOPCDAConnected, value))
                {
                    OnPropertyChanged(nameof(OPCDAConnectionStatus));
                }
            }
        }

        public bool IsOPCUARunning
        {
            get => _isOPCUARunning;
            private set
            {
                if (SetProperty(ref _isOPCUARunning, value))
                {
                    OnPropertyChanged(nameof(OPCUAServerStatus));
                    OnPropertyChanged(nameof(OPCUAEndpointUrl));
                }
            }
        }

        public bool IsMappingEnabled
        {
            get => _isMappingEnabled;
            set
            {
                if (SetProperty(ref _isMappingEnabled, value))
                {
                    _dataMappingService.IsEnabled = value;
                }
            }
        }

        public string LogMessages
        {
            get => _logMessages;
            set => SetProperty(ref _logMessages, value);
        }

        public DateTime CurrentTime
        {
            get => _currentTime;
            set => SetProperty(ref _currentTime, value);
        }

        public string OPCDAConnectionStatus => IsOPCDAConnected ? "已連接" : "未連接";
        public string OPCUAServerStatus => IsOPCUARunning ? "運行中" : "已停止";
        public string OPCUAEndpointUrl => IsOPCUARunning ? (_opcUaService?.EndpointUrl ?? OPCUAConfig.EndpointUrl) : "未啟動";
        
        public int OPCDAItemCount => OPCDAItems?.Count ?? 0;
        public int OPCUANodeCount => _opcUaService?.Nodes?.Count ?? 0;
        public int ActiveMappingCount => ItemMappings?.Count(m => m.IsEnabled) ?? 0;
        public int ConnectedClientCount => _opcUaService?.ConnectedClientCount ?? 0;

        public ObservableCollection<string> AvailableOPCDAServers { get; }
        public ObservableCollection<OPCDAItem> OPCDAItems { get; }
        public ObservableCollection<OPCTreeNode> OPCTreeNodes { get; }
        public ObservableCollection<ItemMapping> ItemMappings { get; }
        public ObservableCollection<OPCDAItem> RealTimeData { get; }
        public ObservableCollection<Models.ClientInfo> ConnectedClients => _opcUaService?.ConnectedClients;
        
        public string SearchTerm
        {
            get => _searchTerm;
            set => SetProperty(ref _searchTerm, value);
        }

        #endregion

        #region Commands

        public ICommand ConnectOPCDACommand { get; private set; }
        public ICommand DisconnectOPCDACommand { get; private set; }
        public ICommand StartOPCUACommand { get; private set; }
        public ICommand StopOPCUACommand { get; private set; }
        public ICommand ScanOPCDAServersCommand { get; private set; }
        public ICommand BrowseOPCDAItemsCommand { get; private set; }
        public ICommand AddSelectedItemsCommand { get; private set; }
        public ICommand ClearMappingsCommand { get; private set; }
        public ICommand LoadConfigCommand { get; private set; }
        public ICommand SaveConfigCommand { get; private set; }
        public ICommand ClearLogCommand { get; private set; }
        public ICommand SearchItemsCommand { get; private set; }
        public ICommand ClearSearchCommand { get; private set; }

        #endregion

        #region Private Methods

        private void InitializeCommands()
        {
            ConnectOPCDACommand = new AsyncRelayCommand(ConnectOPCDAAsync);
            DisconnectOPCDACommand = new RelayCommand(DisconnectOPCDA);
            StartOPCUACommand = new AsyncRelayCommand(StartOPCUAAsync);
            StopOPCUACommand = new RelayCommand(StopOPCUA);
            ScanOPCDAServersCommand = new AsyncRelayCommand(ScanOPCDAServersAsync);
            BrowseOPCDAItemsCommand = new AsyncRelayCommand(BrowseOPCDAItemsAsync);
            AddSelectedItemsCommand = new RelayCommand(AddSelectedItems);
            ClearMappingsCommand = new RelayCommand(ClearMappings);
            LoadConfigCommand = new RelayCommand(LoadConfig);
            SaveConfigCommand = new RelayCommand(SaveConfig);
            ClearLogCommand = new RelayCommand(ClearLog);
            SearchItemsCommand = new AsyncRelayCommand(SearchItemsAsync);
            ClearSearchCommand = new RelayCommand(ClearSearch);
        }

        private void SubscribeToEvents()
        {
            // OPC DA 事件
            _opcDaService.ConnectionStatusChanged += OnOPCDAConnectionStatusChanged;
            _opcDaService.DataChanged += OnOPCDADataChanged;
            _opcDaService.LogMessage += OnLogMessage;

            // OPC UA 事件
            _opcUaService.ServerStatusChanged += OnOPCUAServerStatusChanged;
            _opcUaService.ClientCountChanged += OnOPCUAClientCountChanged;
            _opcUaService.ClientConnected += OnOPCUAClientConnected;
            _opcUaService.ClientDisconnected += OnOPCUAClientDisconnected;
            _opcUaService.LogMessage += OnLogMessage;

            // 數據映射事件
            _dataMappingService.MappingAdded += OnMappingAdded;
            _dataMappingService.MappingRemoved += OnMappingRemoved;
            _dataMappingService.DataMapped += OnDataMapped;
            _dataMappingService.LogMessage += OnLogMessage;
            _dataMappingService.MappingSetupFailed += OnMappingSetupFailed;
        }

        private void LoadDefaultConfiguration()
        {
            // 載入預設的OPC DA伺服器
            AvailableOPCDAServers.Add("Matrikon.OPC.Simulation.1");
            AvailableOPCDAServers.Add("Matrikon.OPC.Simulation");
            
            OPCDAConfig.ServerName = "Matrikon.OPC.Simulation.1";
            OPCDAConfig.HostName = "localhost";
            OPCDAConfig.UpdateRate = 1000;
            
            IsMappingEnabled = true;
        }

        private async Task ConnectOPCDAAsync()
        {
            try
            {
                AddLogMessage("正在連接 OPC DA 伺服器...");
                
                _opcDaService.ReconnectIntervalSeconds = OPCDAConfig.ReconnectIntervalSeconds;
                var success = await _opcDaService.ConnectAsync(
                    OPCDAConfig.ServerName,
                    OPCDAConfig.HostName,
                    OPCDAConfig.ConnectionTimeoutSeconds);
                
                if (success)
                {
                    // 同步項目到UI
                    OPCDAItems.Clear();
                    foreach (var item in _opcDaService.Items)
                    {
                        OPCDAItems.Add(item);
                    }
                    
                    AddLogMessage("OPC DA 伺服器連接成功");
                }
                else
                {
                    AddLogMessage("OPC DA 伺服器連接失敗");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "連接 OPC DA 時發生錯誤");
                AddLogMessage($"連接錯誤: {ex.Message}");
            }
        }

        private void DisconnectOPCDA()
        {
            try
            {
                _opcDaService.Disconnect();
                OPCDAItems.Clear();
                RealTimeData.Clear();
                AddLogMessage("OPC DA 伺服器已斷開連接");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "斷開 OPC DA 連接時發生錯誤");
                AddLogMessage($"斷開連接錯誤: {ex.Message}");
            }
        }

        private async Task StartOPCUAAsync()
        {
            try
            {
                AddLogMessage("正在啟動 OPC UA 伺服器...");
                
                var success = await _opcUaService.StartAsync(OPCUAConfig);
                
                if (success)
                {
                    // 設置現有映射
                    await _dataMappingService.SetupAllMappingsAsync();
                    AddLogMessage("OPC UA 伺服器啟動成功");
                }
                else
                {
                    AddLogMessage("OPC UA 伺服器啟動失敗");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "啟動 OPC UA 時發生錯誤");
                AddLogMessage($"啟動錯誤: {ex.Message}");
            }
        }

        private void StopOPCUA()
        {
            try
            {
                _opcUaService.Stop();
                ConnectedClients.Clear();
                AddLogMessage("OPC UA 伺服器已停止");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "停止 OPC UA 時發生錯誤");
                AddLogMessage($"停止錯誤: {ex.Message}");
            }
        }

        private async Task ScanOPCDAServersAsync()
        {
            try
            {
                AddLogMessage("正在掃描 OPC DA 伺服器...");
                
                var servers = await Task.Run(() => _opcDaService.GetAvailableServers(OPCDAConfig.HostName));
                
                AvailableOPCDAServers.Clear();
                foreach (var server in servers)
                {
                    AvailableOPCDAServers.Add(server);
                }
                
                AddLogMessage($"找到 {servers.Count} 個 OPC DA 伺服器");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "掃描 OPC DA 伺服器時發生錯誤");
                AddLogMessage($"掃描錯誤: {ex.Message}");
            }
        }

        private async Task BrowseOPCDAItemsAsync()
        {
            try
            {
                if (!IsOPCDAConnected)
                {
                    AddLogMessage("請先連接 OPC DA 伺服器");
                    return;
                }

                AddLogMessage("正在瀏覽 OPC DA 項目...");
                
                // 暫時只載入平面列表，避免樹狀結構的潛在問題
                var items = await _opcDaService.BrowseItemsAsync();
                
                OPCDAItems.Clear();
                foreach (var itemId in items.Take(50))
                {
                    OPCDAItems.Add(new OPCDAItem { ItemId = itemId });
                }
                
                // 只有在樹狀結構為空時才載入
                if (OPCTreeNodes.Count == 0)
                {
                    try
                    {
                        var treeNodes = await Task.Run(() => _opcDaService.BrowseItemsAsTree());
                        
                        foreach (var node in treeNodes.Take(20)) // 限制節點數量
                        {
                            OPCTreeNodes.Add(node);
                        }
                        
                        AddLogMessage($"找到 {items.Count} 個項目，{treeNodes.Count} 個樹節點");
                    }
                    catch (Exception treeEx)
                    {
                        logger.Error(treeEx, "載入樹狀結構時發生錯誤");
                        AddLogMessage($"樹狀結構載入失敗: {treeEx.Message}");
                        AddLogMessage($"找到 {items.Count} 個項目（僅平面列表）");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "瀏覽 OPC DA 項目時發生錯誤");
                AddLogMessage($"瀏覽錯誤: {ex.Message}");
            }
        }

        private void AddSelectedItems()
        {
            try
            {
                // 從樹狀視圖中獲取選中的項目
                var selectedTreeItems = GetSelectedTreeItems(OPCTreeNodes).ToList();
                
                // 也檢查平面列表中選中的項目
                var selectedListItems = OPCDAItems.Where(i => i.IsSelected).ToList();
                
                if (!selectedTreeItems.Any() && !selectedListItems.Any())
                {
                    AddLogMessage("請選擇要添加的項目");
                    return;
                }

                var allSelectedItemIds = new HashSet<string>();
                
                // 收集樹狀視圖選中的項目ID
                foreach (var treeItem in selectedTreeItems)
                {
                    if (!treeItem.IsFolder) // 只添加實際的數據項目，不添加資料夾
                    {
                        allSelectedItemIds.Add(treeItem.FullPath);
                    }
                }
                
                // 收集平面列表選中的項目ID
                foreach (var listItem in selectedListItems)
                {
                    allSelectedItemIds.Add(listItem.ItemId);
                }

                if (!allSelectedItemIds.Any())
                {
                    AddLogMessage("請選擇數據項目（不能只選擇資料夾）");
                    return;
                }

                int successCount = 0;
                foreach (var itemId in allSelectedItemIds)
                {
                    // 添加到OPC DA服務
                    if (_opcDaService.AddItem(itemId))
                    {
                        // 創建映射
                        var browseName = itemId.Replace(".", "_");
                        _dataMappingService.AddMapping(itemId, browseName);
                        successCount++;
                        AddLogMessage($"成功添加項目: {itemId}");
                    }
                    else
                    {
                        AddLogMessage($"添加項目失敗: {itemId}");
                    }
                }

                // 同步映射到UI
                SyncMappingsToUI();
                
                AddLogMessage($"成功添加 {successCount}/{allSelectedItemIds.Count} 個項目");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "添加選中項目時發生錯誤");
                AddLogMessage($"添加項目錯誤: {ex.Message}");
            }
        }
        
        private IEnumerable<OPCTreeNode> GetSelectedTreeItems(IEnumerable<OPCTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsSelected)
                {
                    yield return node;
                }
                
                // 遞歸檢查子節點
                foreach (var childNode in GetSelectedTreeItems(node.Children))
                {
                    yield return childNode;
                }
            }
        }

        private void ClearMappings()
        {
            try
            {
                _dataMappingService.ClearAllMappings();
                ItemMappings.Clear();
                AddLogMessage("已清除所有映射");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "清除映射時發生錯誤");
                AddLogMessage($"清除映射錯誤: {ex.Message}");
            }
        }

        private void LoadConfig()
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    Title = "載入配置文件"
                };

                if (dialog.ShowDialog() == true)
                {
                    var json = File.ReadAllText(dialog.FileName);
                    var config = JsonConvert.DeserializeObject<GatewayConfig>(json);
                    
                    Config = config;
                    _dataMappingService.LoadMappingsFromConfig(config.ItemMappings);
                    SyncMappingsToUI();
                    
                    AddLogMessage($"配置已載入: {dialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "載入配置時發生錯誤");
                AddLogMessage($"載入配置錯誤: {ex.Message}");
            }
        }

        private void SaveConfig()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    Title = "保存配置文件",
                    FileName = "gateway_config.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    Config.ItemMappings = ItemMappings.ToList();
                    
                    var json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                    File.WriteAllText(dialog.FileName, json);
                    
                    AddLogMessage($"配置已保存: {dialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "保存配置時發生錯誤");
                AddLogMessage($"保存配置錯誤: {ex.Message}");
            }
        }

        private void ClearLog()
        {
            lock (_logBuilder)
            {
                _logBuilder.Clear();
            }
            LogMessages = "";
        }
        
        private async Task SearchItemsAsync()
        {
            try
            {
                if (!IsOPCDAConnected)
                {
                    AddLogMessage("請先連接 OPC DA 伺服器");
                    return;
                }
                
                if (string.IsNullOrWhiteSpace(SearchTerm))
                {
                    AddLogMessage("請輸入搜尋關鍵字");
                    return;
                }
                
                AddLogMessage($"正在搜尋包含 '{SearchTerm}' 的項目...");
                
                var searchResults = await Task.Run(() => _opcDaService.SearchItems(SearchTerm));
                
                OPCDAItems.Clear();
                foreach (var itemId in searchResults)
                {
                    OPCDAItems.Add(new OPCDAItem { ItemId = itemId });
                }
                
                AddLogMessage($"找到 {searchResults.Count} 個符合條件的項目");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "搜尋項目時發生錯誤");
                AddLogMessage($"搜尋錯誤: {ex.Message}");
            }
        }
        
        private void ClearSearch()
        {
            SearchTerm = "";
            // 重新載入完整列表
            _ = Task.Run(async () => await BrowseOPCDAItemsAsync());
        }
        
        public void LoadChildNodes(OPCTreeNode node)
        {
            try
            {
                if (!IsOPCDAConnected || node == null || node.IsLoaded || !node.HasChildren)
                    return;
                
                // 直接調用服務層的 LoadChildNodes，它會處理所有的邏輯
                // 不需要在這裡操作 node.Children，服務層會處理
                _opcDaService.LoadChildNodes(node);
                
                AddLogMessage($"載入了節點 '{node.Name}' 的 {node.Children.Count} 個子項目");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"載入子節點時發生錯誤: {node?.FullPath}");
                AddLogMessage($"載入子節點錯誤: {ex.Message}");
            }
        }

        private void SyncMappingsToUI()
        {
            ItemMappings.Clear();
            foreach (var mapping in _dataMappingService.Mappings)
            {
                ItemMappings.Add(mapping);
            }
        }

        private void UpdateUI(object sender, EventArgs e)
        {
            CurrentTime = DateTime.Now;
            OnPropertyChanged(nameof(OPCDAItemCount));
            OnPropertyChanged(nameof(OPCUANodeCount));
            OnPropertyChanged(nameof(ActiveMappingCount));
            OnPropertyChanged(nameof(ConnectedClientCount));

            // 批次刷新日誌到 UI（每秒一次，而非每筆訊息）
            if (_logDirty)
            {
                _logDirty = false;
                lock (_logBuilder)
                {
                    LogMessages = _logBuilder.ToString();
                }
            }
        }

        private volatile bool _logDirty;

        private void AddLogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            lock (_logBuilder)
            {
                _logBuilder.AppendLine($"[{timestamp}] {message}");

                // 限制日誌長度
                if (_logBuilder.Length > 50000)
                {
                    var text = _logBuilder.ToString();
                    _logBuilder.Clear();
                    _logBuilder.Append(text.Substring(text.Length - 40000));
                }
            }

            _logDirty = true;
        }

        #endregion

        #region Event Handlers

        private void OnOPCDAConnectionStatusChanged(object sender, bool isConnected)
        {
            IsOPCDAConnected = isConnected;
        }

        private void OnOPCDADataChanged(object sender, OPCDAItem item)
        {
            // 確保在UI線程上更新
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                // 更新即時數據顯示
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
                
                // 也更新OPCDAItems集合中的對應項目（如果存在）
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

        private void OnOPCUAServerStatusChanged(object sender, bool isRunning)
        {
            IsOPCUARunning = isRunning;
            OnPropertyChanged(nameof(OPCUAEndpointUrl));
        }

        private void OnOPCUAClientCountChanged(object sender, int count)
        {
            // 更新客戶端列表（簡化實現）
            OnPropertyChanged(nameof(ConnectedClientCount));
        }

        private void OnOPCUAClientConnected(object sender, Models.ClientInfo clientInfo)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 客戶端連接信息已在服務中的ConnectedClients集合中管理
                OnPropertyChanged(nameof(ConnectedClientCount));
                OnPropertyChanged(nameof(ConnectedClients));
                AddLogMessage($"新客戶端連接: {clientInfo.DisplayName}");
            });
        }

        private void OnOPCUAClientDisconnected(object sender, string sessionId)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 客戶端斷開信息已在服務中的ConnectedClients集合中管理
                OnPropertyChanged(nameof(ConnectedClientCount));
                OnPropertyChanged(nameof(ConnectedClients));
                AddLogMessage($"客戶端斷開連接: {sessionId}");
            });
        }

        private void OnMappingAdded(object sender, ItemMapping mapping)
        {
            if (!ItemMappings.Contains(mapping))
            {
                ItemMappings.Add(mapping);
            }
        }

        private void OnMappingRemoved(object sender, ItemMapping mapping)
        {
            ItemMappings.Remove(mapping);
        }

        private void OnDataMapped(object sender, DataMappingEventArgs e)
        {
            // 可以在這裡添加映射統計或其他邏輯
        }

        private void OnLogMessage(object sender, string message)
        {
            AddLogMessage(message);
        }

        private void OnMappingSetupFailed(object sender, string message)
        {
            AddLogMessage($"[映射警告] {message}");
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        logger.Info("MainViewModel 正在清理資源...");

                        // 停止並釋放UI更新計時器
                        if (_uiUpdateTimer != null)
                        {
                            _uiUpdateTimer.Stop();
                            _uiUpdateTimer.Tick -= UpdateUI;
                        }

                        // 取消事件訂閱
                        _opcDaService.ConnectionStatusChanged -= OnOPCDAConnectionStatusChanged;
                        _opcDaService.DataChanged -= OnOPCDADataChanged;
                        _opcDaService.LogMessage -= OnLogMessage;

                        _opcUaService.ServerStatusChanged -= OnOPCUAServerStatusChanged;
                        _opcUaService.ClientCountChanged -= OnOPCUAClientCountChanged;
                        _opcUaService.ClientConnected -= OnOPCUAClientConnected;
                        _opcUaService.ClientDisconnected -= OnOPCUAClientDisconnected;
                        _opcUaService.LogMessage -= OnLogMessage;

                        _dataMappingService.MappingAdded -= OnMappingAdded;
                        _dataMappingService.MappingRemoved -= OnMappingRemoved;
                        _dataMappingService.DataMapped -= OnDataMapped;
                        _dataMappingService.LogMessage -= OnLogMessage;
                        _dataMappingService.MappingSetupFailed -= OnMappingSetupFailed;

                        // 停止所有服務
                        if (IsOPCUARunning)
                        {
                            logger.Info("自動停止OPC UA伺服器");
                            StopOPCUA();
                        }

                        if (IsOPCDAConnected)
                        {
                            logger.Info("自動斷開OPC DA連線");
                            DisconnectOPCDA();
                        }

                        // 釋放服務資源
                        _dataMappingService?.Dispose();
                        _opcUaService?.Dispose();
                        _opcDaService?.Dispose();

                        logger.Info("MainViewModel 資源清理完成");
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "處置 MainViewModel 時發生錯誤");
                    }
                }
                
                _disposed = true;
            }
        }

        ~MainViewModel()
        {
            Dispose(false);
        }

        #endregion
    }
}