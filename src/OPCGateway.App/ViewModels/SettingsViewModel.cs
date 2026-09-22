using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OPCGateway.App.Services;
using OPCGateway.Core.Configuration;
using OPCGateway.Core.Da;
using OPCGateway.Core.Engine;

namespace OPCGateway.App.ViewModels;

/// <summary>設定頁：DA 來源、UA 伺服器、一般選項；含驗證、匯入匯出。</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly GatewayEngine _engine;
    private readonly ConfigStore _store;
    private readonly ThemeService _theme;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private bool _loadingTheme;

    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _hasStatus;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _configPath = string.Empty;
    [ObservableProperty] private string _endpointPreview = string.Empty;
    [ObservableProperty] private ThemeChoice? _selectedTheme;

    public SettingsViewModel(GatewayEngine engine, ConfigStore store, ThemeService theme, ILogger<SettingsViewModel> logger)
    {
        _engine = engine;
        _store = store;
        _theme = theme;
        _logger = logger;
        _dispatcher = Application.Current.Dispatcher;
        ConfigPath = store.DefaultPath;

        _engine.StateChanged += (_, state) => _dispatcher.InvokeAsync(() => IsRunning = state != GatewayRunState.Stopped);
        Ua.PropertyChanged += (_, _) => UpdateEndpointPreview();
        _theme.Changed += (_, _) => _dispatcher.InvokeAsync(() => SetThemeSelection(_theme.Mode));
        LoadFrom(_engine.Config);
    }

    public IReadOnlyList<ThemeChoice> ThemeOptions { get; } = new[]
    {
        new ThemeChoice(ThemeMode.System, "跟隨系統"),
        new ThemeChoice(ThemeMode.Light, "淺色（Fluent 藍）"),
        new ThemeChoice(ThemeMode.Dark, "深色（石墨青）"),
    };

    partial void OnSelectedThemeChanged(ThemeChoice? value)
    {
        if (_loadingTheme || value == null)
            return;
        _theme.Apply(value.Mode);
        _engine.Config.Options.Theme = value.Mode.ToString();
    }

    private void SetThemeSelection(ThemeMode mode)
    {
        _loadingTheme = true;
        try
        {
            SelectedTheme = ThemeOptions.First(o => o.Mode == mode);
        }
        finally
        {
            _loadingTheme = false;
        }
    }

    public DaSettingsViewModel Da { get; } = new();
    public UaSettingsViewModel Ua { get; } = new();
    public ObservableCollection<DaServerInfo> AvailableServers { get; } = new();
    public ObservableCollection<string> ValidationErrors { get; } = new();

    public void LoadFrom(GatewayConfig config)
    {
        Da.LoadFrom(config.DaSource);
        Ua.LoadFrom(config.UaServer);
        AutoStart = config.Options.AutoStart;
        SetThemeSelection(ThemeService.Parse(config.Options.Theme));
        IsRunning = _engine.State != GatewayRunState.Stopped;
        ValidationErrors.Clear();
        UpdateEndpointPreview();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        Da.ValidateAll();
        Ua.ValidateAll();
        ValidationErrors.Clear();
        foreach (var error in Da.ErrorMessages.Concat(Ua.ErrorMessages))
            ValidationErrors.Add(error);

        if (ValidationErrors.Count > 0)
        {
            SetStatus("請先修正欄位錯誤", isError: true);
            return;
        }

        IsBusy = true;
        try
        {
            var config = _engine.BuildConfig();
            var wasRunning = IsRunning;
            Da.WriteTo(config.DaSource);
            Ua.WriteTo(config.UaServer);
            config.Options.AutoStart = AutoStart;
            config.Options.Theme = (SelectedTheme?.Mode ?? ThemeMode.System).ToString();

            await _engine.ApplyConfigAsync(config);
            _store.Save(config);

            SetStatus(wasRunning
                ? "設定已儲存。DA 與 UA 的連線參數要重新啟動閘道後才會生效。"
                : "設定已儲存。", isError: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "套用設定失敗");
            SetStatus("套用設定失敗：" + ex.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Revert()
    {
        LoadFrom(_engine.Config);
        SetStatus("已還原為目前生效的設定", isError: false);
    }

    [RelayCommand]
    private async Task ScanServersAsync()
    {
        IsBusy = true;
        try
        {
            var host = string.IsNullOrWhiteSpace(Da.Host) ? "localhost" : Da.Host.Trim();
            var servers = await _engine.DaClient.EnumerateServersAsync(host);
            AvailableServers.Clear();
            foreach (var server in servers)
                AvailableServers.Add(server);
            SetStatus(servers.Count == 0
                ? $"在 {host} 找不到 OPC DA 伺服器。請確認 OPC Core Components 已安裝且 OpcEnum 服務可用。"
                : $"在 {host} 找到 {servers.Count} 個 OPC DA 伺服器", isError: servers.Count == 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "掃描 OPC DA 伺服器失敗");
            SetStatus("掃描失敗：" + ex.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectServer(DaServerInfo? server)
    {
        if (server != null)
            Da.ProgId = server.ProgId;
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "匯入設定檔",
            Filter = "JSON 設定檔 (*.json)|*.json|所有檔案 (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(_store.DefaultPath),
        };
        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        try
        {
            var config = _store.Load(dialog.FileName);
            await _engine.ApplyConfigAsync(config);
            _store.Backup();
            _store.Save(config);
            LoadFrom(config);
            SetStatus($"已匯入 {Path.GetFileName(dialog.FileName)}（{config.Tags.Count} 個標籤），並寫入預設設定檔", isError: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "匯入設定失敗");
            SetStatus("匯入失敗：" + ex.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = "匯出設定檔",
            Filter = "JSON 設定檔 (*.json)|*.json",
            FileName = $"gateway_config_{DateTime.Now:yyyyMMdd_HHmm}.json",
            InitialDirectory = Path.GetDirectoryName(_store.DefaultPath),
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            _store.Save(_engine.BuildConfig(), dialog.FileName);
            SetStatus($"已匯出到 {dialog.FileName}", isError: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "匯出設定失敗");
            SetStatus("匯出失敗：" + ex.Message, isError: true);
        }
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        try
        {
            var directory = Path.GetDirectoryName(_store.DefaultPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "開啟設定資料夾失敗");
        }
    }

    [RelayCommand]
    private void DismissStatus() => HasStatus = false;

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
        HasStatus = true;
    }

    private void UpdateEndpointPreview()
    {
        var host = Ua.UseHostNameInEndpoint ? Environment.MachineName : "<IP>";
        EndpointPreview = $"opc.tcp://{host}:{Ua.Port}";
    }
}

/// <summary>設定頁的主題選項。</summary>
public sealed class ThemeChoice
{
    public ThemeChoice(ThemeMode mode, string label)
    {
        Mode = mode;
        Label = label;
    }

    public ThemeMode Mode { get; }
    public string Label { get; }
    public override string ToString() => Label;
}

/// <summary>OPC DA 來源設定的可驗證表單。</summary>
public sealed partial class DaSettingsViewModel : ObservableValidator
{
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "請輸入 OPC DA 伺服器的 ProgID")]
    private string _progId = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "請輸入主機名稱")]
    private string _host = "localhost";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(50, 3_600_000, ErrorMessage = "更新頻率需介於 50 到 3600000 毫秒")]
    private int _updateRateMs = 1000;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0.0, 100.0, ErrorMessage = "死區需介於 0 到 100（百分比）")]
    private float _percentDeadband;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 300, ErrorMessage = "連線逾時需介於 1 到 300 秒")]
    private int _connectTimeoutSeconds = 10;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 3600, ErrorMessage = "重連間隔需介於 1 到 3600 秒")]
    private int _reconnectIntervalSeconds = 10;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 3600, ErrorMessage = "健康檢查間隔需介於 1 到 3600 秒")]
    private int _healthCheckIntervalSeconds = 5;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "請輸入階層分隔符號")]
    private string _branchSeparator = ".";

    public IEnumerable<string> ErrorMessages => GetErrors().Select(e => e.ErrorMessage ?? string.Empty).Where(m => m.Length > 0);

    public void ValidateAll() => ValidateAllProperties();

    public void LoadFrom(DaSourceConfig config)
    {
        ProgId = config.ProgId;
        Host = config.Host;
        UpdateRateMs = config.UpdateRateMs;
        PercentDeadband = config.PercentDeadband;
        ConnectTimeoutSeconds = config.ConnectTimeoutSeconds;
        ReconnectIntervalSeconds = config.ReconnectIntervalSeconds;
        HealthCheckIntervalSeconds = config.HealthCheckIntervalSeconds;
        BranchSeparator = config.BranchSeparator;
        ClearErrors();
    }

    public void WriteTo(DaSourceConfig config)
    {
        config.ProgId = ProgId.Trim();
        config.Host = Host.Trim();
        config.UpdateRateMs = UpdateRateMs;
        config.PercentDeadband = PercentDeadband;
        config.ConnectTimeoutSeconds = ConnectTimeoutSeconds;
        config.ReconnectIntervalSeconds = ReconnectIntervalSeconds;
        config.HealthCheckIntervalSeconds = HealthCheckIntervalSeconds;
        config.BranchSeparator = BranchSeparator;
    }
}

/// <summary>OPC UA 伺服器設定的可驗證表單。</summary>
public sealed partial class UaSettingsViewModel : ObservableValidator
{
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "請輸入伺服器名稱")]
    private string _serverName = "OPC Gateway Server";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "請輸入應用程式名稱")]
    private string _applicationName = "OPC DA to UA Gateway";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "請輸入 Application URI")]
    [RegularExpression(@"^\S+$", ErrorMessage = "Application URI 不可含空白")]
    private string _applicationUri = "urn:localhost:OPCGateway";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 65535, ErrorMessage = "連接埠需介於 1 到 65535")]
    private int _port = 4840;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 10000, ErrorMessage = "最大工作階段數需介於 1 到 10000")]
    private int _maxSessions = 100;

    [ObservableProperty] private bool _useHostNameInEndpoint = true;
    [ObservableProperty] private bool _allowNoSecurity = true;
    [ObservableProperty] private bool _enableSecurity;
    [ObservableProperty] private bool _autoAcceptClientCertificates = true;
    [ObservableProperty] private bool _mirrorDaHierarchy = true;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "請輸入根資料夾名稱")]
    private string _rootFolderName = "Gateway";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "請輸入命名空間 URI")]
    [RegularExpression(@"^\S+$", ErrorMessage = "命名空間 URI 不可含空白")]
    private string _namespaceUri = "urn:opcgateway:da";

    public IEnumerable<string> ErrorMessages => GetErrors().Select(e => e.ErrorMessage ?? string.Empty).Where(m => m.Length > 0);

    public void ValidateAll() => ValidateAllProperties();

    public void LoadFrom(UaServerConfig config)
    {
        ServerName = config.ServerName;
        ApplicationName = config.ApplicationName;
        ApplicationUri = config.ApplicationUri;
        Port = config.Port;
        MaxSessions = config.MaxSessions;
        UseHostNameInEndpoint = config.UseHostNameInEndpoint;
        AllowNoSecurity = config.AllowNoSecurity;
        EnableSecurity = config.EnableSecurity;
        AutoAcceptClientCertificates = config.AutoAcceptClientCertificates;
        MirrorDaHierarchy = config.MirrorDaHierarchy;
        RootFolderName = config.RootFolderName;
        NamespaceUri = config.NamespaceUri;
        ClearErrors();
    }

    public void WriteTo(UaServerConfig config)
    {
        config.ServerName = ServerName.Trim();
        config.ApplicationName = ApplicationName.Trim();
        config.ApplicationUri = ApplicationUri.Trim();
        config.Port = Port;
        config.MaxSessions = MaxSessions;
        config.UseHostNameInEndpoint = UseHostNameInEndpoint;
        config.AllowNoSecurity = AllowNoSecurity;
        config.EnableSecurity = EnableSecurity;
        config.AutoAcceptClientCertificates = AutoAcceptClientCertificates;
        config.MirrorDaHierarchy = MirrorDaHierarchy;
        config.RootFolderName = RootFolderName.Trim();
        config.NamespaceUri = NamespaceUri.Trim();
    }
}
