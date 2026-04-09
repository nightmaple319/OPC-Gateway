using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using OPCGatewayTool.Models;
using NLog;
using TitaniumAS.Opc.Client.Da;
using TitaniumAS.Opc.Client.Common;
using TitaniumAS.Opc.Client.Da.Browsing;

namespace OPCGatewayTool.Services
{
    public class TitaniumOPCDAService : IDisposable
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        
        private OpcDaServer _opcServer;
        private OpcDaGroup _opcGroup;
        private bool _isConnected;
        private bool _disposed;
        private Timer _reconnectTimer;
        private readonly object _lockObject = new object();
        
        public event EventHandler<OPCDAItem> DataChanged;
        public event EventHandler<bool> ConnectionStatusChanged;
        public event EventHandler<string> LogMessage;

        public ObservableCollection<OPCDAItem> Items { get; } = new ObservableCollection<OPCDAItem>();
        
        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    ConnectionStatusChanged?.Invoke(this, value);
                    logger.Info($"OPC DA 連接狀態變更: {(value ? "已連接" : "已斷開")}");
                }
            }
        }

        public string ServerName { get; private set; }
        public string HostName { get; private set; }

        static TitaniumOPCDAService()
        {
            // TitaniumAS 1.0.2 不需要 Bootstrap.Initialize()
            LogManager.GetCurrentClassLogger().Info("TitaniumAS.Opc.Client 準備就緒");
        }

        public async Task<bool> ConnectAsync(string serverName, string hostName = "localhost")
        {
            return await Task.Run(() => Connect(serverName, hostName));
        }

        public bool Connect(string serverName, string hostName = "localhost")
        {
            try
            {
                lock (_lockObject)
                {
                    if (_disposed)
                        throw new ObjectDisposedException(nameof(TitaniumOPCDAService));

                    if (string.IsNullOrWhiteSpace(serverName))
                    {
                        var errorMsg = "伺服器名稱不能為空";
                        logger.Error(errorMsg);
                        LogMessage?.Invoke(this, errorMsg);
                        return false;
                    }

                    Disconnect();

                    ServerName = serverName.Trim();
                    HostName = hostName?.Trim() ?? "localhost";

                    logger.Info($"正在連接到 OPC DA 伺服器: '{ServerName}' @ '{HostName}'");
                    LogMessage?.Invoke(this, $"嘗試連接: {ServerName}");

                    // 建立伺服器連接
                    var uri = UrlBuilder.Build(ServerName, HostName);
                    _opcServer = new OpcDaServer(uri);
                    
                    logger.Debug("正在連接到伺服器...");
                    _opcServer.Connect();
                    logger.Debug("伺服器連接成功，正在創建群組...");

                    // 創建群組
                    _opcGroup = _opcServer.AddGroup("MainGroup");
                    _opcGroup.IsActive = true;
                    _opcGroup.UpdateRate = TimeSpan.FromMilliseconds(1000);
                    
                    // 訂閱數據變更事件
                    _opcGroup.ValuesChanged += OnDataChanged;

                    IsConnected = true;
                    StartReconnectTimer();
                    
                    logger.Info($"成功連接到 OPC DA 伺服器: {ServerName}");
                    LogMessage?.Invoke(this, $"連接成功: {ServerName}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"連接 OPC DA 伺服器失敗: {ex.Message}");
                LogMessage?.Invoke(this, $"連接錯誤: {ex.Message}");
                IsConnected = false;
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                lock (_lockObject)
                {
                    StopReconnectTimer();

                    if (_opcGroup != null)
                    {
                        _opcGroup.ValuesChanged -= OnDataChanged;
                        _opcGroup.IsActive = false;
                        _opcServer?.RemoveGroup(_opcGroup);
                        _opcGroup = null;
                    }

                    if (_opcServer != null && IsConnected)
                    {
                        _opcServer.Disconnect();
                        _opcServer.Dispose();
                        _opcServer = null;
                    }

                    Items.Clear();
                    IsConnected = false;
                    
                    logger.Info("OPC DA 伺服器已斷開連接");
                    LogMessage?.Invoke(this, "OPC DA 伺服器已斷開連接");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "斷開連接時發生錯誤");
            }
        }

        public List<string> GetAvailableServers(string hostName = "localhost")
        {
            var servers = new List<string>();
            try
            {
                logger.Info($"正在掃描主機 {hostName} 上的 OPC DA 伺服器...");
                
                // 暫時使用測試連接方式來檢測伺服器
                var commonServers = new[]
                {
                    "Matrikon.OPC.Simulation.1",
                    "Matrikon.OPC.Simulation",
                    "Kepware.KEPServerEX.V6",
                    "RSLinx OPC Server"
                };
                
                foreach (var serverName in commonServers)
                {
                    try
                    {
                        var uri = UrlBuilder.Build(serverName, hostName);
                        using (var testServer = new OpcDaServer(uri))
                        {
                            testServer.Connect();
                            testServer.Disconnect();
                            
                            servers.Add(serverName);
                            logger.Info($"確認 OPC DA 伺服器存在: {serverName}");
                        }
                    }
                    catch
                    {
                        // 連接失敗表示伺服器不存在，忽略
                        logger.Debug($"伺服器不存在或無法連接: {serverName}");
                    }
                }
                
                logger.Info($"掃描完成，找到 {servers.Count} 個 OPC DA 伺服器");
                LogMessage?.Invoke(this, $"找到 {servers.Count} 個真實伺服器");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "掃描 OPC DA 伺服器時發生錯誤");
                LogMessage?.Invoke(this, $"掃描錯誤: {ex.Message}");
            }
            
            return servers;
        }

        public async Task<List<string>> BrowseItemsAsync(string parentPath = "")
        {
            return await Task.Run(() => BrowseItems(parentPath));
        }

        public List<string> BrowseItems(string parentPath = "")
        {
            var items = new List<string>();
            
            if (!IsConnected)
            {
                logger.Warn("OPC 伺服器未連接，無法瀏覽項目");
                LogMessage?.Invoke(this, "請先連接 OPC DA 伺服器");
                return items;
            }

            try
            {
                lock (_lockObject)
                {
                    logger.Info($"開始遞歸瀏覽 OPC DA 項目，路徑: '{parentPath}'");
                    
                    var browser = new OpcDaBrowserAuto(_opcServer);
                    BrowseChildrenRecursive(browser, parentPath, items, 0);
                }
                
                logger.Info($"瀏覽完成，找到 {items.Count} 個項目，路徑: '{parentPath}'");
                LogMessage?.Invoke(this, $"找到 {items.Count} 個項目");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"瀏覽項目時發生錯誤，路徑: '{parentPath}'");
                LogMessage?.Invoke(this, $"瀏覽錯誤: {ex.Message}");
            }
            
            return items;
        }

        public List<OPCTreeNode> BrowseItemsAsTree(string parentPath = "")
        {
            var rootNodes = new List<OPCTreeNode>();
            var seenItemIds = new HashSet<string>(); // 防重複
            var seenNodesByName = new Dictionary<string, OPCTreeNode>(); // 按顯示名稱去重
            
            if (!IsConnected)
            {
                logger.Warn("OPC 伺服器未連接，無法瀏覽項目");
                LogMessage?.Invoke(this, "請先連接 OPC DA 伺服器");
                return rootNodes;
            }

            try
            {
                lock (_lockObject)
                {
                    logger.Info($"開始瀏覽樹狀 OPC DA 項目，路徑: '{parentPath}'");
                    
                    var browser = new OpcDaBrowserAuto(_opcServer);
                    var elements = browser.GetElements(string.IsNullOrEmpty(parentPath) ? null : parentPath);
                    
                    var elementsList = elements.ToList(); // 轉換為 List 以便重複枚舉
                    logger.Info($"從 OPC 伺服器獲得 {elementsList.Count} 個元素");
                    
                    // 記錄所有原始元素
                    logger.Info("=== OPC 服務器返回的原始元素清單 ===");
                    for (int i = 0; i < elementsList.Count; i++)
                    {
                        var element = elementsList[i];
                        logger.Info($"元素 #{i+1}: ItemId='{element.ItemId}', Name='{element.Name}', HasChildren={element.HasChildren}");
                    }
                    logger.Info("=== 原始元素清單結束 ===");
                    
                    int nodeCount = 0;
                    foreach (var element in elementsList)
                    {
                        logger.Debug($"處理元素: ItemId='{element.ItemId}', Name='{element.Name}', HasChildren={element.HasChildren}");
                        
                        // 限制節點數量，避免無限循環
                        if (nodeCount >= 100)
                        {
                            logger.Warn("達到最大節點數量限制，停止創建更多節點");
                            break;
                        }
                        
                        // 防止完全相同的 ItemId 重複
                        if (seenItemIds.Contains(element.ItemId))
                        {
                            logger.Warn($"發現重複 ItemId，跳過: {element.ItemId}");
                            continue;
                        }
                        seenItemIds.Add(element.ItemId);
                        
                        var nodeName = GetNodeDisplayName(element.ItemId);
                        
                        // 額外檢查：防止相同顯示名稱的節點重複（可能是 OPC 服務器的問題）
                        var nodeKey = $"{nodeName}_{element.HasChildren}"; // 資料夾和文件可以同名
                        if (seenNodesByName.ContainsKey(nodeKey))
                        {
                            logger.Warn($"發現重複顯示名稱的節點，跳過: '{nodeName}' (ItemId: {element.ItemId})");
                            logger.Warn($"原有節點 ItemId: {seenNodesByName[nodeKey].FullPath}");
                            continue;
                        }
                        
                        var node = new OPCTreeNode(nodeName, element.ItemId, element.HasChildren);
                        seenNodesByName[nodeKey] = node;
                        
                        rootNodes.Add(node);
                        nodeCount++;
                        logger.Info($"創建樹節點 #{nodeCount}: '{nodeName}' (路徑: {element.ItemId}, 有子項: {element.HasChildren}, HashCode: {node.GetHashCode()})");
                    }
                }
                
                logger.Info($"樹狀瀏覽完成，找到 {rootNodes.Count} 個根節點（去重後）");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"樹狀瀏覽項目時發生錯誤，路徑: '{parentPath}'");
                LogMessage?.Invoke(this, $"樹狀瀏覽錯誤: {ex.Message}");
            }
            
            return rootNodes;
        }

        private readonly Dictionary<string, bool> _loadingNodes = new Dictionary<string, bool>();
        
        public void LoadChildNodes(OPCTreeNode parentNode)
        {
            if (!IsConnected || parentNode == null || parentNode.IsLoaded || !parentNode.HasChildren)
                return;

            // 防止同一個節點被同時載入多次
            lock (_lockObject)
            {
                if (_loadingNodes.ContainsKey(parentNode.FullPath))
                {
                    logger.Debug($"節點 {parentNode.FullPath} 正在載入中，跳過重複請求");
                    return;
                }
                _loadingNodes[parentNode.FullPath] = true;
            }

            try
            {
                lock (_lockObject)
                {
                    logger.Debug($"載入子節點: {parentNode.FullPath}");
                    
                    // 再次檢查是否已載入（雙重檢查模式）
                    if (parentNode.IsLoaded)
                    {
                        logger.Debug($"節點 {parentNode.FullPath} 在等待期間已被載入");
                        return;
                    }
                    
                    var browser = new OpcDaBrowserAuto(_opcServer);
                    var elements = browser.GetElements(parentNode.FullPath);
                    
                    // 先收集到臨時列表，避免在枚舉時修改集合
                    var elementsToAdd = new List<OPCTreeNode>();
                    var seenItemIds = new HashSet<string>(); // 防重複
                    var seenNodesByName = new Dictionary<string, OPCTreeNode>(); // 按顯示名稱去重
                    
                    logger.Info($"從 OPC 伺服器獲得 {elements.Count()} 個子元素，父節點: {parentNode.FullPath}");
                    
                    int childCount = 0;
                    foreach (var element in elements)
                    {
                        logger.Debug($"處理子元素: ItemId='{element.ItemId}', Name='{element.Name}', HasChildren={element.HasChildren}");
                        
                        // 限制子節點數量
                        if (childCount >= 50)
                        {
                            logger.Warn($"達到子節點數量限制: {parentNode.FullPath}");
                            break;
                        }
                        
                        // 防止完全相同的 ItemId 重複
                        if (seenItemIds.Contains(element.ItemId))
                        {
                            logger.Warn($"發現重複子節點 ItemId，跳過: {element.ItemId}");
                            continue;
                        }
                        seenItemIds.Add(element.ItemId);
                        
                        var nodeName = GetNodeDisplayName(element.ItemId);
                        
                        // 額外檢查：防止相同顯示名稱的節點重複
                        var nodeKey = $"{nodeName}_{element.HasChildren}";
                        if (seenNodesByName.ContainsKey(nodeKey))
                        {
                            logger.Warn($"發現重複顯示名稱的子節點，跳過: '{nodeName}' (ItemId: {element.ItemId})");
                            logger.Warn($"原有子節點 ItemId: {seenNodesByName[nodeKey].FullPath}");
                            continue;
                        }
                        
                        var childNode = new OPCTreeNode(nodeName, element.ItemId, element.HasChildren);
                        seenNodesByName[nodeKey] = childNode;
                        
                        elementsToAdd.Add(childNode);
                        childCount++;
                        logger.Info($"準備添加子節點 #{childCount}: '{nodeName}' 到父節點 '{parentNode.FullPath}' (HashCode: {childNode.GetHashCode()})");
                    }
                    
                    // 在UI線程上安全地更新集合
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        // 先檢查是否已經載入，避免重複操作
                        if (!parentNode.IsLoaded)
                        {
                            try
                            {
                                // 安全地記錄清除前的狀態，避免並發枚舉
                                var beforeCount = parentNode.Children.Count;
                                logger.Info($"===== 開始更新父節點 '{parentNode.FullPath}' =====");
                                logger.Info($"清除前有 {beforeCount} 個子節點");
                                
                                // 直接清除所有子節點（包括佔位符）
                                parentNode.Children.Clear();
                                logger.Info($"已清除所有現有子節點");
                                
                                // 添加新的子節點
                                int addedCount = 0;
                                foreach (var childNode in elementsToAdd)
                                {
                                    parentNode.Children.Add(childNode);
                                    addedCount++;
                                    logger.Info($"已添加子節點 #{addedCount}: '{childNode.Name}' (FullPath: {childNode.FullPath})");
                                }
                                
                                parentNode.IsLoaded = true;
                                logger.Info($"===== 完成載入，父節點 '{parentNode.FullPath}' 現在有 {parentNode.Children.Count} 個子節點 =====");
                            }
                            catch (Exception dispatcherEx)
                            {
                                logger.Error(dispatcherEx, $"在UI線程更新子節點時發生錯誤: {parentNode.FullPath}");
                            }
                        }
                        else
                        {
                            logger.Warn($"節點 {parentNode.FullPath} 已載入，跳過重複操作");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"載入子節點失敗: {parentNode?.FullPath}");
                // 確保標記為已載入，避免重複嘗試
                if (parentNode != null)
                {
                    parentNode.IsLoaded = true;
                }
            }
            finally
            {
                // 清理載入狀態
                if (parentNode != null)
                {
                    lock (_lockObject)
                    {
                        _loadingNodes.Remove(parentNode.FullPath);
                    }
                }
            }
        }

        public List<string> SearchItems(string searchTerm)
        {
            var matchingItems = new List<string>();
            
            if (!IsConnected || string.IsNullOrWhiteSpace(searchTerm))
                return matchingItems;

            try
            {
                lock (_lockObject)
                {
                    logger.Info($"搜尋 OPC DA 項目: '{searchTerm}'");
                    
                    var allItems = new List<string>();
                    var browser = new OpcDaBrowserAuto(_opcServer);
                    SearchItemsRecursive(browser, null, allItems, searchTerm.ToLower(), 0);
                    
                    matchingItems = allItems.Where(item => 
                        item.ToLower().Contains(searchTerm.ToLower())).ToList();
                }
                
                logger.Info($"搜尋完成，找到 {matchingItems.Count} 個匹配項目");
                LogMessage?.Invoke(this, $"搜尋到 {matchingItems.Count} 個匹配項目");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"搜尋項目時發生錯誤: '{searchTerm}'");
                LogMessage?.Invoke(this, $"搜尋錯誤: {ex.Message}");
            }
            
            return matchingItems;
        }

        private void SearchItemsRecursive(OpcDaBrowserAuto browser, string itemId, List<string> allItems, string searchTerm, int depth)
        {
            try
            {
                if (depth > 10) return;

                var elements = browser.GetElements(string.IsNullOrEmpty(itemId) ? null : itemId);
                
                foreach (var element in elements)
                {
                    // 只收集實際的項目（非資料夾）
                    if (!element.HasChildren)
                    {
                        allItems.Add(element.ItemId);
                    }
                    else
                    {
                        // 遞歸搜尋子資料夾
                        SearchItemsRecursive(browser, element.ItemId, allItems, searchTerm, depth + 1);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"搜尋遞歸失敗，路徑: '{itemId}', 深度: {depth}");
            }
        }

        private string GetNodeDisplayName(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return "Root";
                
            // 取得最後一個部分作為顯示名稱
            var parts = fullPath.Split('.');
            return parts.Length > 0 ? parts[parts.Length - 1] : fullPath;
        }

        private void BrowseChildrenRecursive(OpcDaBrowserAuto browser, string itemId, List<string> items, int depth)
        {
            try
            {
                // 防止過深的遞歸
                if (depth > 10)
                {
                    logger.Warn($"達到最大遞歸深度，停止瀏覽: {itemId}");
                    return;
                }

                var elements = browser.GetElements(string.IsNullOrEmpty(itemId) ? null : itemId);
                
                foreach (var element in elements)
                {
                    var displayPath = element.ItemId;
                    
                    if (element.HasChildren)
                    {
                        // 添加資料夾本身
                        items.Add($"[資料夾] {displayPath}");
                        logger.Debug($"找到資料夾 (深度 {depth}): {displayPath}");
                        
                        // 遞歸瀏覽子項目
                        BrowseChildrenRecursive(browser, element.ItemId, items, depth + 1);
                    }
                    else
                    {
                        // 添加實際項目
                        items.Add(displayPath);
                        logger.Debug($"找到項目 (深度 {depth}): {displayPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"瀏覽子項目時發生錯誤，路徑: '{itemId}', 深度: {depth}");
            }
        }

        public bool AddItem(string itemId)
        {
            if (!IsConnected)
            {
                logger.Warn($"OPC 伺服器未連接，無法添加項目: {itemId}");
                return false;
            }

            try
            {
                lock (_lockObject)
                {
                    var existingItem = Items.FirstOrDefault(i => i.ItemId == itemId);
                    if (existingItem != null)
                    {
                        logger.Info($"項目已存在: {itemId}");
                        return true;
                    }

                    // 創建項目定義
                    var itemDefinition = new OpcDaItemDefinition
                    {
                        ItemId = itemId,
                        IsActive = true
                    };

                    // 添加到群組
                    var results = _opcGroup.AddItems(new[] { itemDefinition });
                    
                    if (results.Length > 0 && results[0].Error.Succeeded)
                    {
                        var result = results[0];
                        
                        var newItem = new OPCDAItem
                        {
                            ItemId = itemId,
                            ClientHandle = result.Item.ClientHandle,
                            ServerHandle = result.Item.ServerHandle,
                            DataType = result.Item.CanonicalDataType.ToString(),
                            Quality = 0,
                            Timestamp = DateTime.Now
                        };

                        Items.Add(newItem);
                        logger.Info($"成功添加 OPC DA 項目: {itemId}");
                        LogMessage?.Invoke(this, $"添加項目: {itemId}");
                        return true;
                    }
                    else
                    {
                        logger.Error($"添加項目失敗: {itemId}, 錯誤: {results[0]?.Error}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"添加項目失敗: {itemId}");
                LogMessage?.Invoke(this, $"添加項目失敗: {ex.Message}");
                return false;
            }
        }

        public bool RemoveItem(string itemId)
        {
            try
            {
                lock (_lockObject)
                {
                    var item = Items.FirstOrDefault(i => i.ItemId == itemId);
                    if (item == null)
                        return false;

                    // 從群組中移除項目
                    var opcItem = _opcGroup.Items.FirstOrDefault(i => i.ClientHandle == item.ClientHandle);
                    if (opcItem != null)
                    {
                        _opcGroup.RemoveItems(new[] { opcItem });
                    }

                    Items.Remove(item);
                    logger.Info($"成功移除 OPC DA 項目: {itemId}");
                    LogMessage?.Invoke(this, $"移除項目: {itemId}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"移除項目失敗: {itemId}");
                LogMessage?.Invoke(this, $"移除項目失敗: {ex.Message}");
                return false;
            }
        }

        private void OnDataChanged(object sender, OpcDaItemValuesChangedEventArgs e)
        {
            try
            {
                foreach (var itemValue in e.Values)
                {
                    var item = Items.FirstOrDefault(x => x.ClientHandle == itemValue.Item.ClientHandle);
                    if (item != null)
                    {
                        item.Value = itemValue.Value;
                        item.Quality = (short)itemValue.Quality;
                        item.Timestamp = itemValue.Timestamp.DateTime;
                        
                        if (itemValue.Value != null)
                        {
                            item.DataType = itemValue.Value.GetType().Name;
                        }

                        DataChanged?.Invoke(this, item);
                        logger.Debug($"項目值更新: {item.ItemId} = {item.Value}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "處理數據變更事件時發生錯誤");
            }
        }

        private void StartReconnectTimer()
        {
            if (_reconnectTimer == null)
            {
                _reconnectTimer = new Timer(CheckConnection, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            }
        }

        private void StopReconnectTimer()
        {
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
        }

        private void CheckConnection(object state)
        {
            try
            {
                if (!IsConnected && !string.IsNullOrEmpty(ServerName))
                {
                    logger.Info("嘗試重新連接 OPC DA 伺服器");
                    Connect(ServerName, HostName);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "自動重連檢查時發生錯誤");
            }
        }

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
                    Disconnect();
                }
                _disposed = true;
            }
        }

        ~TitaniumOPCDAService()
        {
            Dispose(false);
        }
    }
}