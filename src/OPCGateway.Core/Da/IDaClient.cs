using OPCGateway.Core.Configuration;

namespace OPCGateway.Core.Da;

/// <summary>
/// OPC DA 用戶端抽象。所有方法皆可在任意執行緒呼叫；事件在背景執行緒觸發，
/// 呼叫端（例如 UI）需自行 marshal。
/// </summary>
public interface IDaClient : IDisposable
{
    /// <summary>連線狀態改變（含自動重連）。</summary>
    event EventHandler<bool>? ConnectionStateChanged;

    /// <summary>伺服器推送的值變更（一次可能多筆）。</summary>
    event EventHandler<IReadOnlyList<DaValue>>? ValuesChanged;

    /// <summary>伺服器要求關閉或連線遺失時的說明訊息。</summary>
    event EventHandler<string>? ConnectionLost;

    bool IsConnected { get; }
    string? ProgId { get; }
    string? Host { get; }
    DaServerState ServerState { get; }

    /// <summary>目前已訂閱的 ItemId。</summary>
    IReadOnlyCollection<string> SubscribedItems { get; }

    /// <summary>連線並建立群組；失敗會擲出例外。成功後啟用健康檢查與自動重連。</summary>
    Task ConnectAsync(DaSourceConfig config, CancellationToken cancellationToken = default);

    /// <summary>主動中斷並停用自動重連。</summary>
    Task DisconnectAsync();

    Task<IReadOnlyList<DaServerInfo>> EnumerateServersAsync(string host, CancellationToken cancellationToken = default);

    /// <summary>瀏覽指定資料夾（null 為根）。</summary>
    Task<DaBrowseResult> BrowseAsync(string? parentItemId, int maxChildren, CancellationToken cancellationToken = default);

    /// <summary>遞迴瀏覽並回傳名稱包含 <paramref name="term"/> 的項目 ItemId。</summary>
    Task<IReadOnlyList<DaBrowseNode>> SearchItemsAsync(string term, int maxResults, CancellationToken cancellationToken = default);

    /// <summary>遞迴列出指定資料夾下的所有項目。</summary>
    Task<IReadOnlyList<DaBrowseNode>> ListItemsRecursiveAsync(string? parentItemId, int maxResults, CancellationToken cancellationToken = default);

    /// <summary>批次訂閱；已訂閱的項目視為成功。</summary>
    Task<IReadOnlyList<DaItemResult>> SubscribeAsync(IReadOnlyList<string> itemIds, CancellationToken cancellationToken = default);

    Task UnsubscribeAsync(IReadOnlyList<string> itemIds, CancellationToken cancellationToken = default);

    /// <summary>從裝置同步讀取目前值。</summary>
    Task<IReadOnlyList<DaValue>> ReadAsync(IReadOnlyList<string> itemIds, CancellationToken cancellationToken = default);

    /// <summary>寫入值；回傳 HRESULT（0 表示成功）。</summary>
    Task<int> WriteAsync(string itemId, object? value, CancellationToken cancellationToken = default);

    Task<DaServerStatus?> GetStatusAsync(CancellationToken cancellationToken = default);
}
