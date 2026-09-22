namespace OPCGateway.Core.Ua;

/// <summary>要在 UA 位址空間建立或更新的變數節點定義。</summary>
public sealed class UaVariableDefinition
{
    public UaVariableDefinition(string key, string[] folderPath, string browseName, string? description, Type? clrType, bool allowWrite)
    {
        Key = key;
        FolderPath = folderPath;
        BrowseName = browseName;
        Description = description;
        ClrType = clrType;
        AllowWrite = allowWrite;
    }

    /// <summary>唯一鍵，同時是 NodeId 的字串識別碼（ns=X;s=Key）。</summary>
    public string Key { get; }

    /// <summary>父資料夾路徑片段；空陣列表示直接放在根資料夾。</summary>
    public string[] FolderPath { get; }

    public string BrowseName { get; }
    public string? Description { get; }
    public Type? ClrType { get; }
    public bool AllowWrite { get; }
}

/// <summary>已連線的 UA 客戶端資訊（來自 SessionManager）。</summary>
public sealed class UaClientInfo
{
    public string SessionId { get; set; } = string.Empty;
    public string? SessionName { get; set; }
    public string? ApplicationName { get; set; }
    public string? ApplicationUri { get; set; }
    public string? EndpointUrl { get; set; }
    public string? SecurityMode { get; set; }
    public string? UserIdentity { get; set; }
    public DateTime ConnectedAtUtc { get; set; }
    public DateTime LastContactUtc { get; set; }
    public int SubscriptionCount { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(ApplicationName) ? (SessionName ?? SessionId) : ApplicationName!;
}

/// <summary>閘道狀態快照，會同步寫到 UA 的 Status 資料夾供客戶端監看。</summary>
public sealed class GatewayStatusSnapshot
{
    public bool DaConnected { get; set; }
    public string? DaServer { get; set; }
    public int TagCount { get; set; }
    public int ActiveTagCount { get; set; }
    public double UpdatesPerSecond { get; set; }
    public long TotalUpdates { get; set; }
    public long DroppedUpdates { get; set; }
    public DateTime? LastUpdateUtc { get; set; }
    public TimeSpan Uptime { get; set; }
    public string Version { get; set; } = string.Empty;
    public int ClientCount { get; set; }
}
