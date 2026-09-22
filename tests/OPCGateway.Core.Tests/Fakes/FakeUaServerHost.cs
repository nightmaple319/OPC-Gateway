using Opc.Ua;
using OPCGateway.Core.Configuration;
using OPCGateway.Core.Ua;

namespace OPCGateway.Core.Tests.Fakes;

/// <summary>記錄節點操作的假 UA 主機。</summary>
public sealed class FakeUaServerHost : IUaServerHost
{
    private readonly object _sync = new();

    public event EventHandler<bool>? RunningChanged;
    public event EventHandler<IReadOnlyList<UaClientInfo>>? ClientsChanged;

    public bool IsRunning { get; private set; }
    public IReadOnlyList<string> EndpointUrls { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<UaClientInfo> Clients { get; private set; } = Array.Empty<UaClientInfo>();
    public Func<string, object?, Task<bool>>? WriteHandler { get; set; }

    public bool FailStart { get; set; }
    public Dictionary<string, UaVariableDefinition> Variables { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, (object? Value, uint Status, DateTime Timestamp)> Values { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, uint> Status { get; } = new(StringComparer.Ordinal);
    public List<GatewayStatusSnapshot> Snapshots { get; } = new();
    public int RemovedCount { get; private set; }

    public Task StartAsync(UaServerConfig config, CancellationToken cancellationToken = default)
    {
        if (FailStart)
            throw new InvalidOperationException("start failed");
        IsRunning = true;
        EndpointUrls = new[] { $"opc.tcp://localhost:{config.Port}" };
        RunningChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsRunning)
            return Task.CompletedTask;
        IsRunning = false;
        lock (_sync)
        {
            Variables.Clear();
            Values.Clear();
            Status.Clear();
        }
        EndpointUrls = Array.Empty<string>();
        RunningChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public void SimulateClients(params UaClientInfo[] clients)
    {
        Clients = clients;
        ClientsChanged?.Invoke(this, clients);
    }

    public void AddOrUpdateVariable(UaVariableDefinition definition)
    {
        if (!IsRunning) return;
        lock (_sync)
        {
            Variables[definition.Key] = definition;
            if (!Status.ContainsKey(definition.Key))
                Status[definition.Key] = StatusCodes.BadWaitingForInitialData;
        }
    }

    public void RemoveVariable(string key)
    {
        if (!IsRunning) return;
        lock (_sync)
        {
            if (Variables.Remove(key))
                RemovedCount++;
            Values.Remove(key);
            Status.Remove(key);
        }
    }

    public void UpdateValue(string key, object? value, uint statusCode, DateTime timestampUtc)
    {
        if (!IsRunning) return;
        lock (_sync)
        {
            Values[key] = (value, statusCode, timestampUtc);
            Status[key] = statusCode;
        }
    }

    public void SetStatus(string key, uint statusCode)
    {
        if (!IsRunning) return;
        lock (_sync) Status[key] = statusCode;
    }

    public void SetAllStatus(uint statusCode)
    {
        if (!IsRunning) return;
        lock (_sync)
        {
            foreach (var key in Status.Keys.ToList())
                Status[key] = statusCode;
        }
    }

    public void UpdateGatewayStatus(GatewayStatusSnapshot snapshot)
    {
        lock (_sync) Snapshots.Add(snapshot);
    }

    public uint StatusOf(string key)
    {
        lock (_sync) return Status.TryGetValue(key, out var s) ? s : 0xFFFFFFFF;
    }

    public bool HasVariable(string key)
    {
        lock (_sync) return Variables.ContainsKey(key);
    }

    public (object? Value, uint Status, DateTime Timestamp)? ValueOf(string key)
    {
        lock (_sync) return Values.TryGetValue(key, out var v) ? v : null;
    }

    public void Dispose() { }
}
