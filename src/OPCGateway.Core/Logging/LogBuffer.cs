namespace OPCGateway.Core.Logging;

public enum LogSeverity
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Fatal = 5,
}

public sealed class LogEntry
{
    public LogEntry(DateTime timestamp, LogSeverity severity, string logger, string message, string? exception = null)
    {
        Timestamp = timestamp;
        Severity = severity;
        Logger = logger;
        Message = message;
        Exception = exception;
    }

    public DateTime Timestamp { get; }
    public LogSeverity Severity { get; }
    public string Logger { get; }
    public string Message { get; }
    public string? Exception { get; }
    public bool HasException => !string.IsNullOrEmpty(Exception);

    /// <summary>來源類別的簡短名稱（去掉命名空間）。</summary>
    public string ShortLogger
    {
        get
        {
            var index = Logger.LastIndexOf('.');
            return index >= 0 && index < Logger.Length - 1 ? Logger.Substring(index + 1) : Logger;
        }
    }

    public override string ToString()
    {
        var text = $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Severity.ToString().ToUpperInvariant(),-5} {ShortLogger} {Message}";
        return Exception == null ? text : text + Environment.NewLine + Exception;
    }
}

/// <summary>
/// 執行緒安全的環形日誌緩衝區。日誌框架（NLog）透過自訂 Target 寫入，UI 訂閱事件顯示。
/// </summary>
public sealed class LogBuffer
{
    private readonly object _sync = new();
    private readonly LinkedList<LogEntry> _entries = new();
    private int _capacity;
    private long _errorCount;
    private long _warningCount;

    public LogBuffer(int capacity = 2000)
    {
        _capacity = Math.Max(100, capacity);
    }

    public event EventHandler<LogEntry>? EntryAdded;
    public event EventHandler? Cleared;

    public int Capacity
    {
        get => _capacity;
        set
        {
            lock (_sync)
            {
                _capacity = Math.Max(100, value);
                Trim();
            }
        }
    }

    public long ErrorCount => Interlocked.Read(ref _errorCount);
    public long WarningCount => Interlocked.Read(ref _warningCount);

    public void Add(LogEntry entry)
    {
        lock (_sync)
        {
            _entries.AddLast(entry);
            Trim();
        }

        if (entry.Severity >= LogSeverity.Error)
            Interlocked.Increment(ref _errorCount);
        else if (entry.Severity == LogSeverity.Warn)
            Interlocked.Increment(ref _warningCount);

        EntryAdded?.Invoke(this, entry);
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_sync)
            return _entries.ToArray();
    }

    public void Clear()
    {
        lock (_sync)
            _entries.Clear();
        Interlocked.Exchange(ref _errorCount, 0);
        Interlocked.Exchange(ref _warningCount, 0);
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    private void Trim()
    {
        while (_entries.Count > _capacity)
            _entries.RemoveFirst();
    }
}
