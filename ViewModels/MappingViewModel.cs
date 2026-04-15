using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using OPCGatewayTool.Models;
using OPCGatewayTool.Services;
using NLog;

namespace OPCGatewayTool.ViewModels
{
    /// <summary>
    /// 數據映射相關的子 ViewModel：映射的新增、移除、啟用切換。
    /// </summary>
    public class MappingViewModel : ObservableObject
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly DataMappingService _dataMappingService;
        private readonly OPCDAViewModel _opcDaVm;
        private readonly LogViewModel _log;

        private bool _isEnabled;

        public MappingViewModel(DataMappingService dataMappingService, OPCDAViewModel opcDaVm, LogViewModel log)
        {
            _dataMappingService = dataMappingService ?? throw new ArgumentNullException(nameof(dataMappingService));
            _opcDaVm = opcDaVm ?? throw new ArgumentNullException(nameof(opcDaVm));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            ItemMappings = new ObservableCollection<ItemMapping>();

            AddSelectedItemsCommand = new RelayCommand(AddSelectedItems);
            ClearCommand = new RelayCommand(ClearMappings);

            _dataMappingService.MappingAdded += OnMappingAdded;
            _dataMappingService.MappingRemoved += OnMappingRemoved;
            _dataMappingService.DataMapped += OnDataMapped;
            _dataMappingService.LogMessage += OnLogMessage;
            _dataMappingService.MappingSetupFailed += OnMappingSetupFailed;
        }

        #region Properties

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetProperty(ref _isEnabled, value))
                {
                    _dataMappingService.IsEnabled = value;
                }
            }
        }

        public int ActiveCount => ItemMappings?.Count(m => m.IsEnabled) ?? 0;

        public ObservableCollection<ItemMapping> ItemMappings { get; }

        #endregion

        #region Commands

        public ICommand AddSelectedItemsCommand { get; }
        public ICommand ClearCommand { get; }

        #endregion

        #region Public Methods

        /// <summary>
        /// 由 UI Timer 定期呼叫。
        /// </summary>
        public void RefreshCounts()
        {
            OnPropertyChanged(nameof(ActiveCount));
        }

        /// <summary>
        /// 設置所有映射（OPC UA 啟動完成後呼叫）。
        /// </summary>
        public async Task<bool> SetupAllMappingsAsync()
        {
            return await _dataMappingService.SetupAllMappingsAsync();
        }

        /// <summary>
        /// 從配置載入映射（LoadConfig 呼叫）。
        /// </summary>
        public void LoadMappingsFromConfig(List<ItemMapping> mappings)
        {
            _dataMappingService.LoadMappingsFromConfig(mappings);
            SyncMappingsToUI();
        }

        /// <summary>
        /// 將目前 ItemMappings 寫回配置物件（SaveConfig 呼叫）。
        /// </summary>
        public List<ItemMapping> GetCurrentMappings()
        {
            return ItemMappings.ToList();
        }

        /// <summary>
        /// 取消訂閱所有服務事件。
        /// </summary>
        public void UnsubscribeAll()
        {
            _dataMappingService.MappingAdded -= OnMappingAdded;
            _dataMappingService.MappingRemoved -= OnMappingRemoved;
            _dataMappingService.DataMapped -= OnDataMapped;
            _dataMappingService.LogMessage -= OnLogMessage;
            _dataMappingService.MappingSetupFailed -= OnMappingSetupFailed;
        }

        #endregion

        #region Command Implementations

        private void AddSelectedItems()
        {
            try
            {
                // 從樹狀視圖取得被勾選的項目
                var selectedTreeItems = _opcDaVm.GetSelectedTreeItems().ToList();

                // 同時檢查平面列表中被勾選的項目
                var selectedListItems = _opcDaVm.OPCDAItems.Where(i => i.IsSelected).ToList();

                if (!selectedTreeItems.Any() && !selectedListItems.Any())
                {
                    _log.AddMessage("請選擇要添加的項目");
                    return;
                }

                var allSelectedItemIds = new HashSet<string>();

                foreach (var treeItem in selectedTreeItems)
                {
                    if (!treeItem.IsFolder)
                    {
                        allSelectedItemIds.Add(treeItem.FullPath);
                    }
                }

                foreach (var listItem in selectedListItems)
                {
                    allSelectedItemIds.Add(listItem.ItemId);
                }

                if (!allSelectedItemIds.Any())
                {
                    _log.AddMessage("請選擇數據項目（不能只選擇資料夾）");
                    return;
                }

                int successCount = 0;
                foreach (var itemId in allSelectedItemIds)
                {
                    if (_opcDaVm.AddItem(itemId))
                    {
                        var browseName = itemId.Replace(".", "_");
                        _dataMappingService.AddMapping(itemId, browseName);
                        successCount++;
                        _log.AddMessage($"成功添加項目: {itemId}");
                    }
                    else
                    {
                        _log.AddMessage($"添加項目失敗: {itemId}");
                    }
                }

                SyncMappingsToUI();
                _log.AddMessage($"成功添加 {successCount}/{allSelectedItemIds.Count} 個項目");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "添加選中項目時發生錯誤");
                _log.AddMessage($"添加項目錯誤: {ex.Message}");
            }
        }

        private void ClearMappings()
        {
            try
            {
                _dataMappingService.ClearAllMappings();
                ItemMappings.Clear();
                _log.AddMessage("已清除所有映射");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "清除映射時發生錯誤");
                _log.AddMessage($"清除映射錯誤: {ex.Message}");
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

        #endregion

        #region Event Handlers

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
            // 預留：映射統計或其他邏輯
        }

        private void OnLogMessage(object sender, string message)
        {
            _log.AddMessage(message);
        }

        private void OnMappingSetupFailed(object sender, string message)
        {
            _log.AddMessage($"[映射警告] {message}");
        }

        #endregion
    }
}
