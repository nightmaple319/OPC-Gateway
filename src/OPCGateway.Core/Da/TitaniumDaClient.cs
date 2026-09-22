using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPCGateway.Core.Configuration;
using TitaniumAS.Opc.Client.Common;
using TitaniumAS.Opc.Client.Da;
using TitaniumAS.Opc.Client.Da.Browsing;

namespace OPCGateway.Core.Da;

/// <summary>
/// 以 TitaniumAS.Opc.Client 實作的 OPC DA 用戶端。
/// 所有 COM 呼叫都在 <see cref="_com"/> 鎖內序列化並於執行緒集區執行；
/// 具備健康檢查（GetStatus）、伺服器關閉事件與自動重連。
/// </summary>
public sealed class TitaniumDaClient : IDaClient
{
    private const string GroupName = "OPCGateway";
    private const int MaxBrowseDepth = 16;

    private readonly ILogger<TitaniumDaClient> _logger;
    private readonly object _com = new();
    private readonly Dictionary<string, OpcDaItem> _items = new(StringComparer.Ordinal);

    private OpcDaServer? _server;
    private OpcDaGroup? _group;
    private DaSourceConfig? _config;
    private Timer? _healthTimer;
    private Timer? _reconnectTimer;
    private volatile bool _isConnected;
    private volatile bool _reconnectEnabled;
    private int _reconnecting;
    private int _handlingLoss;
    private bool _disposed;

    public TitaniumDaClient(ILogger<TitaniumDaClient>? logger = null)
    {
        _logger = logger ?? NullLogger<TitaniumDaClient>.Instance;
    }

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<IReadOnlyList<DaValue>>? ValuesChanged;
    public event EventHandler<string>? ConnectionLost;

    public bool IsConnected => _isConnected;
    public string? ProgId => _config?.ProgId;
    public string? Host => _config?.Host;
    public DaServerState ServerState { get; private set; } = DaServerState.Unknown;

    public IReadOnlyCollection<string> SubscribedItems
    {
        get { lock (_com) return _items.Keys.ToList(); }
    }

    // ---------- 連線 ----------

    public Task ConnectAsync(DaSourceConfig config, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.ProgId)) throw new ArgumentException("ProgID 不可為空", nameof(config));

        _config = config;
        _reconnectEnabled = true;
        StopReconnectTimer();
        return Task.Run(() => ConnectCore(config), cancellationToken);
    }

    private void ConnectCore(DaSourceConfig config)
    {
        var host = string.IsNullOrWhiteSpace(config.Host) ? "localhost" : config.Host.Trim();
        var progId = config.ProgId.Trim();

        lock (_com)
        {
            CleanupConnectionCore();

            var server = new OpcDaServer(UrlBuilder.Build(progId, host));
            var connectTask = Task.Run(() => server.Connect());
            bool completed;
            try
            {
                completed = connectTask.Wait(TimeSpan.FromSeconds(Math.Max(1, config.ConnectTimeoutSeconds)));
            }
            catch (AggregateException ex)
            {
                TryDispose(server);
                throw ex.GetBaseException();
            }

            if (!completed)
            {
                TryDispose(server);
                throw new TimeoutException($"連線 {progId}@{host} 逾時（{config.ConnectTimeoutSeconds} 秒）。請確認伺服器已啟動且 DCOM 設定正確。");
            }

            try { server.ClientName = "OPC Gateway"; } catch { /* 部分伺服器不支援 */ }

            var state = new OpcDaGroupState
            {
                UpdateRate = TimeSpan.FromMilliseconds(Math.Max(50, config.UpdateRateMs)),
                IsActive = true,
                PercentDeadband = config.PercentDeadband > 0 ? config.PercentDeadband : (float?)null,
            };

            var group = server.AddGroup(GroupName, state);
            group.ValuesChanged += OnGroupValuesChanged;
            try
            {
                if (!group.IsSubscribed)
                    group.IsSubscribed = true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "設定群組訂閱旗標失敗（可能已由事件訂閱自動啟用）");
            }

            server.Shutdown += OnServerShutdown;
            server.ConnectionStateChanged += OnServerConnectionStateChanged;

            _server = server;
            _group = group;
            _items.Clear();
            ServerState = DaServerState.Running;
            _isConnected = true;
        }

        StartHealthTimer(config);
        _logger.LogInformation("已連線 OPC DA 伺服器 {ProgId}@{Host}，更新頻率 {Rate} ms", progId, host, config.UpdateRateMs);
        ConnectionStateChanged?.Invoke(this, true);
    }

    public Task DisconnectAsync()
    {
        _reconnectEnabled = false;
        StopReconnectTimer();
        StopHealthTimer();

        return Task.Run(() =>
        {
            bool wasConnected;
            lock (_com)
            {
                wasConnected = _isConnected;
                CleanupConnectionCore();
            }
            _config = null;
            if (wasConnected)
            {
                _logger.LogInformation("已中斷 OPC DA 連線");
                ConnectionStateChanged?.Invoke(this, false);
            }
        });
    }

    /// <summary>必須在 <see cref="_com"/> 鎖內呼叫。</summary>
    private void CleanupConnectionCore()
    {
        var server = _server;
        var group = _group;
        _server = null;
        _group = null;
        _items.Clear();
        _isConnected = false;
        ServerState = DaServerState.Unknown;

        if (group != null)
        {
            try { group.ValuesChanged -= OnGroupValuesChanged; } catch { /* ignore */ }
        }

        if (server != null)
        {
            try { server.Shutdown -= OnServerShutdown; } catch { /* ignore */ }
            try { server.ConnectionStateChanged -= OnServerConnectionStateChanged; } catch { /* ignore */ }
            if (group != null)
            {
                try { server.RemoveGroup(group); } catch { /* 伺服器可能已離線 */ }
            }
            try { if (server.IsConnected) server.Disconnect(); } catch { /* ignore */ }
            TryDispose(server);
        }
    }

    private static void TryDispose(IDisposable? disposable)
    {
        try { disposable?.Dispose(); } catch { /* ignore */ }
    }

    // ---------- 健康檢查與重連 ----------

    private void StartHealthTimer(DaSourceConfig config)
    {
        StopHealthTimer();
        var interval = TimeSpan.FromSeconds(Math.Max(1, config.HealthCheckIntervalSeconds));
        _healthTimer = new Timer(_ => CheckHealth(), null, interval, interval);
    }

    private void StopHealthTimer()
    {
        _healthTimer?.Dispose();
        _healthTimer = null;
    }

    private void CheckHealth()
    {
        if (!_isConnected)
            return;

        string? failure = null;
        try
        {
            OpcDaServerStatus? status;
            lock (_com)
            {
                if (_server == null || !_isConnected)
                    return;
                status = _server.GetStatus();
            }

            if (status == null)
            {
                failure = "GetStatus 未回傳狀態";
            }
            else
            {
                var state = (DaServerState)(int)status.ServerState;
                ServerState = state;
                if (state != DaServerState.Running && state != DaServerState.Test)
                    failure = $"伺服器狀態為 {state}";
            }
        }
        catch (Exception ex)
        {
            failure = "GetStatus 失敗: " + ex.Message;
        }

        if (failure != null)
            HandleConnectionLost(failure);
    }

    private void HandleConnectionLost(string reason)
    {
        if (Interlocked.Exchange(ref _handlingLoss, 1) != 0)
            return;

        try
        {
            bool wasConnected;
            lock (_com)
            {
                wasConnected = _isConnected;
                if (wasConnected)
                    CleanupConnectionCore();
            }

            StopHealthTimer();

            if (wasConnected)
            {
                _logger.LogWarning("OPC DA 連線遺失: {Reason}", reason);
                ConnectionLost?.Invoke(this, reason);
                ConnectionStateChanged?.Invoke(this, false);
            }

            if (_reconnectEnabled && _config != null)
                StartReconnectTimer(_config);
        }
        finally
        {
            Interlocked.Exchange(ref _handlingLoss, 0);
        }
    }

    private void StartReconnectTimer(DaSourceConfig config)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, config.ReconnectIntervalSeconds));
        lock (_com)
        {
            if (_reconnectTimer == null)
                _reconnectTimer = new Timer(_ => TryReconnect(), null, interval, Timeout.InfiniteTimeSpan);
            else
                _reconnectTimer.Change(interval, Timeout.InfiniteTimeSpan);
        }
        _logger.LogInformation("{Seconds} 秒後嘗試重新連線 OPC DA", config.ReconnectIntervalSeconds);
    }

    private void StopReconnectTimer()
    {
        lock (_com)
        {
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
        }
    }

    private void TryReconnect()
    {
        var config = _config;
        if (!_reconnectEnabled || _isConnected || config == null || _disposed)
            return;
        if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0)
            return;

        try
        {
            ConnectCore(config);
            _logger.LogInformation("OPC DA 已重新連線");
            StopReconnectTimer();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("重新連線 OPC DA 失敗: {Message}", ex.Message);
            if (_reconnectEnabled)
            {
                var interval = TimeSpan.FromSeconds(Math.Max(1, config.ReconnectIntervalSeconds));
                lock (_com)
                    _reconnectTimer?.Change(interval, Timeout.InfiniteTimeSpan);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _reconnecting, 0);
        }
    }

    private void OnServerShutdown(object? sender, OpcShutdownEventArgs e)
    {
        var reason = string.IsNullOrWhiteSpace(e?.Reason) ? "伺服器要求關閉" : $"伺服器要求關閉: {e!.Reason}";
        Task.Run(() => HandleConnectionLost(reason));
    }

    private void OnServerConnectionStateChanged(object? sender, OpcDaServerConnectionStateChangedEventArgs e)
    {
        if (e != null && !e.IsConnected && _isConnected)
            Task.Run(() => HandleConnectionLost("COM 連線已釋放"));
    }

    // ---------- 資料 ----------

    private void OnGroupValuesChanged(object? sender, OpcDaItemValuesChangedEventArgs e)
    {
        var values = e?.Values;
        if (values == null || values.Length == 0)
            return;

        var list = new List<DaValue>(values.Length);
        foreach (var value in values)
        {
            if (value?.Item == null)
                continue;
            try
            {
                int code = value.Error;
                list.Add(new DaValue(
                    value.Item.ItemId,
                    value.Value,
                    (short)value.Quality,
                    value.Timestamp.UtcDateTime,
                    value.Error.Succeeded,
                    code));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "轉換 DA 值失敗");
            }
        }

        if (list.Count > 0)
            ValuesChanged?.Invoke(this, list);
    }

    // ---------- 列舉與瀏覽 ----------

    public Task<IReadOnlyList<DaServerInfo>> EnumerateServersAsync(string host, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var target = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
        return Task.Run<IReadOnlyList<DaServerInfo>>(() =>
        {
            var enumerator = new OpcServerEnumeratorAuto(ComProxyBlanket.Default);
            var descriptions = enumerator.Enumerate(target, OpcServerCategory.OpcDaServers);
            var result = new List<DaServerInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var description in descriptions)
            {
                if (description == null || string.IsNullOrWhiteSpace(description.ProgId))
                    continue;
                if (!seen.Add(description.ProgId))
                    continue;
                result.Add(new DaServerInfo(description.ProgId, target, description.UserType, description.CLSID));
            }
            _logger.LogInformation("在 {Host} 找到 {Count} 個 OPC DA 伺服器", target, result.Count);
            return result.OrderBy(r => r.ProgId, StringComparer.OrdinalIgnoreCase).ToList();
        }, cancellationToken);
    }

    public Task<DaBrowseResult> BrowseAsync(string? parentItemId, int maxChildren, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_com)
            {
                var server = RequireServer();
                var browser = new OpcDaBrowserAuto(server);
                var elements = browser.GetElements(string.IsNullOrEmpty(parentItemId) ? null : parentItemId, null, null) ?? Array.Empty<OpcDaBrowseElement>();

                var nodes = new List<DaBrowseNode>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var truncated = false;
                foreach (var element in elements)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (element == null || string.IsNullOrEmpty(element.ItemId) || !seen.Add(element.ItemId))
                        continue;
                    if (nodes.Count >= Math.Max(1, maxChildren))
                    {
                        truncated = true;
                        break;
                    }
                    nodes.Add(ToNode(element));
                }

                var ordered = nodes.OrderByDescending(n => n.HasChildren).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
                return new DaBrowseResult(ordered, truncated);
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<DaBrowseNode>> SearchItemsAsync(string term, int maxResults, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term))
            return Task.FromResult<IReadOnlyList<DaBrowseNode>>(Array.Empty<DaBrowseNode>());

        var needle = term.Trim();
        return Task.Run<IReadOnlyList<DaBrowseNode>>(() =>
        {
            lock (_com)
            {
                var server = RequireServer();
                var browser = new OpcDaBrowserAuto(server);
                var results = new List<DaBrowseNode>();
                Walk(browser, null, results, Math.Max(1, maxResults), 0, cancellationToken,
                    node => node.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                            || node.ItemId.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
                return results;
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<DaBrowseNode>> ListItemsRecursiveAsync(string? parentItemId, int maxResults, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<DaBrowseNode>>(() =>
        {
            lock (_com)
            {
                var server = RequireServer();
                var browser = new OpcDaBrowserAuto(server);
                var results = new List<DaBrowseNode>();
                Walk(browser, string.IsNullOrEmpty(parentItemId) ? null : parentItemId, results, Math.Max(1, maxResults), 0, cancellationToken, _ => true);
                return results;
            }
        }, cancellationToken);
    }

    private static void Walk(OpcDaBrowserAuto browser, string? parent, List<DaBrowseNode> results, int max, int depth,
        CancellationToken cancellationToken, Func<DaBrowseNode, bool> predicate)
    {
        if (depth > MaxBrowseDepth || results.Count >= max)
            return;

        OpcDaBrowseElement[] elements;
        try
        {
            elements = browser.GetElements(parent, null, null) ?? Array.Empty<OpcDaBrowseElement>();
        }
        catch
        {
            return;
        }

        foreach (var element in elements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (element == null || string.IsNullOrEmpty(element.ItemId))
                continue;
            if (results.Count >= max)
                return;

            var node = ToNode(element);
            if (node.IsItem && predicate(node))
                results.Add(node);
            if (node.HasChildren)
                Walk(browser, node.ItemId, results, max, depth + 1, cancellationToken, predicate);
        }
    }

    private static DaBrowseNode ToNode(OpcDaBrowseElement element)
    {
        var name = string.IsNullOrWhiteSpace(element.Name) ? element.ItemId : element.Name;
        return new DaBrowseNode(name, element.ItemId, element.HasChildren, element.IsItem);
    }

    // ---------- 訂閱 ----------

    public Task<IReadOnlyList<DaItemResult>> SubscribeAsync(IReadOnlyList<string> itemIds, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<DaItemResult>>(() =>
        {
            var results = new List<DaItemResult>();
            lock (_com)
            {
                var group = RequireGroup();
                var definitions = new List<OpcDaItemDefinition>();
                var pending = new List<string>();

                foreach (var itemId in itemIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
                {
                    if (_items.TryGetValue(itemId, out var existing))
                    {
                        results.Add(new DaItemResult(itemId, true, 0, null, existing.CanonicalDataType));
                        continue;
                    }
                    definitions.Add(new OpcDaItemDefinition { ItemId = itemId, IsActive = true });
                    pending.Add(itemId);
                }

                if (definitions.Count == 0)
                    return results;

                var added = group.AddItems(definitions);
                for (var i = 0; i < pending.Count; i++)
                {
                    var itemId = pending[i];
                    var result = i < added.Length ? added[i] : null;
                    if (result != null && result.Error.Succeeded && result.Item != null)
                    {
                        _items[itemId] = result.Item;
                        results.Add(new DaItemResult(itemId, true, 0, null, result.Item.CanonicalDataType));
                    }
                    else
                    {
                        int code = result?.Error ?? new HRESULT(unchecked((int)0x80004005));
                        var text = result?.Error.ToString() ?? "無回傳結果";
                        results.Add(new DaItemResult(itemId, false, code, text, null));
                    }
                }
            }

            var ok = results.Count(r => r.Success);
            _logger.LogInformation("訂閱 {Ok}/{Total} 個 OPC DA 項目", ok, results.Count);
            return results;
        }, cancellationToken);
    }

    public Task UnsubscribeAsync(IReadOnlyList<string> itemIds, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_com)
            {
                if (_group == null)
                    return;
                var items = new List<OpcDaItem>();
                foreach (var itemId in itemIds)
                {
                    if (_items.TryGetValue(itemId, out var item))
                    {
                        items.Add(item);
                        _items.Remove(itemId);
                    }
                }
                if (items.Count > 0)
                {
                    try { _group.RemoveItems(items); }
                    catch (Exception ex) { _logger.LogDebug(ex, "移除 OPC DA 項目失敗"); }
                }
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<DaValue>> ReadAsync(IReadOnlyList<string> itemIds, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<DaValue>>(() =>
        {
            lock (_com)
            {
                var group = RequireGroup();
                var items = itemIds.Select(id => _items.TryGetValue(id, out var item) ? item : null).Where(i => i != null).Cast<OpcDaItem>().ToList();
                if (items.Count == 0)
                    return Array.Empty<DaValue>();
                var values = group.Read(items, OpcDaDataSource.Device) ?? Array.Empty<OpcDaItemValue>();
                var result = new List<DaValue>();
                foreach (var value in values)
                {
                    if (value?.Item == null) continue;
                    int code = value.Error;
                    result.Add(new DaValue(value.Item.ItemId, value.Value, (short)value.Quality, value.Timestamp.UtcDateTime, value.Error.Succeeded, code));
                }
                return result;
            }
        }, cancellationToken);
    }

    public Task<int> WriteAsync(string itemId, object? value, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_com)
            {
                var group = RequireGroup();
                if (!_items.TryGetValue(itemId, out var item))
                    return HRESULT.E_INVALIDARG;
                var results = group.Write(new[] { item }, new[] { value! });
                if (results == null || results.Length == 0)
                    return HRESULT.E_FAIL;
                int code = results[0];
                return code;
            }
        }, cancellationToken);
    }

    public Task<DaServerStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<DaServerStatus?>(() =>
        {
            lock (_com)
            {
                if (_server == null)
                    return null;
                var status = _server.GetStatus();
                if (status == null)
                    return null;
                return new DaServerStatus
                {
                    State = (DaServerState)(int)status.ServerState,
                    VendorInfo = status.VendorInfo,
                    StartTimeUtc = status.StartTime.UtcDateTime,
                    CurrentTimeUtc = status.CurrentTime.UtcDateTime,
                    Version = status.Version?.ToString(),
                    GroupCount = status.GroupCount,
                };
            }
        }, cancellationToken);
    }

    // ---------- 工具 ----------

    private OpcDaServer RequireServer()
    {
        if (_server == null || !_isConnected)
            throw new InvalidOperationException("OPC DA 尚未連線");
        return _server;
    }

    private OpcDaGroup RequireGroup()
    {
        if (_group == null || !_isConnected)
            throw new InvalidOperationException("OPC DA 尚未連線");
        return _group;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TitaniumDaClient));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _reconnectEnabled = false;
        StopReconnectTimer();
        StopHealthTimer();
        lock (_com)
            CleanupConnectionCore();
    }
}
