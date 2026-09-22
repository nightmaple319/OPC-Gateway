using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OPCGateway.Core.Configuration;

/// <summary>設定驗證結果：<see cref="Errors"/> 為阻擋性錯誤，<see cref="Fixes"/> 為已自動修正的項目。</summary>
public sealed class ConfigValidationResult
{
    public List<string> Errors { get; } = new();
    public List<string> Fixes { get; } = new();
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// 負責設定檔的載入、儲存、驗證、備份與舊格式轉換。
/// 預設路徑為執行檔目錄下的 Config/gateway_config.json，不依賴工作目錄。
/// </summary>
public sealed class ConfigStore
{
    private readonly ILogger<ConfigStore> _logger;
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Include,
    };

    public ConfigStore(ILogger<ConfigStore>? logger = null, string? defaultPath = null)
    {
        _logger = logger ?? NullLogger<ConfigStore>.Instance;
        DefaultPath = defaultPath ?? Path.Combine(AppContext.BaseDirectory, "Config", "gateway_config.json");
    }

    public string DefaultPath { get; }

    /// <summary>載入設定；檔案不存在時建立預設設定並寫入。</summary>
    public GatewayConfig Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        if (!File.Exists(file))
        {
            _logger.LogInformation("設定檔不存在，建立預設設定: {Path}", file);
            var created = CreateDefault();
            Save(created, file);
            return created;
        }

        var json = File.ReadAllText(file);
        var config = Parse(json);
        var result = Validate(config);
        foreach (var fix in result.Fixes)
            _logger.LogWarning("設定自動修正: {Fix}", fix);
        foreach (var error in result.Errors)
            _logger.LogError("設定錯誤: {Error}", error);

        _logger.LogInformation("設定已載入: {Path}（{Count} 個標籤）", file, config.Tags.Count);
        return config;
    }

    /// <summary>解析 JSON 字串；自動辨識 1.x 舊格式並轉換。</summary>
    public GatewayConfig Parse(string json)
    {
        var root = JObject.Parse(json);
        if (root["OPCDAConfig"] != null || root["OPCUAConfig"] != null || root["ItemMappings"] != null)
        {
            _logger.LogInformation("偵測到 1.x 版設定格式，自動轉換");
            return ConvertLegacy(root);
        }

        return root.ToObject<GatewayConfig>() ?? new GatewayConfig();
    }

    public void Save(GatewayConfig config, string? path = null)
    {
        var file = path ?? DefaultPath;
        var directory = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var result = Validate(config);
        if (!result.IsValid)
            throw new InvalidOperationException("設定驗證失敗: " + string.Join("; ", result.Errors));

        var json = JsonConvert.SerializeObject(config, JsonSettings);
        var temp = file + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(file))
            File.Delete(file);
        File.Move(temp, file);
        _logger.LogInformation("設定已儲存: {Path}", file);
    }

    public string? Backup(string? path = null)
    {
        var file = path ?? DefaultPath;
        if (!File.Exists(file))
            return null;
        var backup = $"{file}.{DateTime.Now:yyyyMMdd_HHmmss}.bak";
        File.Copy(file, backup, overwrite: true);
        return backup;
    }

    public static GatewayConfig CreateDefault()
    {
        var config = new GatewayConfig();
        config.Tags.AddRange(new[]
        {
            new TagMappingConfig { ItemId = "Random.Int1" },
            new TagMappingConfig { ItemId = "Random.Real4" },
            new TagMappingConfig { ItemId = "Saw-toothed Waves.Int1" },
            new TagMappingConfig { ItemId = "Square Waves.Real4" },
            new TagMappingConfig { ItemId = "Triangle Waves.Real4" },
        });
        return config;
    }

    /// <summary>驗證並自動修正可修正的欄位。</summary>
    public static ConfigValidationResult Validate(GatewayConfig config)
    {
        var result = new ConfigValidationResult();

        config.DaSource ??= new DaSourceConfig();
        config.UaServer ??= new UaServerConfig();
        config.Tags ??= new List<TagMappingConfig>();
        config.Options ??= new GatewayOptions();

        var da = config.DaSource;
        if (string.IsNullOrWhiteSpace(da.ProgId))
            result.Errors.Add("OPC DA ProgID 不可為空");
        if (string.IsNullOrWhiteSpace(da.Host)) { da.Host = "localhost"; result.Fixes.Add("DA 主機為空，改為 localhost"); }
        if (da.UpdateRateMs < 50) { da.UpdateRateMs = 1000; result.Fixes.Add("DA 更新頻率過小，改為 1000 ms"); }
        if (da.ConnectTimeoutSeconds <= 0) { da.ConnectTimeoutSeconds = 10; result.Fixes.Add("DA 連線逾時無效，改為 10 秒"); }
        if (da.ReconnectIntervalSeconds <= 0) { da.ReconnectIntervalSeconds = 10; result.Fixes.Add("DA 重連間隔無效，改為 10 秒"); }
        if (da.HealthCheckIntervalSeconds <= 0) { da.HealthCheckIntervalSeconds = 5; result.Fixes.Add("DA 健康檢查間隔無效，改為 5 秒"); }
        if (da.PercentDeadband < 0 || da.PercentDeadband > 100) { da.PercentDeadband = 0; result.Fixes.Add("DA 死區超出 0-100，改為 0"); }
        if (string.IsNullOrEmpty(da.BranchSeparator)) { da.BranchSeparator = "."; result.Fixes.Add("DA 分隔符號為空，改為「.」"); }

        var ua = config.UaServer;
        if (string.IsNullOrWhiteSpace(ua.ServerName)) { ua.ServerName = "OPC Gateway Server"; result.Fixes.Add("UA 伺服器名稱為空，改為預設值"); }
        if (string.IsNullOrWhiteSpace(ua.ApplicationName)) { ua.ApplicationName = ua.ServerName; result.Fixes.Add("UA 應用名稱為空，改用伺服器名稱"); }
        if (string.IsNullOrWhiteSpace(ua.ApplicationUri) || ua.ApplicationUri.Contains(' '))
        {
            ua.ApplicationUri = "urn:localhost:OPCGateway";
            result.Fixes.Add("UA ApplicationUri 無效（空白或含空格），改為 urn:localhost:OPCGateway");
        }
        if (string.IsNullOrWhiteSpace(ua.ProductUri) || ua.ProductUri.Contains(' ')) { ua.ProductUri = "urn:opcgateway:tool"; result.Fixes.Add("UA ProductUri 無效，改為預設值"); }
        if (ua.Port < 1 || ua.Port > 65535) { ua.Port = 4840; result.Fixes.Add($"UA 連接埠 {ua.Port} 無效，改為 4840"); }
        if (ua.MaxSessions <= 0) { ua.MaxSessions = 100; result.Fixes.Add("UA 最大工作階段數無效，改為 100"); }
        if (string.IsNullOrWhiteSpace(ua.NamespaceUri) || ua.NamespaceUri.Contains(' ')) { ua.NamespaceUri = "urn:opcgateway:da"; result.Fixes.Add("UA 命名空間 URI 無效，改為預設值"); }
        if (string.IsNullOrWhiteSpace(ua.RootFolderName)) { ua.RootFolderName = "Gateway"; result.Fixes.Add("UA 根資料夾名稱為空，改為 Gateway"); }
        if (!ua.AllowNoSecurity && !ua.EnableSecurity)
        {
            ua.AllowNoSecurity = true;
            result.Fixes.Add("UA 未啟用任何安全性原則，已自動保留 None 端點");
        }
        if (string.IsNullOrWhiteSpace(ua.CertificateStoreRoot)) { ua.CertificateStoreRoot = @"%CommonApplicationData%\OPC Foundation\CertificateStores"; result.Fixes.Add("憑證存放區為空，改為預設值"); }

        var options = config.Options;
        if (options.UiRefreshIntervalMs < 50) { options.UiRefreshIntervalMs = 250; result.Fixes.Add("UI 刷新間隔過小，改為 250 ms"); }
        if (options.LogBufferSize < 100) { options.LogBufferSize = 2000; result.Fixes.Add("日誌緩衝筆數過小，改為 2000"); }
        if (options.ValueQueueCapacity < 1000) { options.ValueQueueCapacity = 100_000; result.Fixes.Add("值佇列容量過小，改為 100000"); }
        if (options.BrowseChildLimit < 10) { options.BrowseChildLimit = 2000; result.Fixes.Add("瀏覽子項目上限過小，改為 2000"); }
        var theme = NormalizeTheme(options.Theme);
        if (theme == null)
        {
            result.Fixes.Add($"介面主題「{options.Theme}」無效，改為 System");
            theme = "System";
        }
        options.Theme = theme;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = config.Tags.Count - 1; i >= 0; i--)
        {
            var tag = config.Tags[i];
            if (tag == null || string.IsNullOrWhiteSpace(tag.ItemId))
            {
                config.Tags.RemoveAt(i);
                result.Fixes.Add($"第 {i + 1} 筆標籤的 ItemId 為空，已移除");
                continue;
            }
            tag.ItemId = tag.ItemId.Trim();
            if (!seen.Add(tag.ItemId))
            {
                config.Tags.RemoveAt(i);
                result.Fixes.Add($"標籤 {tag.ItemId} 重複，已移除重複項");
                continue;
            }
            if (tag.BrowseName != null && string.IsNullOrWhiteSpace(tag.BrowseName))
                tag.BrowseName = null;
        }

        return result;
    }

    /// <summary>把主題字串正規化為 System / Light / Dark；無法辨識回傳 null。</summary>
    public static string? NormalizeTheme(string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
            return "System";
        return theme!.Trim().ToLowerInvariant() switch
        {
            "system" or "auto" or "default" => "System",
            "light" => "Light",
            "dark" => "Dark",
            _ => null,
        };
    }

    private static GatewayConfig ConvertLegacy(JObject root)
    {
        var config = new GatewayConfig();

        if (root["OPCDAConfig"] is JObject da)
        {
            config.DaSource.ProgId = da.Value<string>("ServerName") ?? config.DaSource.ProgId;
            config.DaSource.Host = da.Value<string>("HostName") ?? "localhost";
            config.DaSource.UpdateRateMs = da.Value<int?>("UpdateRate") ?? 1000;
            config.DaSource.ConnectTimeoutSeconds = da.Value<int?>("ConnectionTimeoutSeconds") ?? 10;
            config.DaSource.ReconnectIntervalSeconds = da.Value<int?>("ReconnectIntervalSeconds") ?? 10;
        }

        if (root["OPCUAConfig"] is JObject ua)
        {
            config.UaServer.ServerName = ua.Value<string>("ServerName") ?? config.UaServer.ServerName;
            config.UaServer.ApplicationName = ua.Value<string>("ApplicationName") ?? config.UaServer.ApplicationName;
            config.UaServer.ApplicationUri = ua.Value<string>("ApplicationUri") ?? config.UaServer.ApplicationUri;
            config.UaServer.Port = ua.Value<int?>("Port") ?? 4840;
            config.UaServer.EnableSecurity = ua.Value<bool?>("EnableSecurity") ?? false;
            config.UaServer.MaxSessions = ua.Value<int?>("MaxClients") ?? 100;
        }

        if (root["ItemMappings"] is JArray mappings)
        {
            foreach (var item in mappings.OfType<JObject>())
            {
                var itemId = item.Value<string>("OPCDAItemId");
                if (string.IsNullOrWhiteSpace(itemId))
                    continue;
                config.Tags.Add(new TagMappingConfig
                {
                    ItemId = itemId!,
                    BrowseName = item.Value<string>("OPCUABrowseName"),
                    Enabled = item.Value<bool?>("IsEnabled") ?? true,
                });
            }
        }

        return config;
    }
}
