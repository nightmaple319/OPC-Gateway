using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Timers;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Configuration;
using OPCGatewayTool.Models;
using NLog;

namespace OPCGatewayTool.Services
{
    public class SimpleRealOPCUAService : IDisposable
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public SimpleRealOPCUAService()
        {
            logger.Info("SimpleRealOPCUAService 已初始化");
        }
        
        private ApplicationInstance _application;
        private GatewayServer _server;
        private bool _isRunning;
        private bool _disposed;
        private Timer _sessionCheckTimer;
        private int _sessionCheckIntervalMs = 2000;

        public event EventHandler<bool> ServerStatusChanged;
        public event EventHandler<int> ClientCountChanged;
        public event EventHandler<string> LogMessage;
        public event EventHandler<ClientInfo> ClientConnected;
        public event EventHandler<string> ClientDisconnected;

        public ObservableCollection<OPCUANode> Nodes { get; } = new ObservableCollection<OPCUANode>();
        public ObservableCollection<ClientInfo> ConnectedClients { get; } = new ObservableCollection<ClientInfo>();
        
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    ServerStatusChanged?.Invoke(this, value);
                    logger.Info($"OPC UA 伺服器狀態變更: {(value ? "運行中" : "已停止")}");
                }
            }
        }

        public int ConnectedClientCount { get; private set; } = 0;
        public string EndpointUrl { get; private set; }

        public async Task<bool> StartAsync(OPCUAConfig config)
        {
            return await Task.Run(() => Start(config));
        }

        public bool Start(OPCUAConfig config)
        {
            try
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(SimpleRealOPCUAService));

                if (IsRunning)
                {
                    logger.Warn("OPC UA 伺服器已在運行中");
                    return true;
                }

                logger.Info("正在啟動 OPC UA 伺服器...");

                // 創建應用程式實例
                _application = new ApplicationInstance
                {
                    ApplicationName = config.ServerName,
                    ApplicationType = ApplicationType.Server,
                    ApplicationConfiguration = CreateServerConfiguration(config)
                };

                // 檢查應用程式憑證
                try
                {
                    var hasAppCertificate = _application.CheckApplicationInstanceCertificate(false, 2048).Result;
                    if (!hasAppCertificate)
                    {
                        logger.Warn("應用程式憑證檢查失敗，但繼續啟動");
                    }
                }
                catch (Exception certEx)
                {
                    logger.Warn($"憑證檢查失敗: {certEx.Message}，但繼續啟動");
                }

                // 創建自訂伺服器
                _server = new GatewayServer(this);
                logger.Info("GatewayServer 已創建");

                // 啟動伺服器
                logger.Info("正在啟動OPC UA伺服器...");
                _application.Start(_server).Wait();
                logger.Info("OPC UA伺服器啟動完成");

                EndpointUrl = GetPrimaryEndpointUrl(config.Port);
                IsRunning = true;
                
                // 設置會話監控間隔
                _sessionCheckIntervalMs = config.SessionCheckIntervalMs > 0 ? config.SessionCheckIntervalMs : 2000;

                // 啟動會話監控計時器
                StartSessionMonitoring();
                
                
                logger.Info($"OPC UA 伺服器已啟動，端點: {EndpointUrl}");
                LogMessage?.Invoke(this, $"伺服器已啟動: {EndpointUrl}");
                
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "啟動 OPC UA 伺服器失敗");
                LogMessage?.Invoke(this, $"伺服器啟動失敗: {ex.Message}");
                IsRunning = false;
                return false;
            }
        }

        public void Stop()
        {
            try
            {
                if (!IsRunning)
                {
                    logger.Info("OPC UA 伺服器未在運行中");
                    return;
                }

                logger.Info("正在停止 OPC UA 伺服器...");

                StopSessionMonitoring();
                _server?.Dispose();
                _application?.Stop();

                IsRunning = false;
                ConnectedClientCount = 0;
                ClientCountChanged?.Invoke(this, ConnectedClientCount);
                
                logger.Info("OPC UA 伺服器已停止");
                LogMessage?.Invoke(this, "伺服器已停止");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "停止 OPC UA 伺服器時發生錯誤");
            }
        }

        public bool AddNode(string itemId, string browseName, object value, Type dataType)
        {
            try
            {
                var node = new OPCUANode
                {
                    BrowseName = browseName,
                    DisplayName = browseName,
                    Value = value,
                    DataType = dataType,
                    Timestamp = DateTime.UtcNow,
                    SourceItemId = itemId,
                    Description = $"來自 OPC DA 項目: {itemId}"
                };

                Nodes.Add(node);
                
                // 添加到實際的 OPC UA 伺服器
                _server?.AddNode(node);
                
                logger.Info($"成功添加 OPC UA 節點: {browseName} (來源: {itemId})");
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"添加節點失敗: {itemId}");
                return false;
            }
        }

        public bool RemoveNode(string itemId)
        {
            try
            {
                for (int i = Nodes.Count - 1; i >= 0; i--)
                {
                    if (Nodes[i].SourceItemId == itemId)
                    {
                        var node = Nodes[i];
                        Nodes.RemoveAt(i);
                        
                        // 從實際的 OPC UA 伺服器移除
                        _server?.RemoveNode(node);
                        
                        logger.Info($"成功移除 OPC UA 節點: {node.BrowseName} (來源: {itemId})");
                        return true;
                    }
                }
                
                logger.Warn($"未找到要移除的節點: {itemId}");
                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"移除節點失敗: {itemId}");
                return false;
            }
        }

        public bool UpdateNodeValue(string itemId, object value, DateTime timestamp)
        {
            try
            {
                var node = Nodes.FirstOrDefault(n => n.SourceItemId == itemId);
                if (node != null)
                {
                    node.Value = value;
                    node.Timestamp = timestamp;
                    
                    // 更新實際的 OPC UA 伺服器節點值
                    _server?.UpdateNodeValue(node);
                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"更新節點值失敗: {itemId}");
                return false;
            }
        }

        private ApplicationConfiguration CreateServerConfiguration(OPCUAConfig config)
        {
            var configuration = new ApplicationConfiguration()
            {
                ApplicationName = config.ServerName,
                ApplicationUri = $"urn:localhost:{config.ServerName}",
                ApplicationType = ApplicationType.Server,
                ProductUri = "urn:OPCGatewayTool",

                ServerConfiguration = new ServerConfiguration()
                {
                    BaseAddresses = GetBaseAddresses(config.Port),
                    SecurityPolicies = new ServerSecurityPolicyCollection()
                    {
                        new ServerSecurityPolicy()
                        {
                            SecurityMode = MessageSecurityMode.None,
                            SecurityPolicyUri = SecurityPolicies.None
                        }
                    },
                    UserTokenPolicies = new UserTokenPolicyCollection()
                    {
                        new UserTokenPolicy()
                        {
                            TokenType = UserTokenType.Anonymous,
                            PolicyId = "AnonymousPolicy"
                        }
                    },
                    MaxSessionCount = config.MaxClients,
                    MaxSessionTimeout = 60000,
                    MinSessionTimeout = 10000,
                    MaxBrowseContinuationPoints = 10,
                    MaxQueryContinuationPoints = 10,
                    MaxHistoryContinuationPoints = 10,
                    MaxRequestAge = 600000,
                    MinRequestThreadCount = 5,
                    MaxRequestThreadCount = 100,
                    MaxMessageQueueSize = 100
                },

                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = @"Directory",
                        StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\MachineDefault",
                        SubjectName = config.ServerName
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = @"Directory",
                        StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\UA Certificate Authorities"
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = @"Directory", 
                        StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\UA Applications"
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = @"Directory",
                        StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\RejectedCertificates"
                    },
                    AutoAcceptUntrustedCertificates = true,
                    RejectSHA1SignedCertificates = false,
                    MinimumCertificateKeySize = 1024,
                },

                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
                TraceConfiguration = new TraceConfiguration()
            };

            configuration.Validate(ApplicationType.Server).Wait();
            return configuration;
        }

        private StringCollection GetBaseAddresses(int port)
        {
            var addresses = new StringCollection();
            
            try
            {
                // 添加 localhost
                addresses.Add($"opc.tcp://localhost:{port}");
                
                // 獲取所有網路介面的IP地址
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                var uniqueIPs = new HashSet<string>();
                
                foreach (var networkInterface in networkInterfaces)
                {
                    // 只處理啟用且正在運行的網路介面
                    if (networkInterface.OperationalStatus == OperationalStatus.Up)
                    {
                        var ipProperties = networkInterface.GetIPProperties();
                        
                        foreach (var unicastAddress in ipProperties.UnicastAddresses)
                        {
                            var ip = unicastAddress.Address;
                            
                            // 只添加IPv4地址，排除loopback和link-local地址
                            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                                !IPAddress.IsLoopback(ip) &&
                                !ip.ToString().StartsWith("169.254") && // 排除APIPA地址
                                !ip.ToString().StartsWith("127."))     // 排除其他loopback地址
                            {
                                var ipString = ip.ToString();
                                if (uniqueIPs.Add(ipString)) // 確保不重複
                                {
                                    addresses.Add($"opc.tcp://{ipString}:{port}");
                                    logger.Info($"添加OPC UA端點: opc.tcp://{ipString}:{port}");
                                }
                            }
                        }
                    }
                }
                
                // 如果沒有找到其他IP地址，至少確保有localhost
                if (addresses.Count == 1)
                {
                    logger.Info("只找到localhost地址，OPC UA伺服器僅可本機訪問");
                }
                else
                {
                    logger.Info($"OPC UA伺服器將監聽 {addresses.Count} 個端點，支援遠端連線");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "獲取網路地址時發生錯誤，使用預設localhost");
                addresses.Clear();
                addresses.Add($"opc.tcp://localhost:{port}");
            }
            
            return addresses;
        }

        private string GetPrimaryEndpointUrl(int port)
        {
            try
            {
                // 優先使用第一個非localhost的IP地址
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                
                foreach (var networkInterface in networkInterfaces)
                {
                    if (networkInterface.OperationalStatus == OperationalStatus.Up)
                    {
                        var ipProperties = networkInterface.GetIPProperties();
                        
                        foreach (var unicastAddress in ipProperties.UnicastAddresses)
                        {
                            var ip = unicastAddress.Address;
                            
                            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                                !IPAddress.IsLoopback(ip) &&
                                !ip.ToString().StartsWith("169.254") &&
                                !ip.ToString().StartsWith("127."))
                            {
                                var endpointUrl = $"opc.tcp://{ip}:{port}";
                                logger.Info($"主要端點URL: {endpointUrl}");
                                return endpointUrl;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "獲取主要端點URL時發生錯誤");
            }
            
            // 如果沒有找到其他IP，回退到localhost
            return $"opc.tcp://localhost:{port}";
        }

        public void OnClientConnected(ClientInfo clientInfo)
        {
            ConnectedClientCount++;
            ClientCountChanged?.Invoke(this, ConnectedClientCount);
            ClientConnected?.Invoke(this, clientInfo);
            logger.Info($"客戶端已連接: {clientInfo.DisplayName}，當前連接數: {ConnectedClientCount}");
            LogMessage?.Invoke(this, $"客戶端已連接: {clientInfo.DisplayName}，當前連接數: {ConnectedClientCount}");
        }

        public void OnClientDisconnected(string sessionId)
        {
            if (ConnectedClientCount > 0)
            {
                ConnectedClientCount--;
                ClientCountChanged?.Invoke(this, ConnectedClientCount);
                ClientDisconnected?.Invoke(this, sessionId);
                logger.Info($"客戶端已斷開: {sessionId}，當前連接數: {ConnectedClientCount}");
                LogMessage?.Invoke(this, $"客戶端已斷開: {sessionId}，當前連接數: {ConnectedClientCount}");
            }
        }

        private void StartSessionMonitoring()
        {
            _sessionCheckTimer = new Timer(_sessionCheckIntervalMs);
            _sessionCheckTimer.Elapsed += CheckSessionCount;
            _sessionCheckTimer.AutoReset = true;
            _sessionCheckTimer.Start();
        }

        private void StopSessionMonitoring()
        {
            _sessionCheckTimer?.Stop();
            _sessionCheckTimer?.Dispose();
            _sessionCheckTimer = null;
        }

        private void CheckSessionCount(object sender, ElapsedEventArgs e)
        {
            try
            {
                if (!IsRunning || _server == null)
                    return;

                // 更新客戶端詳細信息
                _server.UpdateClientInfo();
                
                int currentSessionCount = _server.GetCurrentSessionCount();
                if (currentSessionCount != ConnectedClientCount)
                {
                    ConnectedClientCount = currentSessionCount;
                    ClientCountChanged?.Invoke(this, ConnectedClientCount);
                    logger.Debug($"會話計數更新: {ConnectedClientCount}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "檢查會話計數時發生錯誤");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Stop();
                StopSessionMonitoring();
                _server?.Dispose();
                _server = null;
                _application?.Stop();
                _application = null;
            }
        }
    }

    // 簡化的伺服器實現
    public class GatewayServer : StandardServer
    {
        private GatewayNodeManager _nodeManager;
        private SimpleRealOPCUAService _service;

        public GatewayServer(SimpleRealOPCUAService service)
        {
            _service = service;
            var logger = LogManager.GetCurrentClassLogger();
            logger.Info("GatewayServer 已初始化");
        }

        public int GetCurrentSessionCount()
        {
            try
            {
                return CurrentInstance?.SessionManager?.GetSessions()?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        public void UpdateClientInfo()
        {
            var logger = LogManager.GetCurrentClassLogger();
            try
            {
                var currentSessions = CurrentInstance?.SessionManager?.GetSessions()?.ToList() ?? new List<Session>();
                var currentSessionIds = currentSessions.Select(s => s.Id.ToString()).ToHashSet();

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) return;

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // 移除已斷開的客戶端
                        var disconnectedClients = _service.ConnectedClients
                            .Where(c => !currentSessionIds.Contains(c.SessionId)).ToList();
                        foreach (var client in disconnectedClients)
                        {
                            _service.ConnectedClients.Remove(client);
                            _service.OnClientDisconnected(client.SessionId);
                            logger.Info($"客戶端斷開連接: {client.DisplayName}");
                        }

                        // 添加新連接的客戶端
                        var existingSessionIds = _service.ConnectedClients
                            .Select(c => c.SessionId).ToHashSet();
                        foreach (var session in currentSessions)
                        {
                            if (!existingSessionIds.Contains(session.Id.ToString()))
                            {
                                var clientInfo = new ClientInfo
                                {
                                    SessionId = session.Id.ToString(),
                                    ClientName = "Connected Client",
                                    EndpointUrl = _service.EndpointUrl ?? "Unknown",
                                    ConnectedTime = DateTime.Now,
                                    UserIdentity = "Anonymous",
                                    SubscriptionCount = 0,
                                    SessionTimeout = TimeSpan.FromMinutes(60)
                                };

                                _service.ConnectedClients.Add(clientInfo);
                                _service.OnClientConnected(clientInfo);
                                logger.Info($"新客戶端連接: {clientInfo.DisplayName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "在UI執行緒更新客戶端信息時發生錯誤");
                    }
                }));
            }
            catch (Exception ex)
            {
                logger.Error(ex, "更新客戶端信息時發生錯誤");
            }
        }

        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logger.Info("正在創建節點管理器");
            
            _nodeManager = new GatewayNodeManager(server, configuration);
            var masterNodeManager = new MasterNodeManager(server, configuration, null, _nodeManager);
            
            logger.Info("節點管理器創建完成");
            return masterNodeManager;
        }

        public void AddNode(OPCUANode node)
        {
            _nodeManager?.AddNode(node);
        }

        public void RemoveNode(OPCUANode node)
        {
            _nodeManager?.RemoveNode(node);
        }

        public void UpdateNodeValue(OPCUANode node)
        {
            _nodeManager?.UpdateNodeValue(node);
        }

        public GatewayNodeManager GetNodeManager()
        {
            return _nodeManager;
        }
    }

    // 簡化的節點管理器
    public class GatewayNodeManager : CustomNodeManager2
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly Dictionary<string, NodeId> _nodeIdMap = new Dictionary<string, NodeId>();
        private FolderState _gatewayFolder;

        public GatewayNodeManager(IServerInternal server, ApplicationConfiguration configuration)
            : base(server, configuration, "http://opcgateway.com")
        {
            logger.Info("GatewayNodeManager 已初始化");
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            logger.Info("開始創建OPC UA地址空間");
            
            lock (Lock)
            {
                try
                {
                    LoadPredefinedNodes(SystemContext, externalReferences);
                    logger.Info("預定義節點已加載");
                    
                    // 創建Gateway資料夾
                    _gatewayFolder = CreateFolder(null, "Gateway", "Gateway");
                    
                    // 設置到Objects的引用
                    _gatewayFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                    
                    // 添加到地址空間
                    AddPredefinedNode(SystemContext, _gatewayFolder);
                    
                    // 創建反向引用讓Objects指向Gateway
                    var objectsRef = new ReferenceNode();
                    objectsRef.ReferenceTypeId = ReferenceTypes.Organizes;
                    objectsRef.IsInverse = false;
                    objectsRef.TargetId = _gatewayFolder.NodeId;
                    
                    if (!externalReferences.ContainsKey(ObjectIds.ObjectsFolder))
                    {
                        externalReferences[ObjectIds.ObjectsFolder] = new List<IReference>();
                    }
                    externalReferences[ObjectIds.ObjectsFolder].Add(objectsRef);
                    
                    logger.Info($"Gateway資料夾已創建，NodeId: {_gatewayFolder.NodeId}");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "創建地址空間時發生錯誤");
                }
            }
        }

        public void AddNode(OPCUANode node)
        {
            logger.Info($"添加OPC UA節點: {node?.BrowseName}");
            
            try
            {
                lock (Lock)
                {
                    if (_gatewayFolder == null)
                    {
                        logger.Error("Gateway資料夾未創建，無法添加節點");
                        return;
                    }
                    
                    if (_nodeIdMap.ContainsKey(node.SourceItemId))
                    {
                        logger.Warn($"節點已存在: {node.SourceItemId}");
                        return;
                    }
                    
                    // 創建變數節點
                    var variable = new BaseDataVariableState(_gatewayFolder);
                    variable.NodeId = new NodeId(node.BrowseName, NamespaceIndex);
                    variable.BrowseName = new QualifiedName(node.BrowseName, NamespaceIndex);
                    variable.DisplayName = new LocalizedText(node.BrowseName);
                    variable.TypeDefinitionId = VariableTypeIds.BaseDataVariableType;
                    variable.ReferenceTypeId = ReferenceTypes.HasComponent;
                    variable.DataType = DataTypeIds.BaseDataType;
                    variable.ValueRank = ValueRanks.Scalar;
                    variable.AccessLevel = AccessLevels.CurrentRead;
                    variable.UserAccessLevel = AccessLevels.CurrentRead;
                    
                    // 設置初始值
                    var initialValue = node.Value ?? 0;
                    variable.Value = new Variant(initialValue);
                    variable.StatusCode = StatusCodes.Good;
                    variable.Timestamp = node.Timestamp;
                    
                    // 添加到父資料夾
                    _gatewayFolder.AddChild(variable);
                    
                    // 儲存節點映射
                    _nodeIdMap[node.SourceItemId] = variable.NodeId;
                    
                    // 添加到地址空間
                    AddPredefinedNode(SystemContext, variable);
                    
                    logger.Info($"OPC UA節點創建成功: {node.BrowseName}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"添加OPC UA節點失敗: {node?.SourceItemId}");
            }
        }

        public void RemoveNode(OPCUANode node)
        {
            try
            {
                lock (Lock)
                {
                    if (_nodeIdMap.TryGetValue(node.SourceItemId, out NodeId nodeId))
                    {
                        // 從地址空間移除節點
                        var existingNode = FindPredefinedNode(nodeId, typeof(BaseInstanceState));
                        if (existingNode != null)
                        {
                            RemovePredefinedNode(SystemContext, existingNode, new List<LocalReference>());
                            _nodeIdMap.Remove(node.SourceItemId);
                            logger.Info($"成功移除 OPC UA 節點: {node.BrowseName}");
                        }
                    }
                    else
                    {
                        logger.Warn($"未找到要移除的節點: {node.SourceItemId}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"移除節點失敗: {node?.SourceItemId}");
            }
        }

        public void UpdateNodeValue(OPCUANode node)
        {
            try
            {
                lock (Lock)
                {
                    if (_nodeIdMap.TryGetValue(node.SourceItemId, out NodeId nodeId))
                    {
                        var variable = FindPredefinedNode(nodeId, typeof(BaseVariableState)) as BaseVariableState;
                        if (variable != null)
                        {
                            variable.Value = new Variant(node.Value);
                            variable.StatusCode = StatusCodes.Good;
                            variable.Timestamp = node.Timestamp;
                            variable.ClearChangeMasks(SystemContext, false);
                            
                            logger.Debug($"更新節點值: {node.SourceItemId} = {node.Value}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"更新節點值失敗: {node?.SourceItemId}");
            }
        }

        private FolderState CreateFolder(NodeState parent, string path, string name)
        {
            FolderState folder = new FolderState(parent);
            folder.SymbolicName = name;
            folder.ReferenceTypeId = ReferenceTypes.Organizes;
            folder.TypeDefinitionId = ObjectTypeIds.FolderType;
            folder.NodeId = new NodeId(path, NamespaceIndex);
            folder.BrowseName = new QualifiedName(path, NamespaceIndex);
            folder.DisplayName = new LocalizedText("en", name);
            folder.WriteMask = AttributeWriteMask.None;
            folder.UserWriteMask = AttributeWriteMask.None;
            folder.EventNotifier = EventNotifiers.None;

            if (parent != null)
            {
                parent.AddChild(folder);
            }

            return folder;
        }

        private BaseDataVariableState CreateVariable(NodeState parent, string path, string name, NodeId dataType, int valueRank)
        {
            BaseDataVariableState variable = new BaseDataVariableState(parent);
            variable.SymbolicName = name;
            variable.ReferenceTypeId = ReferenceTypes.HasComponent;
            variable.TypeDefinitionId = VariableTypeIds.BaseDataVariableType;
            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.BrowseName = new QualifiedName(path, NamespaceIndex);
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;
            variable.UserWriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;
            variable.DataType = dataType;
            variable.ValueRank = valueRank;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = null;
            variable.StatusCode = StatusCodes.Bad;
            variable.Timestamp = DateTime.UtcNow;

            if (parent != null)
            {
                parent.AddChild(variable);
            }

            return variable;
        }
    }
}