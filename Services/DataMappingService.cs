using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Concurrent;
using OPCGatewayTool.Models;
using NLog;

namespace OPCGatewayTool.Services
{
    public class DataMappingService : IDisposable
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        
        private readonly TitaniumOPCDAService _opcDaService;
        private readonly SimpleRealOPCUAService _opcUaService;
        private readonly ConcurrentDictionary<string, ItemMapping> _mappings;
        private bool _disposed;
        private bool _isEnabled;
        
        public event EventHandler<ItemMapping> MappingAdded;
        public event EventHandler<ItemMapping> MappingRemoved;
        public event EventHandler<DataMappingEventArgs> DataMapped;
        public event EventHandler<string> LogMessage;

        public ObservableCollection<ItemMapping> Mappings { get; }
        
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                logger.Info($"數據映射服務 {(value ? "已啟用" : "已停用")}");
            }
        }

        public int TotalMappings => _mappings.Count;
        public int ActiveMappings => _mappings.Count(m => m.Value.IsEnabled);

        public DataMappingService(TitaniumOPCDAService opcDaService, SimpleRealOPCUAService opcUaService)
        {
            _opcDaService = opcDaService ?? throw new ArgumentNullException(nameof(opcDaService));
            _opcUaService = opcUaService ?? throw new ArgumentNullException(nameof(opcUaService));
            
            _mappings = new ConcurrentDictionary<string, ItemMapping>();
            Mappings = new ObservableCollection<ItemMapping>();
            
            // 訂閱OPC DA數據變更事件
            _opcDaService.DataChanged += OnOPCDADataChanged;
            
            logger.Info("數據映射服務已初始化");
        }

        public bool AddMapping(string opcDaItemId, string opcUaBrowseName, string opcUaNodeId = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(opcDaItemId) || string.IsNullOrWhiteSpace(opcUaBrowseName))
                {
                    logger.Warn("無效的映射參數");
                    return false;
                }

                if (_mappings.ContainsKey(opcDaItemId))
                {
                    logger.Info($"映射已存在: {opcDaItemId}");
                    return true;
                }

                var mapping = new ItemMapping
                {
                    OPCDAItemId = opcDaItemId,
                    OPCUABrowseName = opcUaBrowseName,
                    OPCUANodeId = opcUaNodeId ?? $"Gateway.{opcUaBrowseName}",
                    IsEnabled = true
                };

                if (_mappings.TryAdd(opcDaItemId, mapping))
                {
                    Mappings.Add(mapping);
                    
                    // 嘗試添加到OPC DA和OPC UA服務
                    SetupMapping(mapping);
                    
                    MappingAdded?.Invoke(this, mapping);
                    logger.Info($"成功添加數據映射: {opcDaItemId} -> {opcUaBrowseName}");
                    LogMessage?.Invoke(this, $"添加映射: {opcDaItemId} -> {opcUaBrowseName}");
                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"添加映射失敗: {opcDaItemId}");
                return false;
            }
        }

        public bool RemoveMapping(string opcDaItemId)
        {
            try
            {
                if (_mappings.TryRemove(opcDaItemId, out ItemMapping mapping))
                {
                    Mappings.Remove(mapping);
                    
                    // 從服務中移除
                    CleanupMapping(mapping);
                    
                    MappingRemoved?.Invoke(this, mapping);
                    logger.Info($"成功移除數據映射: {opcDaItemId}");
                    LogMessage?.Invoke(this, $"移除映射: {opcDaItemId}");
                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"移除映射失敗: {opcDaItemId}");
                return false;
            }
        }

        public bool EnableMapping(string opcDaItemId, bool enabled)
        {
            try
            {
                if (_mappings.TryGetValue(opcDaItemId, out ItemMapping mapping))
                {
                    mapping.IsEnabled = enabled;
                    logger.Info($"映射 {opcDaItemId} {(enabled ? "已啟用" : "已停用")}");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"設置映射狀態失敗: {opcDaItemId}");
                return false;
            }
        }

        public async Task<bool> SetupAllMappingsAsync()
        {
            return await Task.Run(SetupAllMappings);
        }

        public bool SetupAllMappings()
        {
            try
            {
                logger.Info("開始設置所有映射");
                int successCount = 0;
                
                foreach (var mapping in _mappings.Values.Where(m => m.IsEnabled))
                {
                    if (SetupMapping(mapping))
                    {
                        successCount++;
                    }
                }
                
                logger.Info($"成功設置 {successCount}/{_mappings.Count} 個映射");
                LogMessage?.Invoke(this, $"已設置 {successCount} 個映射");
                
                return successCount > 0;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "設置所有映射時發生錯誤");
                return false;
            }
        }

        public void ClearAllMappings()
        {
            try
            {
                logger.Info("清除所有映射");
                
                foreach (var mapping in _mappings.Values)
                {
                    CleanupMapping(mapping);
                }
                
                _mappings.Clear();
                Mappings.Clear();
                
                logger.Info("所有映射已清除");
                LogMessage?.Invoke(this, "所有映射已清除");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "清除映射時發生錯誤");
            }
        }

        public List<ItemMapping> GetMappingsForOPCDAItem(string opcDaItemId)
        {
            return _mappings.Values.Where(m => m.OPCDAItemId == opcDaItemId).ToList();
        }

        public ItemMapping GetMappingByOPCDAItem(string opcDaItemId)
        {
            _mappings.TryGetValue(opcDaItemId, out ItemMapping mapping);
            return mapping;
        }

        public void LoadMappingsFromConfig(List<ItemMapping> mappings)
        {
            try
            {
                logger.Info($"從配置載入 {mappings?.Count ?? 0} 個映射");
                
                ClearAllMappings();
                
                if (mappings != null)
                {
                    foreach (var mapping in mappings)
                    {
                        if (!string.IsNullOrWhiteSpace(mapping.OPCDAItemId) && 
                            !string.IsNullOrWhiteSpace(mapping.OPCUABrowseName))
                        {
                            AddMapping(mapping.OPCDAItemId, mapping.OPCUABrowseName, mapping.OPCUANodeId);
                            EnableMapping(mapping.OPCDAItemId, mapping.IsEnabled);
                        }
                    }
                }
                
                logger.Info("映射配置載入完成");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "載入映射配置時發生錯誤");
            }
        }

        private bool SetupMapping(ItemMapping mapping)
        {
            try
            {
                bool opcDaAdded = true;
                bool opcUaAdded = true;
                
                // 添加到OPC DA服務
                if (_opcDaService.IsConnected)
                {
                    opcDaAdded = _opcDaService.AddItem(mapping.OPCDAItemId);
                }
                else
                {
                    logger.Warn($"OPC DA 服務未連接，無法添加項目: {mapping.OPCDAItemId}");
                    opcDaAdded = false;
                }
                
                // 添加到OPC UA服務 (獲取當前值)
                if (_opcUaService.IsRunning)
                {
                    // 嘗試從OPC DA服務獲取當前值
                    object currentValue = null;
                    var opcDaItem = _opcDaService.Items.FirstOrDefault(i => i.ItemId == mapping.OPCDAItemId);
                    if (opcDaItem != null)
                    {
                        currentValue = opcDaItem.Value;
                        logger.Debug($"從OPC DA獲取到當前值: {mapping.OPCDAItemId} = {currentValue}");
                    }
                    else
                    {
                        logger.Warn($"未找到OPC DA項目: {mapping.OPCDAItemId}，使用默認值0");
                        currentValue = 0;
                    }
                    
                    opcUaAdded = _opcUaService.AddNode(mapping.OPCDAItemId, mapping.OPCUABrowseName, 
                        currentValue, typeof(object));
                }
                else
                {
                    logger.Warn($"OPC UA 服務未運行，無法添加節點: {mapping.OPCUABrowseName}");
                    opcUaAdded = false;
                }
                
                bool success = opcDaAdded && opcUaAdded;
                if (success)
                {
                    logger.Debug($"映射設置成功: {mapping.OPCDAItemId} -> {mapping.OPCUABrowseName}");
                }
                else
                {
                    logger.Warn($"映射設置部分失敗: {mapping.OPCDAItemId} -> {mapping.OPCUABrowseName}");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"設置映射失敗: {mapping.OPCDAItemId}");
                return false;
            }
        }

        private void CleanupMapping(ItemMapping mapping)
        {
            try
            {
                // 從OPC DA服務中移除
                _opcDaService?.RemoveItem(mapping.OPCDAItemId);
                
                // 從OPC UA服務中移除
                _opcUaService?.RemoveNode(mapping.OPCDAItemId);
                
                logger.Debug($"映射清理完成: {mapping.OPCDAItemId}");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"清理映射失敗: {mapping.OPCDAItemId}");
            }
        }

        private void OnOPCDADataChanged(object sender, OPCDAItem item)
        {
            try
            {
                if (!IsEnabled)
                    return;

                if (_mappings.TryGetValue(item.ItemId, out ItemMapping mapping) && mapping.IsEnabled)
                {
                    // 更新OPC UA節點值
                    bool success = _opcUaService.UpdateNodeValue(item.ItemId, item.Value, item.Timestamp);
                    
                    if (success)
                    {
                        var eventArgs = new DataMappingEventArgs
                        {
                            Mapping = mapping,
                            OPCDAItem = item,
                            Timestamp = DateTime.Now
                        };
                        
                        DataMapped?.Invoke(this, eventArgs);
                        logger.Debug($"數據映射成功: {item.ItemId} = {item.Value}");
                    }
                    else
                    {
                        logger.Warn($"更新OPC UA節點值失敗: {item.ItemId}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"處理OPC DA數據變更時發生錯誤: {item?.ItemId}");
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
                    ClearAllMappings();
                    
                    if (_opcDaService != null)
                    {
                        _opcDaService.DataChanged -= OnOPCDADataChanged;
                    }
                }
                
                _disposed = true;
            }
        }

        ~DataMappingService()
        {
            Dispose(false);
        }
    }

    public class DataMappingEventArgs : EventArgs
    {
        public ItemMapping Mapping { get; set; }
        public OPCDAItem OPCDAItem { get; set; }
        public DateTime Timestamp { get; set; }
    }
}