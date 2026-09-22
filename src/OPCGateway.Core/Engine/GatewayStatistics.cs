namespace OPCGateway.Core.Engine;

/// <summary>執行緒安全的傳輸統計。</summary>
public sealed class GatewayStatistics
{
    private long _totalUpdates;
    private long _badQualityUpdates;
    private long _droppedUpdates;
    private long _writeRequests;
    private long _windowCount;
    private long _lastUpdateTicks;
    private DateTime _windowStartUtc = DateTime.UtcNow;
    private double _updatesPerSecond;
    private DateTime? _startedAtUtc;

    public long TotalUpdates => Interlocked.Read(ref _totalUpdates);
    public long BadQualityUpdates => Interlocked.Read(ref _badQualityUpdates);
    public long DroppedUpdates => Interlocked.Read(ref _droppedUpdates);
    public long WriteRequests => Interlocked.Read(ref _writeRequests);
    public double UpdatesPerSecond => Volatile.Read(ref _updatesPerSecond);
    public DateTime? StartedAtUtc => _startedAtUtc;

    public DateTime? LastUpdateUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastUpdateTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public TimeSpan Uptime => _startedAtUtc.HasValue ? DateTime.UtcNow - _startedAtUtc.Value : TimeSpan.Zero;

    public void MarkStarted()
    {
        _startedAtUtc = DateTime.UtcNow;
        _windowStartUtc = DateTime.UtcNow;
    }

    public void MarkStopped()
    {
        _startedAtUtc = null;
        Volatile.Write(ref _updatesPerSecond, 0);
    }

    public void RecordUpdate(bool goodQuality)
    {
        Interlocked.Increment(ref _totalUpdates);
        Interlocked.Increment(ref _windowCount);
        if (!goodQuality)
            Interlocked.Increment(ref _badQualityUpdates);
        Interlocked.Exchange(ref _lastUpdateTicks, DateTime.UtcNow.Ticks);
    }

    public void RecordDropped() => Interlocked.Increment(ref _droppedUpdates);
    public void RecordWrite() => Interlocked.Increment(ref _writeRequests);

    /// <summary>每秒呼叫一次，重新計算每秒更新數。</summary>
    public void Tick()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _windowStartUtc).TotalSeconds;
        if (elapsed < 0.5)
            return;

        var count = Interlocked.Exchange(ref _windowCount, 0);
        _windowStartUtc = now;
        Volatile.Write(ref _updatesPerSecond, count / elapsed);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _totalUpdates, 0);
        Interlocked.Exchange(ref _badQualityUpdates, 0);
        Interlocked.Exchange(ref _droppedUpdates, 0);
        Interlocked.Exchange(ref _writeRequests, 0);
        Interlocked.Exchange(ref _windowCount, 0);
        Interlocked.Exchange(ref _lastUpdateTicks, 0);
        Volatile.Write(ref _updatesPerSecond, 0);
        _windowStartUtc = DateTime.UtcNow;
    }
}
