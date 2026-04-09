using System;
using System.ComponentModel;

namespace OPCGatewayTool.Models
{
    public class OPCDAItem : INotifyPropertyChanged
    {
        private string _itemId;
        private object _value;
        private short _quality;
        private DateTime _timestamp;
        private bool _isSelected;
        private string _dataType;

        public string ItemId
        {
            get => _itemId;
            set
            {
                _itemId = value;
                OnPropertyChanged(nameof(ItemId));
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

        public short Quality
        {
            get => _quality;
            set
            {
                _quality = value;
                OnPropertyChanged(nameof(Quality));
                OnPropertyChanged(nameof(QualityString));
            }
        }

        public string QualityString
        {
            get
            {
                return _quality switch
                {
                    192 => "Good",
                    64 => "Uncertain",
                    0 => "Bad",
                    _ => $"Quality({_quality})"
                };
            }
        }

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

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public string DataType
        {
            get => _dataType;
            set
            {
                _dataType = value;
                OnPropertyChanged(nameof(DataType));
            }
        }

        public int ClientHandle { get; set; }
        public int ServerHandle { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}