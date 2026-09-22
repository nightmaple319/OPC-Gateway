using OPCGateway.Core.Configuration;
using OPCGateway.Core.Da;

namespace OPCGateway.Core.Tests.Fakes;

/// <summary>記憶體內的假 DA 用戶端，用來測試引擎的對帳與資料流。</summary>
public sealed class FakeDaClient : IDaClient
{
    private readonly object _sync = new();
    private readonly HashSet<string> _subscribed = new(StringComparer.Ordinal);

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<IReadOnlyList<DaValue>>? ValuesChanged;
    public event EventHandler<string>? ConnectionLost;

    public bool IsConnected { get; private set; }
    public string? ProgId { get; private set; }
    public string? Host { get; private set; }
    public DaServerState ServerState => IsConnected ? DaServerState.Running : DaServerState.Unknown;

    public bool FailConnect { get; set; }
    public HashSet<string> InvalidItems { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Type> ItemTypes { get; } = new(StringComparer.Ordinal);
    public List<(string ItemId, object? Value)> Writes { get; } = new();
    public int WriteResult { get; set; }
    public int SubscribeCalls { get; private set; }
    public Dictionary<string, List<DaBrowseNode>> Tree { get; } = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> SubscribedItems
    {
        get { lock (_sync) return _subscribed.ToList(); }
    }

    public Task ConnectAsync(DaSourceConfig config, CancellationToken cancellationToken = default)
    {
        if (FailConnect)
            throw new IOException("connect failed");
        ProgId = config.ProgId;
        Host = config.Host;
        IsConnected = true;
        ConnectionStateChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        if (IsConnected)
        {
            IsConnected = false;
            lock (_sync) _subscribed.Clear();
            ConnectionStateChanged?.Invoke(this, false);
        }
        ProgId = null;
        return Task.CompletedTask;
    }

    public void SimulateConnectionLost()
    {
        IsConnected = false;
        lock (_sync) _subscribed.Clear();
        ConnectionLost?.Invoke(this, "simulated");
        ConnectionStateChanged?.Invoke(this, false);
    }

    public void SimulateReconnected()
    {
        IsConnected = true;
        ConnectionStateChanged?.Invoke(this, true);
    }

    public void Push(string itemId, object? value, short quality = DaQuality.Good)
    {
        ValuesChanged?.Invoke(this, new[] { new DaValue(itemId, value, quality, DateTime.UtcNow) });
    }

    public Task<IReadOnlyList<DaServerInfo>> EnumerateServersAsync(string host, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DaServerInfo> list = new[] { new DaServerInfo("Fake.OPC.Server.1", host, "Fake", Guid.Empty) };
        return Task.FromResult(list);
    }

    public Task<DaBrowseResult> BrowseAsync(string? parentItemId, int maxChildren, CancellationToken cancellationToken = default)
    {
        var key = parentItemId ?? string.Empty;
        var nodes = Tree.TryGetValue(key, out var list) ? list : new List<DaBrowseNode>();
        return Task.FromResult(new DaBrowseResult(nodes.Take(maxChildren).ToList(), nodes.Count > maxChildren));
    }

    public Task<IReadOnlyList<DaBrowseNode>> SearchItemsAsync(string term, int maxResults, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DaBrowseNode> result = Tree.Values.SelectMany(x => x)
            .Where(n => n.IsItem && n.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            .Take(maxResults).ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<DaBrowseNode>> ListItemsRecursiveAsync(string? parentItemId, int maxResults, CancellationToken cancellationToken = default)
    {
        var result = new List<DaBrowseNode>();
        Collect(parentItemId ?? string.Empty, result, maxResults);
        return Task.FromResult<IReadOnlyList<DaBrowseNode>>(result);
    }

    private void Collect(string key, List<DaBrowseNode> result, int max)
    {
        if (!Tree.TryGetValue(key, out var children))
            return;
        foreach (var child in children)
        {
            if (result.Count >= max)
                return;
            if (child.IsItem)
                result.Add(child);
            else
                Collect(child.ItemId, result, max);
        }
    }

    public Task<IReadOnlyList<DaItemResult>> SubscribeAsync(IReadOnlyList<string> itemIds, CancellationToken cancellationToken = default)
    {
        SubscribeCalls++;
        var results = new List<DaItemResult>();
        lock (_sync)
        {
            foreach (var id in itemIds)
            {
                if (InvalidItems.Contains(id))
                {
                    results.Add(new DaItemResult(id, false, unchecked((int)0xC0040007), "Unknown ItemId", null));
                    continue;
                }
                _subscribed.Add(id);
                ItemTypes.TryGetValue(id, out var type);
                results.Add(new DaItemResult(id, true, 0, null, type));
            }
        }
        return Task.FromResult<IReadOnlyList<DaItemResult>>(results);
    }

    public Task UnsubscribeAsync(IReadOnlyList<string> itemIds, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            foreach (var id in itemIds)
                _subscribed.Remove(id);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DaValue>> ReadAsync(IReadOnlyList<string> itemIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DaValue>>(Array.Empty<DaValue>());

    public Task<int> WriteAsync(string itemId, object? value, CancellationToken cancellationToken = default)
    {
        Writes.Add((itemId, value));
        return Task.FromResult(WriteResult);
    }

    public Task<DaServerStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<DaServerStatus?>(new DaServerStatus { State = ServerState });

    public void Dispose() { }
}
