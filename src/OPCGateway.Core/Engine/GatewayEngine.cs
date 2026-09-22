using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using OPCGateway.Core.Configuration;
using OPCGateway.Core.Da;
using OPCGateway.Core.Ua;

namespace OPCGateway.Core.Engine;

public enum GatewayRunState
{
    Stopped,
    Starting,
    Running,
    /// <summary>只有一邊在運作（DA 未連線或 UA 未啟動）。</summary>
    Degraded,
    Stopping,
}

/// <summary>
/// 閘道引擎：持有標籤的單一真相，負責把「期望狀態」（設定）對帳成實際的 DA 訂閱與 UA 節點，
/// 並以單一消費者的佇列把 DA 值變更推到 UA。所有事件在背景執行緒觸發。
/// </summary>
public sealed class GatewayEngine : IDisposable
{
    private readonly IDaClient _da;
    private readonly IUaServerHost _ua;
    private readonly ILogger<GatewayEngine> _logger;
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TagMapping> _index = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TagMapping> _dirty = new(StringComparer.Ordinal);
    private readonly HashSet<string> _uaNodes = new(StringComparer.Ordinal);
    private readonly object _uaNodesLock = new();

    private Channel<DaValue>? _channel;
    private Task? _consumerTask;
    private CancellationTokenSource? _consumerCts;
    private Timer? _refreshTimer;
    private Timer? _statsTimer;
    private GatewayRunState _state = GatewayRunState.Stopped;
    private bool _disposed;

    public GatewayEngine(IDaClient daClient, IUaServerHost uaServerHost, ILogger<GatewayEngine>? logger = null)
    {
        _da = daClient ?? throw new ArgumentNullException(nameof(daClient));
        _ua = uaServerHost ?? throw new ArgumentNullException(nameof(uaServerHost));
        _logger = logger ?? NullLogger<GatewayEngine>.Instance;

        Config = new GatewayConfig();
        Tags = new ObservableCollection<TagMapping>();
        Statistics = new GatewayStatistics();

        _da.ValuesChanged += OnDaValuesChanged;
        _da.ConnectionStateChanged += OnDaConnectionStateChanged;
        _da.ConnectionLost += OnDaConnectionLost;
        _ua.RunningChanged += OnUaRunningChanged;
        _ua.ClientsChanged += OnUaClientsChanged;
        _ua.WriteHandler = HandleUaWriteAsync;
    }

    // ---------- 事件 ----------

    public event EventHandler<GatewayRunState>? StateChanged;
    public event EventHandler<IReadOnlyList<TagMapping>>? TagsLiveChanged;
    public event EventHandler<GatewayStatistics>? StatisticsUpdated;
    public event EventHandler<IReadOnlyList<UaClientInfo>>? ClientsChanged;
    public event EventHandler<string>? ErrorOccurred;

    // ---------- 狀態 ----------

    public GatewayConfig Config { get; private set; }

    /// <summary>標籤集合；跨執行緒存取請鎖定 <see cref="TagsLock"/>（WPF 可用 EnableCollectionSynchronization）。</summary>
    public ObservableCollection<TagMapping> Tags { get; }
    public object TagsLock { get; } = new();

    public GatewayStatistics Statistics { get; }
    public bool DaConnected => _da.IsConnected;
    public bool UaRunning => _ua.IsRunning;
    public IReadOnlyList<string> UaEndpointUrls => _ua.EndpointUrls;
    public IReadOnlyList<UaClientInfo> Clients => _ua.Clients;
    public IDaClient DaClient => _da;

    public GatewayRunState State
    {
        get => _state;
        private set
        {
            if (_state == value)
                return;
            _state = value;
            StateChanged?.Invoke(this, value);
        }
    }

    public int TagCount
    {
        get { lock (TagsLock) return Tags.Count; }
    }

    public int ActiveTagCount => _index.Values.Count(t => t.State == TagState.Active);

    // ---------- 設定 ----------

    /// <summary>套用設定並重建標籤集合；若引擎正在運作會立即對帳。</summary>
    public async Task ApplyConfigAsync(GatewayConfig config, CancellationToken cancellationToken = default)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        ConfigStore.Validate(Config);

        var separator = Config.DaSource.BranchSeparator;
        var tags = Config.Tags.Select(t => CreateTag(t, separator)).ToList();

        foreach (var old in _index.Values)
            old.PropertyChanged -= OnTagPropertyChanged;
        _index.Clear();

        lock (TagsLock)
        {
            Tags.Clear();
            foreach (var tag in tags)
            {
                Tags.Add(tag);
                _index[tag.ItemId] = tag;
                tag.PropertyChanged += OnTagPropertyChanged;
            }
        }

        _logger.LogInformation("已套用設定：{Count} 個標籤，DA {ProgId}@{Host}，UA 連接埠 {Port}",
            tags.Count, Config.DaSource.ProgId, Config.DaSource.Host, Config.UaServer.Port);

        if (DaConnected || UaRunning)
            await ReconcileAsync(cancellationToken).ConfigureAwait(false);
        else
            RaiseLiveChanged(tags);
    }

    /// <summary>以目前的標籤集合回填設定物件（供儲存）。</summary>
    public GatewayConfig BuildConfig()
    {
        var config = Config.Clone();
        lock (TagsLock)
            config.Tags = Tags.Select(t => t.ToConfig()).ToList();
        return config;
    }

    // ---------- 啟停 ----------

    /// <summary>一鍵啟動：先啟動 UA（節點先以 BadNotConnected 出現），再連線 DA 並訂閱。</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        State = GatewayRunState.Starting;
        Statistics.Reset();
        Statistics.MarkStarted();
        StartPipeline();

        Exception? uaError = null;
        Exception? daError = null;

        try
        {
            await StartUaCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            uaError = ex;
            _logger.LogError(ex, "OPC UA 伺服器啟動失敗");
            ErrorOccurred?.Invoke(this, "OPC UA 伺服器啟動失敗: " + ex.Message);
        }

        try
        {
            await ConnectDaCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            daError = ex;
            _logger.LogError(ex, "OPC DA 連線失敗");
            ErrorOccurred?.Invoke(this, "OPC DA 連線失敗: " + ex.Message);
        }

        await ReconcileAsync(cancellationToken).ConfigureAwait(false);
        UpdateState();

        if (uaError != null && daError != null)
        {
            StopPipeline();
            Statistics.MarkStopped();
            State = GatewayRunState.Stopped;
            throw new AggregateException("閘道啟動失敗", uaError, daError);
        }
    }

    public async Task StopAsync()
    {
        if (State == GatewayRunState.Stopped)
            return;

        State = GatewayRunState.Stopping;
        try
        {
            await DisconnectDaCoreAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "中斷 OPC DA 時發生錯誤");
        }

        try
        {
            await StopUaCoreAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "停止 OPC UA 時發生錯誤");
        }

        StopPipeline();
        Statistics.MarkStopped();

        foreach (var tag in _index.Values)
            tag.SetState(tag.Enabled ? TagState.Idle : TagState.Disabled, StatusCodes.BadNotConnected);
        RaiseLiveChanged(_index.Values.ToList());

        State = GatewayRunState.Stopped;
        _logger.LogInformation("閘道已停止");
    }

    /// <summary>只連線 DA（例如為了瀏覽項目）。</summary>
    public async Task ConnectDaAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsurePipeline();
        await ConnectDaCoreAsync(cancellationToken).ConfigureAwait(false);
        await ReconcileAsync(cancellationToken).ConfigureAwait(false);
        UpdateState();
    }

    public async Task DisconnectDaAsync()
    {
        await DisconnectDaCoreAsync().ConfigureAwait(false);
        await ReconcileAsync().ConfigureAwait(false);
        UpdateState();
    }

    public async Task StartUaAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsurePipeline();
        await StartUaCoreAsync(cancellationToken).ConfigureAwait(false);
        await ReconcileAsync(cancellationToken).ConfigureAwait(false);
        UpdateState();
    }

    public async Task StopUaAsync()
    {
        await StopUaCoreAsync().ConfigureAwait(false);
        await ReconcileAsync().ConfigureAwait(false);
        UpdateState();
    }

    // ---------- 標籤管理 ----------

    /// <summary>新增標籤（已存在者略過），並在運作中立即對帳。回傳實際新增的標籤。</summary>
    public async Task<IReadOnlyList<TagMapping>> AddTagsAsync(IEnumerable<TagMappingConfig> configs, CancellationToken cancellationToken = default)
    {
        var separator = Config.DaSource.BranchSeparator;
        var added = new List<TagMapping>();

        lock (TagsLock)
        {
            foreach (var cfg in configs)
            {
                if (cfg == null || string.IsNullOrWhiteSpace(cfg.ItemId))
                    continue;
                cfg.ItemId = cfg.ItemId.Trim();
                if (_index.ContainsKey(cfg.ItemId))
                    continue;

                var tag = CreateTag(cfg, separator);
                Tags.Add(tag);
                _index[tag.ItemId] = tag;
                tag.PropertyChanged += OnTagPropertyChanged;
                added.Add(tag);
            }
        }

        if (added.Count > 0)
        {
            _logger.LogInformation("新增 {Count} 個標籤", added.Count);
            if (DaConnected || UaRunning)
                await ReconcileAsync(cancellationToken).ConfigureAwait(false);
            else
                RaiseLiveChanged(added);
        }

        return added;
    }

    public async Task RemoveTagsAsync(IEnumerable<string> itemIds, CancellationToken cancellationToken = default)
    {
        var removed = new List<TagMapping>();
        lock (TagsLock)
        {
            foreach (var itemId in itemIds.Distinct().ToList())
            {
                if (!_index.TryRemove(itemId, out var tag))
                    continue;
                tag.PropertyChanged -= OnTagPropertyChanged;
                Tags.Remove(tag);
                removed.Add(tag);
            }
        }

        if (removed.Count > 0)
        {
            _logger.LogInformation("移除 {Count} 個標籤", removed.Count);
            await ReconcileAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task ClearTagsAsync(CancellationToken cancellationToken = default)
    {
        List<string> keys;
        lock (TagsLock)
            keys = Tags.Select(t => t.ItemId).ToList();
        return RemoveTagsAsync(keys, cancellationToken);
    }

    public TagMapping? FindTag(string itemId) => _index.TryGetValue(itemId, out var tag) ? tag : null;

    // ---------- 對帳 ----------

    /// <summary>把期望狀態（標籤集合）對帳成實際的 UA 節點與 DA 訂閱。可重入呼叫會被序列化。</summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        await _reconcileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReconcileCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "對帳標籤時發生錯誤");
            ErrorOccurred?.Invoke(this, "標籤對帳失敗: " + ex.Message);
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    private async Task ReconcileCoreAsync(CancellationToken cancellationToken)
    {
        TagMapping[] tags;
        lock (TagsLock)
            tags = Tags.ToArray();

        var daConnected = DaConnected;
        var uaRunning = UaRunning;

        // 1. UA 節點：所有標籤都建立節點（停用者標記 BadOutOfService），移除已不存在的節點。
        if (uaRunning)
        {
            var desiredKeys = new HashSet<string>(tags.Select(t => t.ItemId), StringComparer.Ordinal);
            List<string> stale;
            lock (_uaNodesLock)
                stale = _uaNodes.Where(k => !desiredKeys.Contains(k)).ToList();

            foreach (var key in stale)
            {
                _ua.RemoveVariable(key);
                lock (_uaNodesLock)
                    _uaNodes.Remove(key);
            }

            foreach (var tag in tags)
            {
                _ua.AddOrUpdateVariable(new UaVariableDefinition(
                    tag.ItemId, tag.FolderPath, tag.BrowseName, tag.Description, tag.DataType, tag.AllowWrite));
                lock (_uaNodesLock)
                    _uaNodes.Add(tag.ItemId);
            }
        }
        else
        {
            lock (_uaNodesLock)
                _uaNodes.Clear();
        }

        // 2. DA 訂閱：只訂閱啟用的標籤。
        if (daConnected)
        {
            var desired = new HashSet<string>(tags.Where(t => t.Enabled).Select(t => t.ItemId), StringComparer.Ordinal);
            var current = new HashSet<string>(_da.SubscribedItems, StringComparer.Ordinal);

            var toRemove = current.Where(k => !desired.Contains(k)).ToList();
            if (toRemove.Count > 0)
                await _da.UnsubscribeAsync(toRemove, cancellationToken).ConfigureAwait(false);

            var toAdd = desired.Where(k => !current.Contains(k)).ToList();
            if (toAdd.Count > 0)
            {
                var results = await _da.SubscribeAsync(toAdd, cancellationToken).ConfigureAwait(false);
                foreach (var result in results)
                {
                    if (!_index.TryGetValue(result.ItemId, out var tag))
                        continue;

                    if (result.Success)
                    {
                        tag.SetDataType(result.CanonicalType);
                        if (tag.State != TagState.Active)
                            tag.SetState(TagState.WaitingForData, StatusCodes.BadWaitingForInitialData);
                        if (uaRunning)
                        {
                            _ua.AddOrUpdateVariable(new UaVariableDefinition(
                                tag.ItemId, tag.FolderPath, tag.BrowseName, tag.Description, tag.DataType, tag.AllowWrite));
                            _ua.SetStatus(tag.ItemId, tag.UaStatusCode);
                        }
                    }
                    else
                    {
                        var message = result.ErrorText ?? $"0x{result.ErrorCode:X8}";
                        tag.SetState(TagState.Error, StatusCodes.BadConfigurationError, message);
                        _logger.LogWarning("訂閱 {ItemId} 失敗: {Error}", result.ItemId, message);
                        if (uaRunning)
                            _ua.SetStatus(tag.ItemId, StatusCodes.BadConfigurationError);
                    }
                }
            }

            foreach (var tag in tags)
            {
                if (!tag.Enabled)
                {
                    tag.SetState(TagState.Disabled, StatusCodes.BadOutOfService);
                    if (uaRunning) _ua.SetStatus(tag.ItemId, StatusCodes.BadOutOfService);
                }
                else if (tag.State == TagState.Idle && current.Contains(tag.ItemId))
                {
                    tag.SetState(TagState.WaitingForData, StatusCodes.BadWaitingForInitialData);
                    if (uaRunning) _ua.SetStatus(tag.ItemId, StatusCodes.BadWaitingForInitialData);
                }
            }
        }
        else
        {
            foreach (var tag in tags)
            {
                if (tag.Enabled)
                {
                    tag.SetState(TagState.Idle, StatusCodes.BadNotConnected);
                    if (uaRunning) _ua.SetStatus(tag.ItemId, StatusCodes.BadNotConnected);
                }
                else
                {
                    tag.SetState(TagState.Disabled, StatusCodes.BadOutOfService);
                    if (uaRunning) _ua.SetStatus(tag.ItemId, StatusCodes.BadOutOfService);
                }
            }
        }

        RaiseLiveChanged(tags);
        PublishGatewayStatus();
    }

    // ---------- 內部：DA / UA ----------

    private async Task StartUaCoreAsync(CancellationToken cancellationToken)
    {
        if (UaRunning)
            return;
        _logger.LogInformation("正在啟動 OPC UA 伺服器（連接埠 {Port}）", Config.UaServer.Port);
        await _ua.StartAsync(Config.UaServer, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("OPC UA 伺服器已啟動：{Endpoints}", string.Join(", ", _ua.EndpointUrls));
    }

    private async Task StopUaCoreAsync()
    {
        if (!UaRunning)
            return;
        await _ua.StopAsync().ConfigureAwait(false);
        lock (_uaNodesLock)
            _uaNodes.Clear();
        _logger.LogInformation("OPC UA 伺服器已停止");
    }

    private async Task ConnectDaCoreAsync(CancellationToken cancellationToken)
    {
        if (DaConnected)
            return;
        _logger.LogInformation("正在連線 OPC DA：{ProgId}@{Host}", Config.DaSource.ProgId, Config.DaSource.Host);
        await _da.ConnectAsync(Config.DaSource, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("OPC DA 已連線");
    }

    private async Task DisconnectDaCoreAsync()
    {
        if (!DaConnected && _da.ProgId == null)
            return;
        await _da.DisconnectAsync().ConfigureAwait(false);
        _logger.LogInformation("OPC DA 已中斷");
    }

    private void UpdateState()
    {
        if (State == GatewayRunState.Starting || State == GatewayRunState.Stopping)
        {
            State = ComputeState();
            return;
        }
        State = ComputeState();
    }

    private GatewayRunState ComputeState()
    {
        var da = DaConnected;
        var ua = UaRunning;
        if (da && ua) return GatewayRunState.Running;
        if (da || ua) return GatewayRunState.Degraded;
        return GatewayRunState.Stopped;
    }

    // ---------- 資料管線 ----------

    private void EnsurePipeline()
    {
        if (_consumerTask == null)
        {
            Statistics.MarkStarted();
            StartPipeline();
        }
    }

    private void StartPipeline()
    {
        if (_consumerTask != null)
            return;

        _channel = Channel.CreateBounded<DaValue>(new BoundedChannelOptions(Math.Max(1000, Config.Options.ValueQueueCapacity))
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        _consumerCts = new CancellationTokenSource();
        var token = _consumerCts.Token;
        var reader = _channel.Reader;
        _consumerTask = Task.Run(() => ConsumeAsync(reader, token), token);

        var refresh = Math.Max(50, Config.Options.UiRefreshIntervalMs);
        _refreshTimer = new Timer(_ => FlushDirty(), null, refresh, refresh);
        _statsTimer = new Timer(_ => OnStatsTick(), null, 1000, 1000);
    }

    private void StopPipeline()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        _statsTimer?.Dispose();
        _statsTimer = null;

        _channel?.Writer.TryComplete();
        _consumerCts?.Cancel();
        try
        {
            _consumerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // 取消例外可忽略
        }
        _consumerCts?.Dispose();
        _consumerCts = null;
        _consumerTask = null;
        _channel = null;
        _dirty.Clear();
    }

    private async Task ConsumeAsync(ChannelReader<DaValue> reader, CancellationToken token)
    {
        try
        {
            while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var value))
                    ProcessValue(value);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "值處理迴圈異常結束");
        }
    }

    private void ProcessValue(DaValue value)
    {
        if (!_index.TryGetValue(value.ItemId, out var tag) || !tag.Enabled)
            return;

        try
        {
            var normalized = UaTypeMapper.NormalizeValue(value.Value);
            var status = value.Succeeded ? UaStatusMapper.FromDaQuality(value.Quality) : StatusCodes.BadCommunicationError;
            var previousType = tag.DataType;

            tag.SetLiveValue(normalized, value.Quality, value.TimestampUtc, status);
            Statistics.RecordUpdate(DaQuality.IsGood(value.Quality));

            if (_ua.IsRunning)
            {
                if (previousType == null && tag.DataType != null)
                {
                    _ua.AddOrUpdateVariable(new UaVariableDefinition(
                        tag.ItemId, tag.FolderPath, tag.BrowseName, tag.Description, tag.DataType, tag.AllowWrite));
                }
                _ua.UpdateValue(tag.ItemId, normalized, status, value.TimestampUtc);
            }

            _dirty[tag.ItemId] = tag;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "處理 {ItemId} 的值時發生錯誤", value.ItemId);
        }
    }

    private void FlushDirty()
    {
        if (_dirty.IsEmpty)
            return;

        var keys = _dirty.Keys.ToList();
        var batch = new List<TagMapping>(keys.Count);
        foreach (var key in keys)
        {
            if (_dirty.TryRemove(key, out var tag))
                batch.Add(tag);
        }

        if (batch.Count > 0)
            TagsLiveChanged?.Invoke(this, batch);
    }

    private void OnStatsTick()
    {
        try
        {
            Statistics.Tick();
            StatisticsUpdated?.Invoke(this, Statistics);
            PublishGatewayStatus();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "統計更新失敗");
        }
    }

    private void PublishGatewayStatus()
    {
        if (!_ua.IsRunning)
            return;

        _ua.UpdateGatewayStatus(new GatewayStatusSnapshot
        {
            DaConnected = DaConnected,
            DaServer = _da.ProgId == null ? null : $"{_da.ProgId}@{_da.Host}",
            TagCount = TagCount,
            ActiveTagCount = ActiveTagCount,
            UpdatesPerSecond = Statistics.UpdatesPerSecond,
            TotalUpdates = Statistics.TotalUpdates,
            DroppedUpdates = Statistics.DroppedUpdates,
            LastUpdateUtc = Statistics.LastUpdateUtc,
            Uptime = Statistics.Uptime,
            Version = typeof(GatewayEngine).Assembly.GetName().Version?.ToString() ?? "0.0",
            ClientCount = _ua.Clients.Count,
        });
    }

    private void RaiseLiveChanged(IReadOnlyList<TagMapping> tags)
    {
        if (tags.Count > 0)
            TagsLiveChanged?.Invoke(this, tags);
    }

    // ---------- 事件處理 ----------

    private void OnDaValuesChanged(object? sender, IReadOnlyList<DaValue> values)
    {
        var channel = _channel;
        if (channel == null)
            return;

        foreach (var value in values)
        {
            if (channel.Reader.Count >= Config.Options.ValueQueueCapacity)
                Statistics.RecordDropped();
            channel.Writer.TryWrite(value);
        }
    }

    private void OnDaConnectionStateChanged(object? sender, bool connected)
    {
        _logger.LogInformation("OPC DA 連線狀態: {State}", connected ? "已連線" : "已中斷");
        if (!connected)
        {
            foreach (var tag in _index.Values)
            {
                if (tag.Enabled)
                    tag.SetState(TagState.Idle, StatusCodes.BadNotConnected);
            }
            if (_ua.IsRunning)
                _ua.SetAllStatus(StatusCodes.BadNotConnected);
            RaiseLiveChanged(_index.Values.ToList());
            UpdateState();
            PublishGatewayStatus();
            return;
        }

        // 重新連線後群組是新的，必須重新訂閱。
        _ = Task.Run(async () =>
        {
            await ReconcileAsync().ConfigureAwait(false);
            UpdateState();
        });
    }

    private void OnDaConnectionLost(object? sender, string reason)
    {
        _logger.LogWarning("OPC DA 連線遺失: {Reason}", reason);
        ErrorOccurred?.Invoke(this, "OPC DA 連線遺失: " + reason);
    }

    private void OnUaRunningChanged(object? sender, bool running)
    {
        if (!running)
        {
            lock (_uaNodesLock)
                _uaNodes.Clear();
        }
        UpdateState();
    }

    private void OnUaClientsChanged(object? sender, IReadOnlyList<UaClientInfo> clients)
    {
        ClientsChanged?.Invoke(this, clients);
        PublishGatewayStatus();
    }

    private void OnTagPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(TagMapping.Enabled) or nameof(TagMapping.AllowWrite) or nameof(TagMapping.BrowseName)))
            return;

        if (DaConnected || UaRunning)
            _ = Task.Run(() => ReconcileAsync());
        else if (sender is TagMapping tag)
        {
            tag.SetState(tag.Enabled ? TagState.Idle : TagState.Disabled, tag.Enabled ? StatusCodes.BadNotConnected : StatusCodes.BadOutOfService);
            RaiseLiveChanged(new[] { tag });
        }
    }

    private async Task<bool> HandleUaWriteAsync(string key, object? value)
    {
        if (!_index.TryGetValue(key, out var tag))
            return false;
        if (!tag.AllowWrite || !tag.Enabled)
        {
            _logger.LogWarning("拒絕寫入 {ItemId}：未允許寫入", key);
            return false;
        }
        if (!DaConnected)
        {
            _logger.LogWarning("拒絕寫入 {ItemId}：OPC DA 未連線", key);
            return false;
        }

        try
        {
            Statistics.RecordWrite();
            var hresult = await _da.WriteAsync(key, value).ConfigureAwait(false);
            if (hresult != 0)
                _logger.LogWarning("寫入 {ItemId} 失敗，HRESULT 0x{Code:X8}", key, hresult);
            else
                _logger.LogInformation("UA 客戶端寫入 {ItemId} = {Value}", key, value);
            return hresult == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "寫入 {ItemId} 時發生例外", key);
            return false;
        }
    }

    // ---------- 工具 ----------

    private static TagMapping CreateTag(TagMappingConfig cfg, string separator)
    {
        var defaultName = NodeNaming.DefaultBrowseName(cfg.ItemId, separator);
        var browseName = NodeNaming.SanitizeBrowseName(cfg.BrowseName, defaultName);
        var folders = NodeNaming.GetFolderSegments(cfg.ItemId, separator);
        return new TagMapping(cfg.ItemId, folders, browseName, cfg.Description, cfg.Enabled, cfg.AllowWrite);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GatewayEngine));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dispose 時停止閘道失敗");
        }

        _da.ValuesChanged -= OnDaValuesChanged;
        _da.ConnectionStateChanged -= OnDaConnectionStateChanged;
        _da.ConnectionLost -= OnDaConnectionLost;
        _ua.RunningChanged -= OnUaRunningChanged;
        _ua.ClientsChanged -= OnUaClientsChanged;
        _ua.WriteHandler = null;
        foreach (var tag in _index.Values)
            tag.PropertyChanged -= OnTagPropertyChanged;
        _reconcileGate.Dispose();
    }
}
