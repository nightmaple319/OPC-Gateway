using NLog;
using NLog.Targets;
using OPCGateway.Core.Logging;

namespace OPCGateway.App.Logging;

/// <summary>把 NLog 事件轉成 <see cref="LogEntry"/> 推進 UI 用的環形緩衝區。</summary>
[Target("LogBuffer")]
public sealed class LogBufferTarget : TargetWithLayout
{
    private readonly LogBuffer _buffer;

    public LogBufferTarget(LogBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        Name = "uiLogBuffer";
    }

    protected override void Write(LogEventInfo logEvent)
    {
        var loggerName = logEvent.LoggerName ?? string.Empty;

        // 第三方堆疊的 Debug 訊息不進 UI，避免淹沒使用者關心的事件
        if ((loggerName.StartsWith("Opc.Ua", StringComparison.Ordinal) || loggerName.StartsWith("Microsoft.", StringComparison.Ordinal))
            && logEvent.Level < LogLevel.Warn)
            return;

        var entry = new LogEntry(
            logEvent.TimeStamp,
            Map(logEvent.Level),
            loggerName,
            logEvent.FormattedMessage ?? string.Empty,
            logEvent.Exception?.ToString());

        _buffer.Add(entry);
    }

    private static LogSeverity Map(LogLevel level)
    {
        if (level == LogLevel.Trace) return LogSeverity.Trace;
        if (level == LogLevel.Debug) return LogSeverity.Debug;
        if (level == LogLevel.Info) return LogSeverity.Info;
        if (level == LogLevel.Warn) return LogSeverity.Warn;
        if (level == LogLevel.Error) return LogSeverity.Error;
        return LogSeverity.Fatal;
    }
}
