namespace OPCGateway.Core.Da;

/// <summary>一筆 DA 值（值、品質、時間戳）。時間戳一律為 UTC。</summary>
public sealed class DaValue
{
    public DaValue(string itemId, object? value, short quality, DateTime timestampUtc, bool succeeded = true, int errorCode = 0)
    {
        ItemId = itemId;
        Value = value;
        Quality = quality;
        TimestampUtc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime();
        Succeeded = succeeded;
        ErrorCode = errorCode;
    }

    public string ItemId { get; }
    public object? Value { get; }
    public short Quality { get; }
    public DateTime TimestampUtc { get; }
    public bool Succeeded { get; }
    public int ErrorCode { get; }
}

/// <summary>瀏覽結果中的一個節點（資料夾或項目）。</summary>
public sealed class DaBrowseNode
{
    public DaBrowseNode(string name, string itemId, bool hasChildren, bool isItem)
    {
        Name = name;
        ItemId = itemId;
        HasChildren = hasChildren;
        IsItem = isItem;
    }

    /// <summary>伺服器提供的顯示名稱（不是用分隔符號切出來的）。</summary>
    public string Name { get; }
    public string ItemId { get; }
    public bool HasChildren { get; }
    public bool IsItem { get; }
}

public sealed class DaBrowseResult
{
    public DaBrowseResult(IReadOnlyList<DaBrowseNode> nodes, bool truncated)
    {
        Nodes = nodes;
        Truncated = truncated;
    }

    public IReadOnlyList<DaBrowseNode> Nodes { get; }
    public bool Truncated { get; }
}

/// <summary>OPCEnum 列舉到的伺服器。</summary>
public sealed class DaServerInfo
{
    public DaServerInfo(string progId, string host, string? description, Guid clsid)
    {
        ProgId = progId;
        Host = host;
        Description = description;
        Clsid = clsid;
    }

    public string ProgId { get; }
    public string Host { get; }
    public string? Description { get; }
    public Guid Clsid { get; }

    public override string ToString() => ProgId;
}

/// <summary>訂閱單一項目的結果。</summary>
public sealed class DaItemResult
{
    public DaItemResult(string itemId, bool success, int errorCode, string? errorText, Type? canonicalType)
    {
        ItemId = itemId;
        Success = success;
        ErrorCode = errorCode;
        ErrorText = errorText;
        CanonicalType = canonicalType;
    }

    public string ItemId { get; }
    public bool Success { get; }
    public int ErrorCode { get; }
    public string? ErrorText { get; }

    /// <summary>伺服器宣告的原生資料型別（CLR 型別）。</summary>
    public Type? CanonicalType { get; }
}

public enum DaServerState
{
    Unknown = 0,
    Running = 1,
    Failed = 2,
    NoConfig = 3,
    Suspended = 4,
    Test = 5,
    CommFault = 6,
}

public sealed class DaServerStatus
{
    public DaServerState State { get; set; }
    public string? VendorInfo { get; set; }
    public DateTime StartTimeUtc { get; set; }
    public DateTime CurrentTimeUtc { get; set; }
    public string? Version { get; set; }
    public int GroupCount { get; set; }
}
