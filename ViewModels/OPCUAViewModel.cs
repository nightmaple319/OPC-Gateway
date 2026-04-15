using System;
using System.Collections.ObjectModel;
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
    /// OPC UA 相關的子 ViewModel：伺服器啟停、客戶端監控。
    /// </summary>
    public class OPCUAViewModel : ObservableObject
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IOPCUAService _opcUaService;
        private readonly LogViewModel _log;

        private OPCUAConfig _config;
        private bool _isRunning;

        public OPCUAViewModel(IOPCUAService opcUaService, LogViewModel log)
        {
            _opcUaService = opcUaService ?? throw new ArgumentNullException(nameof(opcUaService));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            // 初始化命令
            StartCommand = new AsyncRelayCommand(StartAsync);
            StopCommand = new RelayCommand(Stop);

            // 訂閱服務事件
            _opcUaService.ServerStatusChanged += OnServerStatusChanged;
            _opcUaService.ClientCountChanged += OnClientCountChanged;
            _opcUaService.ClientConnected += OnClientConnected;
            _opcUaService.ClientDisconnected += OnClientDisconnected;
            _opcUaService.LogMessage += OnLogMessage;
        }

        #region Properties

        public OPCUAConfig Config
        {
            get => _config;
            set => SetProperty(ref _config, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    OnPropertyChanged(nameof(ServerStatus));
                    OnPropertyChanged(nameof(EndpointUrl));
                }
            }
        }

        public string ServerStatus => IsRunning ? "運行中" : "已停止";

        public string EndpointUrl => IsRunning ? (_opcUaService?.EndpointUrl ?? Config?.EndpointUrl ?? "未啟動") : "未啟動";

        public int NodeCount => _opcUaService?.Nodes?.Count ?? 0;

        public int ConnectedClientCount => _opcUaService?.ConnectedClientCount ?? 0;

        public ObservableCollection<ClientInfo> ConnectedClients => _opcUaService?.ConnectedClients;

        #endregion

        #region Commands

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }

        #endregion

        #region Public Methods

        /// <summary>
        /// 由 MainViewModel 載入配置時呼叫。
        /// </summary>
        public void UpdateConfig(OPCUAConfig config)
        {
            Config = config;
        }

        /// <summary>
        /// 由 UI Timer 定期呼叫。
        /// </summary>
        public void RefreshCounts()
        {
            OnPropertyChanged(nameof(NodeCount));
            OnPropertyChanged(nameof(ConnectedClientCount));
        }

        /// <summary>
        /// OPC UA 服務啟動成功後，等待映射設置完成的 callback（由 MainViewModel 觸發）。
        /// </summary>
        public event EventHandler StartedSuccessfully;

        /// <summary>
        /// 取消訂閱所有服務事件。
        /// </summary>
        public void UnsubscribeAll()
        {
            _opcUaService.ServerStatusChanged -= OnServerStatusChanged;
            _opcUaService.ClientCountChanged -= OnClientCountChanged;
            _opcUaService.ClientConnected -= OnClientConnected;
            _opcUaService.ClientDisconnected -= OnClientDisconnected;
            _opcUaService.LogMessage -= OnLogMessage;
        }

        #endregion

        #region Command Implementations

        private async Task StartAsync()
        {
            try
            {
                _log.AddMessage("正在啟動 OPC UA 伺服器...");

                var success = await _opcUaService.StartAsync(Config);

                if (success)
                {
                    _log.AddMessage("OPC UA 伺服器啟動成功");
                    StartedSuccessfully?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    _log.AddMessage("OPC UA 伺服器啟動失敗");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "啟動 OPC UA 時發生錯誤");
                _log.AddMessage($"啟動錯誤: {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                _opcUaService.Stop();
                ConnectedClients?.Clear();
                _log.AddMessage("OPC UA 伺服器已停止");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "停止 OPC UA 時發生錯誤");
                _log.AddMessage($"停止錯誤: {ex.Message}");
            }
        }

        #endregion

        #region Event Handlers

        private void OnServerStatusChanged(object sender, bool isRunning)
        {
            IsRunning = isRunning;
        }

        private void OnClientCountChanged(object sender, int count)
        {
            OnPropertyChanged(nameof(ConnectedClientCount));
        }

        private void OnClientConnected(object sender, ClientInfo clientInfo)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(ConnectedClientCount));
                OnPropertyChanged(nameof(ConnectedClients));
                _log.AddMessage($"新客戶端連接: {clientInfo.DisplayName}");
            });
        }

        private void OnClientDisconnected(object sender, string sessionId)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(ConnectedClientCount));
                OnPropertyChanged(nameof(ConnectedClients));
                _log.AddMessage($"客戶端斷開連接: {sessionId}");
            });
        }

        private void OnLogMessage(object sender, string message)
        {
            _log.AddMessage(message);
        }

        #endregion
    }
}
