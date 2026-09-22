using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OPCGateway.App.Services;
using OPCGateway.Core.Configuration;
using OPCGateway.Core.Engine;
using OPCGateway.Core.Logging;

namespace OPCGateway.App.ViewModels;

/// <summary>主視窗殼層：閘道啟停、全域狀態列、錯誤提示。</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly GatewayEngine _engine;
    private readonly ConfigStore _store;
    private readonly LogBuffer _logBuffer;
    private readonly ThemeService _theme;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartGatewayCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopGatewayCommand))]
    [NotifyPropertyChangedFor(nameof(RunStateText))]
    private GatewayRunState _runState = GatewayRunState.Stopped;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartGatewayCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopGatewayCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _busyMessage;
    [ObservableProperty] private bool _daConnected;
    [ObservableProperty] private bool _uaRunning;
    [ObservableProperty] private int _tagCount;
    [ObservableProperty] private int _activeTagCount;
    [ObservableProperty] private int _clientCount;
    [ObservableProperty] private double _updatesPerSecond;
    [ObservableProperty] private long _errorCount;
    [ObservableProperty] private long _warningCount;
    [ObservableProperty] private DateTime _clock = DateTime.Now;
    [ObservableProperty] private string _endpointText = "尚未啟動";
    [ObservableProperty] private string? _lastError;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _daText = "DA 未連線";
    [ObservableProperty] private string _uaText = "UA 未啟動";
    [ObservableProperty] private bool _isDarkTheme;

    public ShellViewModel(GatewayEngine engine, ConfigStore store, LogBuffer logBuffer, ThemeService theme, ILogger<ShellViewModel> logger)
    {
        _engine = engine;
        _store = store;
        _logBuffer = logBuffer;
        _theme = theme;
        _logger = logger;
        _dispatcher = Application.Current.Dispatcher;

        _engine.StateChanged += (_, state) => _dispatcher.InvokeAsync(() => { RunState = state; Refresh(); });
        _engine.StatisticsUpdated += (_, stats) => _dispatcher.InvokeAsync(() => UpdatesPerSecond = Math.Round(stats.UpdatesPerSecond, 1));
        _engine.ClientsChanged += (_, clients) => _dispatcher.InvokeAsync(() => ClientCount = clients.Count);
        _engine.ErrorOccurred += (_, message) => _dispatcher.InvokeAsync(() => ShowError(message));
        _theme.Changed += (_, current) => _dispatcher.InvokeAsync(() => IsDarkTheme = current == Wpf.Ui.Appearance.ApplicationTheme.Dark);

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            Clock = DateTime.Now;
            ErrorCount = _logBuffer.ErrorCount;
            WarningCount = _logBuffer.WarningCount;
            TagCount = _engine.TagCount;
            ActiveTagCount = _engine.ActiveTagCount;
        };
        _timer.Start();
    }

    public string RunStateText => RunState switch
    {
        GatewayRunState.Stopped => "已停止",
        GatewayRunState.Starting => "啟動中",
        GatewayRunState.Running => "運作中",
        GatewayRunState.Degraded => "部分運作",
        GatewayRunState.Stopping => "停止中",
        _ => RunState.ToString(),
    };

    public string ConfigPath => _store.DefaultPath;

    /// <summary>載入設定並依 AutoStart 決定是否啟動。</summary>
    /// <param name="preloaded">App 啟動時已先載入（並套用主題）的設定；為 null 時在此載入。</param>
    public async Task InitializeAsync(GatewayConfig? preloaded = null)
    {
        try
        {
            var config = preloaded ?? _store.Load();
            if (preloaded == null)
                _theme.Apply(ThemeService.Parse(config.Options.Theme));
            await _engine.ApplyConfigAsync(config);
            Refresh();
            _logger.LogInformation("設定已載入：{Count} 個標籤", config.Tags.Count);

            if (config.Options.AutoStart)
            {
                _logger.LogInformation("依設定自動啟動閘道");
                await StartGatewayAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化失敗");
            ShowError("初始化失敗：" + ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartGateway))]
    private async Task StartGatewayAsync()
    {
        IsBusy = true;
        BusyMessage = "正在啟動閘道…";
        try
        {
            await _engine.StartAsync();
            HasError = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "啟動閘道失敗");
            ShowError("啟動閘道失敗：" + Flatten(ex));
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
            RunState = _engine.State;
            Refresh();
        }
    }

    private bool CanStartGateway() => !IsBusy && (RunState == GatewayRunState.Stopped || RunState == GatewayRunState.Degraded);

    [RelayCommand(CanExecute = nameof(CanStopGateway))]
    private async Task StopGatewayAsync()
    {
        IsBusy = true;
        BusyMessage = "正在停止閘道…";
        try
        {
            await _engine.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止閘道失敗");
            ShowError("停止閘道失敗：" + ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
            RunState = _engine.State;
            Refresh();
        }
    }

    private bool CanStopGateway() => !IsBusy && RunState != GatewayRunState.Stopped && RunState != GatewayRunState.Stopping;

    [RelayCommand]
    private void SaveConfig()
    {
        try
        {
            _store.Save(_engine.BuildConfig());
            _logger.LogInformation("設定已儲存到 {Path}", _store.DefaultPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存設定失敗");
            ShowError("儲存設定失敗：" + ex.Message);
        }
    }

    /// <summary>在淺色與深色之間切換，並記到設定（離開跟隨系統模式）。</summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        _theme.Toggle();
        _engine.Config.Options.Theme = _theme.Mode.ToString();
    }

    [RelayCommand]
    private void DismissError()
    {
        HasError = false;
        LastError = null;
    }

    public void SaveConfigQuietly()
    {
        try
        {
            _store.Save(_engine.BuildConfig());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "關閉時儲存設定失敗");
        }
    }

    private void Refresh()
    {
        DaConnected = _engine.DaConnected;
        UaRunning = _engine.UaRunning;
        TagCount = _engine.TagCount;
        ActiveTagCount = _engine.ActiveTagCount;
        ClientCount = _engine.Clients.Count;
        DaText = DaConnected ? $"DA 已連線 {_engine.Config.DaSource.ProgId}" : "DA 未連線";
        UaText = UaRunning ? $"UA 運作中 :{_engine.Config.UaServer.Port}" : "UA 未啟動";
        EndpointText = UaRunning && _engine.UaEndpointUrls.Count > 0
            ? string.Join(Environment.NewLine, _engine.UaEndpointUrls)
            : "尚未啟動";
    }

    private void ShowError(string message)
    {
        LastError = message;
        HasError = true;
    }

    private static string Flatten(Exception ex)
    {
        if (ex is AggregateException aggregate)
            return string.Join("；", aggregate.InnerExceptions.Select(e => e.Message));
        return ex.Message;
    }
}
