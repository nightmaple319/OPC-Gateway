using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OPCGateway.Core.Da;
using OPCGateway.Core.Engine;
using OPCGateway.Core.Ua;

namespace OPCGateway.App.ViewModels;

/// <summary>監控頁：KPI 卡片、標籤即時表、已連線客戶端。</summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly GatewayEngine _engine;
    private readonly ILogger<DashboardViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private DateTime _lastFilterRefreshUtc = DateTime.MinValue;

    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _showOnlyProblems;
    [ObservableProperty] private TagMapping? _selectedTag;
    [ObservableProperty] private bool _hasTags;
    [ObservableProperty] private bool _hasClients;
    [ObservableProperty] private int _tagCount;
    [ObservableProperty] private int _activeTagCount;
    [ObservableProperty] private int _problemTagCount;
    [ObservableProperty] private int _clientCount;
    [ObservableProperty] private long _totalUpdates;
    [ObservableProperty] private double _updatesPerSecond;
    [ObservableProperty] private long _badQualityUpdates;
    [ObservableProperty] private long _droppedUpdates;
    [ObservableProperty] private string _lastUpdateText = "—";
    [ObservableProperty] private string _uptimeText = "—";
    [ObservableProperty] private bool _daConnected;
    [ObservableProperty] private bool _uaRunning;
    [ObservableProperty] private string _endpointsText = "尚未啟動";
    [ObservableProperty] private string _daServerText = "—";

    public DashboardViewModel(GatewayEngine engine, ILogger<DashboardViewModel> logger)
    {
        _engine = engine;
        _logger = logger;
        _dispatcher = Application.Current.Dispatcher;

        BindingOperations.EnableCollectionSynchronization(_engine.Tags, _engine.TagsLock);
        TagsView = CollectionViewSource.GetDefaultView(_engine.Tags);
        TagsView.Filter = FilterTag;

        _engine.Tags.CollectionChanged += OnTagsCollectionChanged;
        _engine.TagsLiveChanged += OnTagsLiveChanged;
        _engine.StatisticsUpdated += OnStatisticsUpdated;
        _engine.ClientsChanged += OnClientsChanged;
        _engine.StateChanged += (_, _) => _dispatcher.InvokeAsync(RefreshState);

        RefreshState();
        UpdateCounts();
    }

    public ICollectionView TagsView { get; }
    public ObservableCollection<UaClientInfo> Clients { get; } = new();

    partial void OnFilterTextChanged(string value) => TagsView.Refresh();
    partial void OnShowOnlyProblemsChanged(bool value) => TagsView.Refresh();

    private bool FilterTag(object item)
    {
        if (item is not TagMapping tag)
            return false;

        if (ShowOnlyProblems && !IsProblem(tag))
            return false;

        if (string.IsNullOrWhiteSpace(FilterText))
            return true;

        return tag.ItemId.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0
               || tag.BrowseName.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsProblem(TagMapping tag) =>
        tag.State == TagState.Error || tag.State == TagState.Idle ||
        (tag.State == TagState.Active && tag.QualityMaster != DaQualityMaster.Good);

    [RelayCommand]
    private async Task RemoveTagAsync(TagMapping? tag)
    {
        tag ??= SelectedTag;
        if (tag == null)
            return;
        try
        {
            await _engine.RemoveTagsAsync(new[] { tag.ItemId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移除標籤失敗");
        }
    }

    [RelayCommand]
    private void ToggleTag(TagMapping? tag)
    {
        tag ??= SelectedTag;
        if (tag != null)
            tag.Enabled = !tag.Enabled;
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        if (_engine.TagCount == 0)
            return;
        var result = MessageBox.Show("確定要移除所有標籤嗎？此動作會立即停止對應的 OPC UA 節點。", "移除所有標籤",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
            return;
        try
        {
            await _engine.ClearTagsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清除標籤失敗");
        }
    }

    [RelayCommand]
    private void EnableAll()
    {
        foreach (var tag in SnapshotTags())
            tag.Enabled = true;
    }

    [RelayCommand]
    private void DisableAll()
    {
        foreach (var tag in SnapshotTags())
            tag.Enabled = false;
    }

    [RelayCommand]
    private void CopyNodeId(TagMapping? tag)
    {
        tag ??= SelectedTag;
        if (tag == null)
            return;
        TrySetClipboard(tag.NodeIdText);
    }

    [RelayCommand]
    private void CopyEndpoints()
    {
        if (_engine.UaEndpointUrls.Count > 0)
            TrySetClipboard(string.Join(Environment.NewLine, _engine.UaEndpointUrls));
    }

    private static void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch { /* 剪貼簿被其他程式鎖住時忽略 */ }
    }

    private TagMapping[] SnapshotTags()
    {
        lock (_engine.TagsLock)
            return _engine.Tags.ToArray();
    }

    private void OnTagsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _dispatcher.InvokeAsync(UpdateCounts, DispatcherPriority.Background);
    }

    private void OnTagsLiveChanged(object? sender, IReadOnlyList<TagMapping> batch)
    {
        _dispatcher.InvokeAsync(() =>
        {
            foreach (var tag in batch)
                tag.NotifyLiveChanged();

            UpdateCounts();

            if (ShowOnlyProblems && (DateTime.UtcNow - _lastFilterRefreshUtc).TotalSeconds >= 1)
            {
                _lastFilterRefreshUtc = DateTime.UtcNow;
                TagsView.Refresh();
            }
        }, DispatcherPriority.Background);
    }

    private void OnStatisticsUpdated(object? sender, GatewayStatistics stats)
    {
        _dispatcher.InvokeAsync(() =>
        {
            TotalUpdates = stats.TotalUpdates;
            UpdatesPerSecond = Math.Round(stats.UpdatesPerSecond, 1);
            BadQualityUpdates = stats.BadQualityUpdates;
            DroppedUpdates = stats.DroppedUpdates;
            LastUpdateText = stats.LastUpdateUtc.HasValue ? stats.LastUpdateUtc.Value.ToLocalTime().ToString("HH:mm:ss.fff") : "—";
            UptimeText = stats.StartedAtUtc.HasValue ? FormatUptime(stats.Uptime) : "—";
        }, DispatcherPriority.Background);
    }

    private void OnClientsChanged(object? sender, IReadOnlyList<UaClientInfo> clients)
    {
        _dispatcher.InvokeAsync(() =>
        {
            Clients.Clear();
            foreach (var client in clients)
                Clients.Add(client);
            ClientCount = Clients.Count;
            HasClients = Clients.Count > 0;
        });
    }

    private void RefreshState()
    {
        DaConnected = _engine.DaConnected;
        UaRunning = _engine.UaRunning;
        DaServerText = _engine.DaClient.ProgId != null ? $"{_engine.DaClient.ProgId} @ {_engine.DaClient.Host}" : $"{_engine.Config.DaSource.ProgId} @ {_engine.Config.DaSource.Host}";
        EndpointsText = UaRunning && _engine.UaEndpointUrls.Count > 0
            ? string.Join(Environment.NewLine, _engine.UaEndpointUrls)
            : "尚未啟動";
        if (!UaRunning)
        {
            Clients.Clear();
            ClientCount = 0;
            HasClients = false;
        }
    }

    private void UpdateCounts()
    {
        var tags = SnapshotTags();
        TagCount = tags.Length;
        HasTags = tags.Length > 0;
        ActiveTagCount = tags.Count(t => t.State == TagState.Active);
        ProblemTagCount = tags.Count(IsProblem);
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays} 天 {uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";
        return $"{(int)uptime.TotalHours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";
    }
}
