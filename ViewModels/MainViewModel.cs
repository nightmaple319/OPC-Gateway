using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Win32;
using Newtonsoft.Json;
using NLog;
using OPCGatewayTool.Interfaces;
using OPCGatewayTool.Models;
using OPCGatewayTool.Services;

namespace OPCGatewayTool.ViewModels
{
    /// <summary>
    /// 主 ViewModel：負責協調服務、子 ViewModel、UI Timer，以及配置載入/儲存。
    /// 本身不含業務邏輯，只做組裝（composition）。
    /// </summary>
    public class MainViewModel : ObservableObject, IDisposable
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IOPCDAService _opcDaService;
        private readonly IOPCUAService _opcUaService;
        private readonly DataMappingService _dataMappingService;
        private readonly DispatcherTimer _uiUpdateTimer;
        private GatewayConfig _config;
        private bool _disposed;

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

                // 初始化子 ViewModels（按依賴順序）
                Log = new LogViewModel();
                OPCDA = new OPCDAViewModel(_opcDaService, Log);
                OPCUA = new OPCUAViewModel(_opcUaService, Log);
                Mapping = new MappingViewModel(_dataMappingService, OPCDA, Log);

                // 將 Config 下發到子 VM
                OPCDA.UpdateConfig(_config.OPCDAConfig);
                OPCUA.UpdateConfig(_config.OPCUAConfig);

                // OPC UA 啟動成功後，設置所有映射
                OPCUA.StartedSuccessfully += OnOPCUAStartedSuccessfully;

                // 初始化 Main 層級的命令
                LoadConfigCommand = new RelayCommand(LoadConfig);
                SaveConfigCommand = new RelayCommand(SaveConfig);

                // 啟動 UI 更新計時器
                _uiUpdateTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
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

        #region Child ViewModels

        public LogViewModel Log { get; }
        public OPCDAViewModel OPCDA { get; }
        public OPCUAViewModel OPCUA { get; }
        public MappingViewModel Mapping { get; }

        #endregion

        #region Properties

        public GatewayConfig Config
        {
            get => _config;
            set
            {
                if (SetProperty(ref _config, value))
                {
                    // 下發到子 VM
                    OPCDA.UpdateConfig(_config.OPCDAConfig);
                    OPCUA.UpdateConfig(_config.OPCUAConfig);
                }
            }
        }

        #endregion

        #region Delegated properties/commands (for MainWindow.xaml.cs compatibility)

        /// <summary>
        /// 委派自 OPCDA.IsConnected，供 MainWindow.xaml.cs 使用。
        /// </summary>
        public bool IsOPCDAConnected => OPCDA?.IsConnected ?? false;

        /// <summary>
        /// 委派自 OPCUA.IsRunning，供 MainWindow.xaml.cs 使用。
        /// </summary>
        public bool IsOPCUARunning => OPCUA?.IsRunning ?? false;

        /// <summary>
        /// 委派自 OPCDA.DisconnectCommand，供 MainWindow.xaml.cs 的 CleanupAllConnections 使用。
        /// </summary>
        public ICommand DisconnectOPCDACommand => OPCDA?.DisconnectCommand;

        /// <summary>
        /// 委派自 OPCUA.StopCommand，供 MainWindow.xaml.cs 的 CleanupAllConnections 使用。
        /// </summary>
        public ICommand StopOPCUACommand => OPCUA?.StopCommand;

        /// <summary>
        /// 委派給 OPCDA.LoadChildNodes，供 MainWindow.xaml.cs 的 TreeViewItem_Expanded 使用。
        /// </summary>
        public void LoadChildNodes(OPCTreeNode node) => OPCDA?.LoadChildNodes(node);

        #endregion

        #region Commands (Main 層級)

        public ICommand LoadConfigCommand { get; }
        public ICommand SaveConfigCommand { get; }

        #endregion

        #region Private Methods

        private void LoadDefaultConfiguration()
        {
            OPCDA.LoadDefaultAvailableServers();

            _config.OPCDAConfig.ServerName = "Matrikon.OPC.Simulation.1";
            _config.OPCDAConfig.HostName = "localhost";
            _config.OPCDAConfig.UpdateRate = 1000;

            Mapping.IsEnabled = true;
        }

        private async void OnOPCUAStartedSuccessfully(object sender, EventArgs e)
        {
            try
            {
                await Mapping.SetupAllMappingsAsync();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "設置所有映射時發生錯誤");
                Log.AddMessage($"設置映射錯誤: {ex.Message}");
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
                    Mapping.LoadMappingsFromConfig(config.ItemMappings);

                    Log.AddMessage($"配置已載入: {dialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "載入配置時發生錯誤");
                Log.AddMessage($"載入配置錯誤: {ex.Message}");
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
                    _config.ItemMappings = Mapping.GetCurrentMappings();

                    var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                    File.WriteAllText(dialog.FileName, json);

                    Log.AddMessage($"配置已保存: {dialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "保存配置時發生錯誤");
                Log.AddMessage($"保存配置錯誤: {ex.Message}");
            }
        }

        private void UpdateUI(object sender, EventArgs e)
        {
            // 更新時間
            Log.CurrentTime = DateTime.Now;

            // 日誌批次刷新
            Log.FlushIfDirty();

            // 各子 VM 的計數更新
            OPCDA?.RefreshCounts();
            OPCUA?.RefreshCounts();
            Mapping?.RefreshCounts();

            // 委派屬性需要手動通知變更
            OnPropertyChanged(nameof(IsOPCDAConnected));
            OnPropertyChanged(nameof(IsOPCUARunning));
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

                        // 停止並釋放 UI 更新計時器
                        if (_uiUpdateTimer != null)
                        {
                            _uiUpdateTimer.Stop();
                            _uiUpdateTimer.Tick -= UpdateUI;
                        }

                        // 解除 OPCUA 的 StartedSuccessfully 訂閱
                        if (OPCUA != null)
                        {
                            OPCUA.StartedSuccessfully -= OnOPCUAStartedSuccessfully;
                        }

                        // 讓子 VM 取消服務事件訂閱
                        OPCDA?.UnsubscribeAll();
                        OPCUA?.UnsubscribeAll();
                        Mapping?.UnsubscribeAll();

                        // 停止所有服務
                        if (IsOPCUARunning)
                        {
                            logger.Info("自動停止OPC UA伺服器");
                            OPCUA?.Stop();
                        }

                        if (IsOPCDAConnected)
                        {
                            logger.Info("自動斷開OPC DA連線");
                            OPCDA?.Disconnect();
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
