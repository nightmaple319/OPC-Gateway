using Newtonsoft.Json;

namespace OPCGateway.Core.Configuration;

/// <summary>
/// 閘道的完整設定（可序列化為 JSON）。這是「期望狀態」的唯一來源，
/// <see cref="Engine.GatewayEngine"/> 會把它對帳成實際的 DA 訂閱與 UA 節點。
/// </summary>
public sealed class GatewayConfig
{
    public DaSourceConfig DaSource { get; set; } = new();
    public UaServerConfig UaServer { get; set; } = new();
    public List<TagMappingConfig> Tags { get; set; } = new();
    public GatewayOptions Options { get; set; } = new();

    public GatewayConfig Clone()
    {
        var json = JsonConvert.SerializeObject(this);
        return JsonConvert.DeserializeObject<GatewayConfig>(json) ?? new GatewayConfig();
    }
}

/// <summary>OPC DA 來源伺服器設定。</summary>
public sealed class DaSourceConfig
{
    /// <summary>COM ProgID，例如 Matrikon.OPC.Simulation.1。</summary>
    public string ProgId { get; set; } = "Matrikon.OPC.Simulation.1";

    /// <summary>主機名稱或 IP；本機請用 localhost。</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>群組更新頻率（毫秒）。</summary>
    public int UpdateRateMs { get; set; } = 1000;

    /// <summary>百分比死區（0 表示不使用）。</summary>
    public float PercentDeadband { get; set; } = 0f;

    public int ConnectTimeoutSeconds { get; set; } = 10;

    /// <summary>斷線後每隔多少秒嘗試重連。</summary>
    public int ReconnectIntervalSeconds { get; set; } = 10;

    /// <summary>連線健康檢查（GetStatus）間隔秒數。</summary>
    public int HealthCheckIntervalSeconds { get; set; } = 5;

    /// <summary>DA ItemId 的階層分隔符號；Matrikon 與 Kepware 為「.」。</summary>
    public string BranchSeparator { get; set; } = ".";
}

/// <summary>內建 OPC UA 伺服器設定。</summary>
public sealed class UaServerConfig
{
    public string ServerName { get; set; } = "OPC Gateway Server";
    public string ApplicationName { get; set; } = "OPC DA to UA Gateway";

    /// <summary>應用程式 URI，必須是合法 URI（不可含空白），並與憑證一致。</summary>
    public string ApplicationUri { get; set; } = "urn:localhost:OPCGateway";

    public string ProductUri { get; set; } = "urn:opcgateway:tool";
    public int Port { get; set; } = 4840;

    /// <summary>true：以主機名稱作為端點（opc.tcp://HOSTNAME:port）；false：列舉所有 IPv4 位址各建一個端點。</summary>
    public bool UseHostNameInEndpoint { get; set; } = true;

    /// <summary>保留無安全性的端點（None / Anonymous）。</summary>
    public bool AllowNoSecurity { get; set; } = true;

    /// <summary>加入 Basic256Sha256 的 Sign 與 SignAndEncrypt 端點。</summary>
    public bool EnableSecurity { get; set; } = false;

    /// <summary>自動信任未知的客戶端憑證。生產環境建議關閉並手動信任。</summary>
    public bool AutoAcceptClientCertificates { get; set; } = true;

    public int MaxSessions { get; set; } = 100;

    /// <summary>閘道命名空間 URI（節點的 ns）。</summary>
    public string NamespaceUri { get; set; } = "urn:opcgateway:da";

    /// <summary>Objects 資料夾下的根資料夾名稱。</summary>
    public string RootFolderName { get; set; } = "Gateway";

    /// <summary>true：依 DA ItemId 的階層建立子資料夾；false：所有節點平鋪在根資料夾。</summary>
    public bool MirrorDaHierarchy { get; set; } = true;

    /// <summary>憑證存放區根目錄，支援 %CommonApplicationData% 等環境變數。</summary>
    public string CertificateStoreRoot { get; set; } = @"%CommonApplicationData%\OPC Foundation\CertificateStores";
}

/// <summary>單一標籤的映射定義。</summary>
public sealed class TagMappingConfig
{
    /// <summary>OPC DA ItemId，同時作為映射的唯一鍵與 UA NodeId 的字串識別碼。</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>UA BrowseName / DisplayName；留空則使用 ItemId 的最後一段。</summary>
    public string? BrowseName { get; set; }

    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>允許 UA 客戶端寫入並回寫到 DA。</summary>
    public bool AllowWrite { get; set; } = false;
}

/// <summary>閘道行為選項。</summary>
public sealed class GatewayOptions
{
    /// <summary>程式啟動後自動啟動閘道。</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>UI 即時值批次刷新間隔（毫秒）。</summary>
    public int UiRefreshIntervalMs { get; set; } = 250;

    /// <summary>UI 日誌緩衝區保留筆數。</summary>
    public int LogBufferSize { get; set; } = 2000;

    /// <summary>DA 到 UA 的值佇列容量；滿了會丟棄最舊的值並計數。</summary>
    public int ValueQueueCapacity { get; set; } = 100_000;

    /// <summary>瀏覽單一資料夾時最多回傳的子項目數。</summary>
    public int BrowseChildLimit { get; set; } = 2000;

    /// <summary>介面主題：System（跟隨 Windows）、Light、Dark。</summary>
    public string Theme { get; set; } = "System";
}
