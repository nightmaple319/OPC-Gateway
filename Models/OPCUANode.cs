using System;
using System.ComponentModel;

namespace OPCGatewayTool.Models
{
    public class OPCUANode : INotifyPropertyChanged
    {
        private string _nodeId;
        private string _browseName;
        private string _displayName;
        private object _value;
        private string _statusCode;
        private DateTime _timestamp;
        private Type _dataType;
        private string _description;

        public string NodeId
        {
            get => _nodeId;
            set
            {
                _nodeId = value;
                OnPropertyChanged(nameof(NodeId));
            }
        }

        public string BrowseName
        {
            get => _browseName;
            set
            {
                _browseName = value;
                OnPropertyChanged(nameof(BrowseName));
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                _displayName = value;
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public object Value
        {
            get => _value;
            set
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
                OnPropertyChanged(nameof(ValueString));
            }
        }

        public string ValueString => _value?.ToString() ?? "N/A";

        public string StatusCode
        {
            get => _statusCode;
            set
            {
                _statusCode = value;
                OnPropertyChanged(nameof(StatusCode));
                OnPropertyChanged(nameof(StatusString));
            }
        }

        public string StatusString => StatusCode?.ToString() ?? "Good";

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                _timestamp = value;
                OnPropertyChanged(nameof(Timestamp));
                OnPropertyChanged(nameof(TimestampString));
            }
        }

        public string TimestampString => _timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");

        public Type DataType
        {
            get => _dataType;
            set
            {
                _dataType = value;
                OnPropertyChanged(nameof(DataType));
                OnPropertyChanged(nameof(DataTypeString));
            }
        }

        public string DataTypeString => _dataType?.Name ?? "Unknown";

        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                OnPropertyChanged(nameof(Description));
            }
        }

        public string SourceItemId { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}