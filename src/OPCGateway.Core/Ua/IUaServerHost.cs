using OPCGateway.Core.Configuration;

namespace OPCGateway.Core.Ua;

/// <summary>
/// 內建 OPC UA 伺服器的抽象。節點操作皆為同步且執行緒安全；伺服器未啟動時節點操作為 no-op。
/// </summary>
public interface IUaServerHost : IDisposable
{
    event EventHandler<bool>? RunningChanged;
    event EventHandler<IReadOnlyList<UaClientInfo>>? ClientsChanged;

    bool IsRunning { get; }
    IReadOnlyList<string> EndpointUrls { get; }
    IReadOnlyList<UaClientInfo> Clients { get; }

    /// <summary>UA 客戶端寫入節點時的處理函式；回傳 true 表示已成功寫回來源。</summary>
    Func<string, object?, Task<bool>>? WriteHandler { get; set; }

    Task StartAsync(UaServerConfig config, CancellationToken cancellationToken = default);
    Task StopAsync();

    void AddOrUpdateVariable(UaVariableDefinition definition);
    void RemoveVariable(string key);
    void UpdateValue(string key, object? value, uint statusCode, DateTime timestampUtc);
    void SetStatus(string key, uint statusCode);
    void SetAllStatus(uint statusCode);
    void UpdateGatewayStatus(GatewayStatusSnapshot snapshot);
}
