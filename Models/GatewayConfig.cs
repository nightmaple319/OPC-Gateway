using System;
using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;

namespace OPCGatewayTool.Models
{
    public class GatewayConfig : INotifyPropertyChanged
    {
        public OPCDAConfig OPCDAConfig { get; set; } = new OPCDAConfig();
        public OPCUAConfig OPCUAConfig { get; set; } = new OPCUAConfig();
        public List<ItemMapping> ItemMappings { get; set; } = new List<ItemMapping>();
        public LoggingConfig LoggingConfig { get; set; } = new LoggingConfig();

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class OPCDAConfig : INotifyPropertyChanged
    {
        private string _serverName = "Matrikon.OPC.Simulation.1";
        private string _serverProgId = "Matrikon.OPC.Simulation";
        private int _updateRate = 1000;
        private bool _useLocalHost = true;
        private string _hostName = "localhost";

        public string ServerName
        {
            get => _serverName;
            set
            {
                _serverName = value;
                OnPropertyChanged(nameof(ServerName));
            }
        }

        public string ServerProgId
        {
            get => _serverProgId;
            set
            {
                _serverProgId = value;
                OnPropertyChanged(nameof(ServerProgId));
            }
        }

        public int UpdateRate
        {
            get => _updateRate;
            set
            {
                _updateRate = value;
                OnPropertyChanged(nameof(UpdateRate));
            }
        }

        public bool UseLocalHost
        {
            get => _useLocalHost;
            set
            {
                _useLocalHost = value;
                OnPropertyChanged(nameof(UseLocalHost));
            }
        }

        public string HostName
        {
            get => _hostName;
            set
            {
                _hostName = value;
                OnPropertyChanged(nameof(HostName));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class OPCUAConfig : INotifyPropertyChanged
    {
        private string _serverName = "OPC Gateway Server";
        private int _port = 4840;
        private string _applicationName = "OPC DA to UA Gateway";
        private string _applicationUri = "urn:localhost:OPCGateway";
        private bool _enableSecurity = false;
        private int _maxClients = 100;

        public string ServerName
        {
            get => _serverName;
            set
            {
                _serverName = value;
                OnPropertyChanged(nameof(ServerName));
            }
        }

        public int Port
        {
            get => _port;
            set
            {
                _port = value;
                OnPropertyChanged(nameof(Port));
            }
        }

        public string ApplicationName
        {
            get => _applicationName;
            set
            {
                _applicationName = value;
                OnPropertyChanged(nameof(ApplicationName));
            }
        }

        public string ApplicationUri
        {
            get => _applicationUri;
            set
            {
                _applicationUri = value;
                OnPropertyChanged(nameof(ApplicationUri));
            }
        }

        public bool EnableSecurity
        {
            get => _enableSecurity;
            set
            {
                _enableSecurity = value;
                OnPropertyChanged(nameof(EnableSecurity));
            }
        }

        public int MaxClients
        {
            get => _maxClients;
            set
            {
                _maxClients = value;
                OnPropertyChanged(nameof(MaxClients));
            }
        }

        [JsonIgnore]
        public string EndpointUrl => $"opc.tcp://localhost:{Port}";

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ItemMapping : INotifyPropertyChanged
    {
        private string _opcDaItemId;
        private string _opcUaNodeId;
        private string _opcUaBrowseName;
        private bool _isEnabled = true;

        public string OPCDAItemId
        {
            get => _opcDaItemId;
            set
            {
                _opcDaItemId = value;
                OnPropertyChanged(nameof(OPCDAItemId));
            }
        }

        public string OPCUANodeId
        {
            get => _opcUaNodeId;
            set
            {
                _opcUaNodeId = value;
                OnPropertyChanged(nameof(OPCUANodeId));
            }
        }

        public string OPCUABrowseName
        {
            get => _opcUaBrowseName;
            set
            {
                _opcUaBrowseName = value;
                OnPropertyChanged(nameof(OPCUABrowseName));
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class LoggingConfig
    {
        public string LogLevel { get; set; } = "Info";
        public string LogFilePath { get; set; } = "Logs/gateway.log";
        public int MaxFileSize { get; set; } = 10;
        public int MaxFiles { get; set; } = 5;
    }
}