using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using OPCGateway.Core.Configuration;
using OPCGateway.Core.Da;
using OPCGateway.Core.Ua;

namespace OPCGateway.Core.Engine;

public enum TagState
{
    /// <summary>已定義但尚未訂閱（DA 未連線）。</summary>
    Idle,
    /// <summary>使用者停用。</summary>
    Disabled,
    /// <summary>已訂閱，等待第一筆資料。</summary>
    WaitingForData,
    /// <summary>持續收到資料。</summary>
    Active,
    /// <summary>訂閱失敗（例如 ItemId 不存在）。</summary>
    Error,
}

/// <summary>
/// 單一標籤的「單一真相」：同時包含設定（ItemId、BrowseName、Enabled）與即時值。
/// 設定屬性變更會立即觸發 PropertyChanged；即時值由引擎以批次方式透過 <see cref="NotifyLiveChanged"/> 通知。
/// </summary>
public sealed class TagMapping : INotifyPropertyChanged
{
    private string _browseName;
    private string? _description;
    private bool _enabled;
    private bool _allowWrite;

    private object? _value;
    private short _quality = DaQuality.BadNotConnected;
    private DateTime? _timestampUtc;
    private Type? _dataType;
    private uint _uaStatusCode = Opc.Ua.StatusCodes.BadNotConnected;
    private TagState _state = TagState.Idle;
    private string? _errorMessage;
    private long _updateCount;
    private DateTime? _lastUpdateUtc;

    public TagMapping(string itemId, string[] folderPath, string browseName, string? description, bool enabled, bool allowWrite)
    {
        ItemId = itemId;
        FolderPath = folderPath;
        _browseName = browseName;
        _description = description;
        _enabled = enabled;
        _allowWrite = allowWrite;
        if (!enabled)
            _state = TagState.Disabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ---------- 設定 ----------

    public string ItemId { get; }
    public string[] FolderPath { get; }
    public string FolderPathText => string.Join(" / ", FolderPath);

    public string BrowseName
    {
        get => _browseName;
        set => SetField(ref _browseName, value);
    }

    public string? Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public bool AllowWrite
    {
        get => _allowWrite;
        set => SetField(ref _allowWrite, value);
    }

    public string NodeIdText => $"s={ItemId}";

    // ---------- 即時值（由引擎更新，批次通知） ----------

    public object? Value => _value;
    public short Quality => _quality;
    public DateTime? TimestampUtc => _timestampUtc;
    public DateTime? TimestampLocal => _timestampUtc?.ToLocalTime();
    public Type? DataType => _dataType;
    public uint UaStatusCode => _uaStatusCode;
    public TagState State => _state;
    public string? ErrorMessage => _errorMessage;
    public long UpdateCount => Interlocked.Read(ref _updateCount);
    public DateTime? LastUpdateUtc => _lastUpdateUtc;

    public DaQualityMaster QualityMaster => DaQuality.GetMaster(_quality);
    public string QualityText => DaQuality.ToText(_quality);
    public string UaStatusText => UaStatusMapper.ToText(_uaStatusCode);
    public string DataTypeName => UaTypeMapper.GetTypeDisplayName(_dataType);
    public string ValueText => FormatValue(_value);

    public string StateText => _state switch
    {
        TagState.Idle => "待連線",
        TagState.Disabled => "已停用",
        TagState.WaitingForData => "等待資料",
        TagState.Active => "運作中",
        TagState.Error => "錯誤",
        _ => _state.ToString(),
    };

    internal void SetLiveValue(object? value, short quality, DateTime timestampUtc, uint uaStatusCode)
    {
        _value = value;
        _quality = quality;
        _timestampUtc = timestampUtc;
        _uaStatusCode = uaStatusCode;
        _state = TagState.Active;
        _errorMessage = null;
        _lastUpdateUtc = DateTime.UtcNow;
        Interlocked.Increment(ref _updateCount);
        if (_dataType == null && value != null)
            _dataType = value.GetType();
    }

    internal void SetState(TagState state, uint uaStatusCode, string? errorMessage = null)
    {
        _state = state;
        _uaStatusCode = uaStatusCode;
        _errorMessage = errorMessage;
        if (state != TagState.Active)
            _quality = state == TagState.WaitingForData ? DaQuality.BadWaitingForInitialData : DaQuality.BadNotConnected;
    }

    internal void SetDataType(Type? type)
    {
        if (type != null)
            _dataType = type;
    }

    /// <summary>在 UI 執行緒呼叫，一次通知所有即時屬性。</summary>
    public void NotifyLiveChanged()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(Quality));
        OnPropertyChanged(nameof(QualityText));
        OnPropertyChanged(nameof(QualityMaster));
        OnPropertyChanged(nameof(TimestampUtc));
        OnPropertyChanged(nameof(TimestampLocal));
        OnPropertyChanged(nameof(DataType));
        OnPropertyChanged(nameof(DataTypeName));
        OnPropertyChanged(nameof(UaStatusCode));
        OnPropertyChanged(nameof(UaStatusText));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(LastUpdateUtc));
    }

    public TagMappingConfig ToConfig() => new()
    {
        ItemId = ItemId,
        BrowseName = BrowseName,
        Description = Description,
        Enabled = Enabled,
        AllowWrite = AllowWrite,
    };

    public static string FormatValue(object? value)
    {
        switch (value)
        {
            case null:
                return "—";
            case string s:
                return s;
            case double d:
                return d.ToString("0.###", CultureInfo.InvariantCulture);
            case float f:
                return f.ToString("0.###", CultureInfo.InvariantCulture);
            case decimal m:
                return m.ToString("0.###", CultureInfo.InvariantCulture);
            case DateTime dt:
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            case bool b:
                return b ? "True" : "False";
            case Array array:
            {
                var items = array.Cast<object?>().Take(8).Select(FormatValue);
                var text = "[" + string.Join(", ", items);
                return array.Length > 8 ? text + $", … ({array.Length})]" : text + "]";
            }
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
