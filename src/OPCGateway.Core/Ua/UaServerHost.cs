using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using OPCGateway.Core.Configuration;

namespace OPCGateway.Core.Ua;

/// <summary>以 OPC Foundation .NET Standard 堆疊實作的 UA 伺服器主機。</summary>
public sealed class UaServerHost : IUaServerHost
{
    private readonly ILogger<UaServerHost> _logger;
    private readonly Action<ILoggingBuilder>? _configureUaLogging;
    private readonly object _sync = new();
    private ApplicationInstance? _application;
    private GatewayServer? _server;
    private Timer? _clientRefreshTimer;
    private IReadOnlyList<string> _endpoints = Array.Empty<string>();
    private IReadOnlyList<UaClientInfo> _clients = Array.Empty<UaClientInfo>();
    private bool _isRunning;
    private bool _disposed;

    public UaServerHost(ILogger<UaServerHost>? logger = null, Action<ILoggingBuilder>? configureUaLogging = null)
    {
        _logger = logger ?? NullLogger<UaServerHost>.Instance;
        _configureUaLogging = configureUaLogging;
    }

    public event EventHandler<bool>? RunningChanged;
    public event EventHandler<IReadOnlyList<UaClientInfo>>? ClientsChanged;

    public bool IsRunning => _isRunning;
    public IReadOnlyList<string> EndpointUrls => _endpoints;
    public IReadOnlyList<UaClientInfo> Clients => _clients;
    public Func<string, object?, Task<bool>>? WriteHandler { get; set; }

    public async Task StartAsync(UaServerConfig config, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UaServerHost));
        if (_isRunning)
            return;

        var appConfig = BuildApplicationConfiguration(config);
        await appConfig.ValidateAsync(ApplicationType.Server, cancellationToken).ConfigureAwait(false);

        var telemetry = DefaultTelemetry.Create(_configureUaLogging ?? (_ => { }));
        var application = new ApplicationInstance(appConfig, telemetry)
        {
            ApplicationName = config.ApplicationName,
            ApplicationType = ApplicationType.Server,
        };

        var certificateOk = await application.CheckApplicationInstanceCertificatesAsync(true, null, cancellationToken).ConfigureAwait(false);
        if (!certificateOk)
            throw new InvalidOperationException("應用程式憑證檢查失敗，無法啟動 OPC UA 伺服器");

        var server = new GatewayServer(config, this, _logger);
        try
        {
            await application.StartAsync(server).ConfigureAwait(false);
        }
        catch
        {
            try { server.Dispose(); } catch { /* ignore */ }
            throw;
        }

        lock (_sync)
        {
            _application = application;
            _server = server;
            _endpoints = appConfig.ServerConfiguration.BaseAddresses.ToList();
            _isRunning = true;
        }

        _clientRefreshTimer = new Timer(_ => RefreshClients(), null, 5000, 5000);
        _logger.LogInformation("OPC UA 伺服器已啟動：{Endpoints}", string.Join(", ", _endpoints));
        RunningChanged?.Invoke(this, true);
    }

    public async Task StopAsync()
    {
        ApplicationInstance? application;
        GatewayServer? server;
        lock (_sync)
        {
            if (!_isRunning)
                return;
            application = _application;
            server = _server;
            _application = null;
            _server = null;
            _isRunning = false;
        }

        _clientRefreshTimer?.Dispose();
        _clientRefreshTimer = null;

        try
        {
            if (application != null)
                await application.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "停止 OPC UA 應用程式時發生錯誤");
        }

        try { server?.Dispose(); } catch { /* ignore */ }

        _endpoints = Array.Empty<string>();
        _clients = Array.Empty<UaClientInfo>();
        ClientsChanged?.Invoke(this, _clients);
        RunningChanged?.Invoke(this, false);
    }

    public void AddOrUpdateVariable(UaVariableDefinition definition) => _server?.NodeManager?.AddOrUpdateVariable(definition);
    public void RemoveVariable(string key) => _server?.NodeManager?.RemoveVariable(key);
    public void UpdateValue(string key, object? value, uint statusCode, DateTime timestampUtc) => _server?.NodeManager?.UpdateValue(key, value, statusCode, timestampUtc);
    public void SetStatus(string key, uint statusCode) => _server?.NodeManager?.SetStatus(key, statusCode);
    public void SetAllStatus(uint statusCode) => _server?.NodeManager?.SetAllStatus(statusCode);
    public void UpdateGatewayStatus(GatewayStatusSnapshot snapshot) => _server?.NodeManager?.UpdateGatewayStatus(snapshot);

    internal void PublishClients(IReadOnlyList<UaClientInfo> clients)
    {
        _clients = clients;
        ClientsChanged?.Invoke(this, clients);
    }

    internal bool HandleWrite(string key, object? value)
    {
        var handler = WriteHandler;
        if (handler == null)
            return false;
        try
        {
            var task = handler(key, value);
            return task.Wait(TimeSpan.FromSeconds(10)) && task.Result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "處理 UA 寫入 {Key} 時發生錯誤", key);
            return false;
        }
    }

    private void RefreshClients()
    {
        try
        {
            var server = _server;
            if (server == null || !_isRunning)
                return;
            PublishClients(server.SnapshotClients(null));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "更新客戶端清單失敗");
        }
    }

    private static ApplicationConfiguration BuildApplicationConfiguration(UaServerConfig config)
    {
        var storeRoot = config.CertificateStoreRoot.TrimEnd('\\', '/');
        var hostName = Utils.GetHostName();

        var securityPolicies = new ServerSecurityPolicyCollection();
        if (config.AllowNoSecurity || !config.EnableSecurity)
        {
            securityPolicies.Add(new ServerSecurityPolicy
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
            });
        }
        if (config.EnableSecurity)
        {
            securityPolicies.Add(new ServerSecurityPolicy
            {
                SecurityMode = MessageSecurityMode.Sign,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            });
            securityPolicies.Add(new ServerSecurityPolicy
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            });
            securityPolicies.Add(new ServerSecurityPolicy
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss,
            });
        }

        var configuration = new ApplicationConfiguration
        {
            ApplicationName = config.ApplicationName,
            ApplicationUri = config.ApplicationUri,
            ProductUri = config.ProductUri,
            ApplicationType = ApplicationType.Server,
            ServerConfiguration = new ServerConfiguration
            {
                BaseAddresses = BuildBaseAddresses(config),
                SecurityPolicies = securityPolicies,
                UserTokenPolicies = new UserTokenPolicyCollection
                {
                    new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "Anonymous" },
                },
                MaxSessionCount = config.MaxSessions,
                MinSessionTimeout = 10_000,
                MaxSessionTimeout = 3_600_000,
                MaxBrowseContinuationPoints = 100,
                MaxQueryContinuationPoints = 100,
                MaxHistoryContinuationPoints = 100,
                MaxRequestAge = 600_000,
                MinRequestThreadCount = 5,
                MaxRequestThreadCount = 100,
                MaxQueuedRequestCount = 2_000,
                MaxSubscriptionCount = 1_000,
                MaxMessageQueueSize = 100,
                MaxNotificationQueueSize = 1_000,
                MaxNotificationsPerPublish = 1_000,
                MaxPublishRequestCount = 20,
                MinPublishingInterval = 100,
                MaxPublishingInterval = 3_600_000,
                PublishingResolution = 50,
                MinSubscriptionLifetime = 10_000,
                MaxSubscriptionLifetime = 3_600_000,
                DiagnosticsEnabled = true,
            },
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = storeRoot + @"\MachineDefault",
                    SubjectName = $"CN={config.ApplicationName}, DC={hostName}",
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = storeRoot + @"\UA Certificate Authorities",
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = storeRoot + @"\UA Applications",
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = storeRoot + @"\RejectedCertificates",
                },
                AutoAcceptUntrustedCertificates = config.AutoAcceptClientCertificates,
                RejectSHA1SignedCertificates = true,
                MinimumCertificateKeySize = 2048,
                AddAppCertToTrustedStore = true,
            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 15_000,
                MaxStringLength = 1_048_576,
                MaxByteStringLength = 4_194_304,
                MaxArrayLength = 65_535,
                MaxMessageSize = 4_194_304,
                MaxBufferSize = 65_535,
                ChannelLifetime = 300_000,
                SecurityTokenLifetime = 3_600_000,
            },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60_000 },
        };

        return configuration;
    }

    private static StringCollection BuildBaseAddresses(UaServerConfig config)
    {
        var addresses = new StringCollection();
        if (config.UseHostNameInEndpoint)
        {
            // 堆疊會把 localhost 替換成主機名稱，並在所有介面上監聽。
            addresses.Add($"opc.tcp://localhost:{config.Port}");
            return addresses;
        }

        addresses.Add($"opc.tcp://localhost:{config.Port}");
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    var ip = unicast.Address;
                    if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip))
                        continue;
                    var text = ip.ToString();
                    if (text.StartsWith("169.254", StringComparison.Ordinal))
                        continue;
                    var url = $"opc.tcp://{text}:{config.Port}";
                    if (!addresses.Contains(url))
                        addresses.Add(url);
                }
            }
        }
        catch
        {
            // 列舉失敗時只保留 localhost
        }
        return addresses;
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
            _logger.LogWarning(ex, "Dispose 時停止 OPC UA 伺服器失敗");
        }
    }
}

/// <summary>StandardServer 子類別：掛入節點管理器並回報工作階段變化。</summary>
internal sealed class GatewayServer : StandardServer
{
    private readonly UaServerConfig _config;
    private readonly UaServerHost _host;
    private readonly ILogger _logger;
    private GatewayNodeManager? _nodeManager;

    public GatewayServer(UaServerConfig config, UaServerHost host, ILogger logger)
    {
        _config = config;
        _host = host;
        _logger = logger;
    }

    public GatewayNodeManager? NodeManager => _nodeManager;

    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        _nodeManager = new GatewayNodeManager(server, configuration, _config, _host, _logger);
        return new MasterNodeManager(server, configuration, null, new INodeManager[] { _nodeManager });
    }

    protected override void OnServerStarted(IServerInternal server)
    {
        base.OnServerStarted(server);
        var sessions = server.SessionManager;
        sessions.SessionCreated += OnSessionEvent;
        sessions.SessionActivated += OnSessionEvent;
        sessions.SessionClosing += OnSessionEvent;
    }

    private void OnSessionEvent(ISession session, SessionEventReason reason)
    {
        try
        {
            var closing = reason == SessionEventReason.Closing ? session : null;
            var clients = SnapshotClients(closing);
            if (reason == SessionEventReason.Activated)
                _logger.LogInformation("UA 客戶端已連線：{Client}（{Endpoint}）", Describe(session), session.EndpointDescription?.EndpointUrl);
            else if (reason == SessionEventReason.Closing)
                _logger.LogInformation("UA 客戶端已離線：{Client}", Describe(session));
            _host.PublishClients(clients);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "處理工作階段事件失敗");
        }
    }

    private static string Describe(ISession session)
    {
        var name = session.SessionDiagnostics?.ClientDescription?.ApplicationName?.Text;
        return string.IsNullOrWhiteSpace(name) ? session.Id.ToString() : name!;
    }

    internal IReadOnlyList<UaClientInfo> SnapshotClients(ISession? closing)
    {
        var sessions = CurrentInstance?.SessionManager?.GetSessions();
        if (sessions == null)
            return Array.Empty<UaClientInfo>();

        var list = new List<UaClientInfo>();
        foreach (var session in sessions)
        {
            if (session == null || ReferenceEquals(session, closing))
                continue;
            var diagnostics = session.SessionDiagnostics;
            list.Add(new UaClientInfo
            {
                SessionId = session.Id?.ToString() ?? string.Empty,
                SessionName = diagnostics?.SessionName,
                ApplicationName = diagnostics?.ClientDescription?.ApplicationName?.Text,
                ApplicationUri = diagnostics?.ClientDescription?.ApplicationUri,
                EndpointUrl = session.EndpointDescription?.EndpointUrl,
                SecurityMode = session.EndpointDescription?.SecurityMode.ToString(),
                UserIdentity = string.IsNullOrWhiteSpace(session.Identity?.DisplayName) ? "Anonymous" : session.Identity!.DisplayName,
                ConnectedAtUtc = diagnostics?.ClientConnectionTime ?? DateTime.UtcNow,
                LastContactUtc = session.ClientLastContactTime,
                SubscriptionCount = (int)(diagnostics?.CurrentSubscriptionsCount ?? 0),
            });
        }
        return list;
    }
}

/// <summary>閘道的位址空間：根資料夾、狀態資料夾、依 DA 階層鏡射的變數節點。</summary>
internal sealed class GatewayNodeManager : CustomNodeManager2
{
    private const string FolderIdPrefix = "__folder__/";
    private readonly UaServerConfig _config;
    private readonly UaServerHost _host;
    private readonly ILogger _logger;
    private readonly Dictionary<string, BaseDataVariableState> _variables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FolderState> _folders = new(StringComparer.Ordinal);
    private FolderState? _root;
    private BaseDataVariableState? _statusDaConnected;
    private BaseDataVariableState? _statusDaServer;
    private BaseDataVariableState? _statusTagCount;
    private BaseDataVariableState? _statusActiveTagCount;
    private BaseDataVariableState? _statusUpdatesPerSecond;
    private BaseDataVariableState? _statusTotalUpdates;
    private BaseDataVariableState? _statusDroppedUpdates;
    private BaseDataVariableState? _statusLastUpdate;
    private BaseDataVariableState? _statusUptime;
    private BaseDataVariableState? _statusVersion;
    private BaseDataVariableState? _statusClientCount;

    public GatewayNodeManager(IServerInternal server, ApplicationConfiguration configuration, UaServerConfig config, UaServerHost host, ILogger logger)
        : base(server, configuration, config.NamespaceUri)
    {
        _config = config;
        _host = host;
        _logger = logger;
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            _root = CreateFolder(null, FolderIdPrefix + _config.RootFolderName, _config.RootFolderName);
            _root.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);

            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, _root.NodeId));
            AddPredefinedNode(SystemContext, _root);

            var status = CreateFolder(_root, FolderIdPrefix + _config.RootFolderName + "/Status", "Status");
            AddPredefinedNode(SystemContext, status);

            _statusDaConnected = CreateStatusVariable(status, "DaConnected", DataTypeIds.Boolean, false);
            _statusDaServer = CreateStatusVariable(status, "DaServer", DataTypeIds.String, string.Empty);
            _statusTagCount = CreateStatusVariable(status, "TagCount", DataTypeIds.Int32, 0);
            _statusActiveTagCount = CreateStatusVariable(status, "ActiveTagCount", DataTypeIds.Int32, 0);
            _statusUpdatesPerSecond = CreateStatusVariable(status, "UpdatesPerSecond", DataTypeIds.Double, 0.0);
            _statusTotalUpdates = CreateStatusVariable(status, "TotalUpdates", DataTypeIds.Int64, 0L);
            _statusDroppedUpdates = CreateStatusVariable(status, "DroppedUpdates", DataTypeIds.Int64, 0L);
            _statusLastUpdate = CreateStatusVariable(status, "LastUpdateTime", DataTypeIds.DateTime, DateTime.MinValue);
            _statusUptime = CreateStatusVariable(status, "UptimeSeconds", DataTypeIds.Double, 0.0);
            _statusVersion = CreateStatusVariable(status, "GatewayVersion", DataTypeIds.String, string.Empty);
            _statusClientCount = CreateStatusVariable(status, "ClientCount", DataTypeIds.Int32, 0);

            _logger.LogInformation("UA 位址空間已建立：{Root}（ns={Index}）", _config.RootFolderName, NamespaceIndex);
        }
    }

    public void AddOrUpdateVariable(UaVariableDefinition definition)
    {
        lock (Lock)
        {
            if (_root == null)
                return;

            var dataType = UaTypeMapper.GetDataTypeId(definition.ClrType, out var valueRank);

            if (_variables.TryGetValue(definition.Key, out var existing))
            {
                var changed = false;
                if (existing.DataType != dataType) { existing.DataType = dataType; changed = true; }
                if (existing.ValueRank != valueRank) { existing.ValueRank = valueRank; changed = true; }
                if (existing.DisplayName?.Text != definition.BrowseName)
                {
                    existing.DisplayName = new LocalizedText(definition.BrowseName);
                    existing.BrowseName = new QualifiedName(definition.BrowseName, NamespaceIndex);
                    changed = true;
                }
                var access = definition.AllowWrite ? AccessLevels.CurrentReadOrWrite : AccessLevels.CurrentRead;
                if (existing.AccessLevel != access)
                {
                    existing.AccessLevel = access;
                    existing.UserAccessLevel = access;
                    existing.OnWriteValue = definition.AllowWrite ? OnWriteValue : null;
                    changed = true;
                }
                if (changed)
                    existing.ClearChangeMasks(SystemContext, false);
                return;
            }

            var parent = _config.MirrorDaHierarchy ? EnsureFolder(definition.FolderPath) : _root;
            var variable = new BaseDataVariableState(parent)
            {
                NodeId = new NodeId(definition.Key, NamespaceIndex),
                BrowseName = new QualifiedName(definition.BrowseName, NamespaceIndex),
                DisplayName = new LocalizedText(definition.BrowseName),
                Description = new LocalizedText(definition.Description ?? $"OPC DA: {definition.Key}"),
                SymbolicName = definition.BrowseName,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                ReferenceTypeId = ReferenceTypes.HasComponent,
                DataType = dataType,
                ValueRank = valueRank,
                Historizing = false,
                StatusCode = StatusCodes.BadWaitingForInitialData,
                Timestamp = DateTime.UtcNow,
                Value = null,
            };
            var accessLevel = definition.AllowWrite ? AccessLevels.CurrentReadOrWrite : AccessLevels.CurrentRead;
            variable.AccessLevel = accessLevel;
            variable.UserAccessLevel = accessLevel;
            if (definition.AllowWrite)
                variable.OnWriteValue = OnWriteValue;

            parent.AddChild(variable);
            AddPredefinedNode(SystemContext, variable);
            _variables[definition.Key] = variable;
        }
    }

    public void RemoveVariable(string key)
    {
        lock (Lock)
        {
            if (!_variables.TryGetValue(key, out var variable))
                return;
            _variables.Remove(key);
            try
            {
                (variable.Parent as NodeState)?.RemoveChild(variable);
                DeleteNode(SystemContext, variable.NodeId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "移除 UA 節點 {Key} 失敗", key);
            }
        }
    }

    public void UpdateValue(string key, object? value, uint statusCode, DateTime timestampUtc)
    {
        lock (Lock)
        {
            if (!_variables.TryGetValue(key, out var variable))
                return;
            variable.Value = value;
            variable.StatusCode = statusCode;
            variable.Timestamp = timestampUtc;
            variable.ClearChangeMasks(SystemContext, false);
        }
    }

    public void SetStatus(string key, uint statusCode)
    {
        lock (Lock)
        {
            if (!_variables.TryGetValue(key, out var variable))
                return;
            if (variable.StatusCode.Code == statusCode)
                return;
            variable.StatusCode = statusCode;
            variable.Timestamp = DateTime.UtcNow;
            variable.ClearChangeMasks(SystemContext, false);
        }
    }

    public void SetAllStatus(uint statusCode)
    {
        lock (Lock)
        {
            var now = DateTime.UtcNow;
            foreach (var variable in _variables.Values)
            {
                if (variable.StatusCode.Code == statusCode)
                    continue;
                variable.StatusCode = statusCode;
                variable.Timestamp = now;
                variable.ClearChangeMasks(SystemContext, false);
            }
        }
    }

    public void UpdateGatewayStatus(GatewayStatusSnapshot snapshot)
    {
        lock (Lock)
        {
            var now = DateTime.UtcNow;
            SetStatusValue(_statusDaConnected, snapshot.DaConnected, now);
            SetStatusValue(_statusDaServer, snapshot.DaServer ?? string.Empty, now);
            SetStatusValue(_statusTagCount, snapshot.TagCount, now);
            SetStatusValue(_statusActiveTagCount, snapshot.ActiveTagCount, now);
            SetStatusValue(_statusUpdatesPerSecond, Math.Round(snapshot.UpdatesPerSecond, 2), now);
            SetStatusValue(_statusTotalUpdates, snapshot.TotalUpdates, now);
            SetStatusValue(_statusDroppedUpdates, snapshot.DroppedUpdates, now);
            SetStatusValue(_statusLastUpdate, snapshot.LastUpdateUtc ?? DateTime.MinValue, now);
            SetStatusValue(_statusUptime, Math.Round(snapshot.Uptime.TotalSeconds, 0), now);
            SetStatusValue(_statusVersion, snapshot.Version, now);
            SetStatusValue(_statusClientCount, snapshot.ClientCount, now);
        }
    }

    private void SetStatusValue(BaseDataVariableState? variable, object value, DateTime now)
    {
        if (variable == null)
            return;
        if (Equals(variable.Value, value))
            return;
        variable.Value = value;
        variable.StatusCode = StatusCodes.Good;
        variable.Timestamp = now;
        variable.ClearChangeMasks(SystemContext, false);
    }

    private ServiceResult OnWriteValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding,
        ref object value, ref StatusCode statusCode, ref DateTime timestamp)
    {
        var key = node.NodeId.Identifier as string;
        if (key == null)
            return StatusCodes.BadNotWritable;

        var ok = _host.HandleWrite(key, value);
        if (!ok)
            return ServiceResult.Create(StatusCodes.BadNotWritable, "來源 OPC DA 拒絕寫入或未連線");

        statusCode = StatusCodes.Good;
        timestamp = DateTime.UtcNow;
        return ServiceResult.Good;
    }

    private FolderState EnsureFolder(string[] segments)
    {
        var parent = _root!;
        if (segments.Length == 0)
            return parent;

        var key = FolderIdPrefix + _config.RootFolderName;
        foreach (var segment in segments)
        {
            key += "/" + segment;
            if (!_folders.TryGetValue(key, out var folder))
            {
                folder = CreateFolder(parent, key, segment);
                AddPredefinedNode(SystemContext, folder);
                _folders[key] = folder;
            }
            parent = folder;
        }
        return parent;
    }

    private FolderState CreateFolder(NodeState? parent, string nodeIdString, string name)
    {
        var folder = new FolderState(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypes.Organizes,
            TypeDefinitionId = ObjectTypeIds.FolderType,
            NodeId = new NodeId(nodeIdString, NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = new LocalizedText(name),
            WriteMask = AttributeWriteMask.None,
            UserWriteMask = AttributeWriteMask.None,
            EventNotifier = EventNotifiers.None,
        };
        parent?.AddChild(folder);
        return folder;
    }

    private BaseDataVariableState CreateStatusVariable(NodeState parent, string name, NodeId dataType, object initialValue)
    {
        var variable = new BaseDataVariableState(parent)
        {
            NodeId = new NodeId(FolderIdPrefix + _config.RootFolderName + "/Status/" + name, NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = new LocalizedText(name),
            SymbolicName = name,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            ReferenceTypeId = ReferenceTypes.HasComponent,
            DataType = dataType,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentRead,
            UserAccessLevel = AccessLevels.CurrentRead,
            Historizing = false,
            Value = initialValue,
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow,
        };
        parent.AddChild(variable);
        AddPredefinedNode(SystemContext, variable);
        return variable;
    }
}
