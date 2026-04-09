using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NLog.Targets;

namespace OPCGatewayTool.Services
{
    public class LogService : IDisposable
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly MemoryTarget _memoryTarget;
        private readonly Timer _logUpdateTimer;
        private bool _disposed;

        public event EventHandler<LogEntry> LogEntryAdded;
        public event EventHandler LogCleared;

        public ObservableCollection<LogEntry> LogEntries { get; }
        public int MaxLogEntries { get; set; } = 1000;

        public LogService()
        {
            LogEntries = new ObservableCollection<LogEntry>();
            
            // 找到記憶體目標
            _memoryTarget = LogManager.Configuration?.AllTargets
                .OfType<MemoryTarget>()
                .FirstOrDefault(t => t.Name == "memoryTarget");

            if (_memoryTarget == null)
            {
                logger.Warn("未找到記憶體日誌目標，創建新的記憶體目標");
                CreateMemoryTarget();
            }

            // 啟動定時器以定期更新日誌
            _logUpdateTimer = new Timer(UpdateLogs, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
            
            logger.Info("日誌服務已初始化");
        }

        public void AddLogEntry(LogLevel level, string message, Exception exception = null)
        {
            try
            {
                var entry = new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = level,
                    Message = message,
                    Exception = exception,
                    Logger = "Manual"
                };

                // 添加到集合
                if (LogEntries.Count >= MaxLogEntries)
                {
                    LogEntries.RemoveAt(0);
                }
                
                LogEntries.Add(entry);
                
                // 觸發事件
                LogEntryAdded?.Invoke(this, entry);
                
                // 同時記錄到NLog
                var logEvent = new LogEventInfo(GetNLogLevel(level), "Manual", message);
                if (exception != null)
                {
                    logEvent.Exception = exception;
                }
                
                logger.Log(logEvent);
            }
            catch (Exception ex)
            {
                // 避免在日誌記錄過程中出現無限遞迴
                System.Diagnostics.Debug.WriteLine($"記錄日誌時發生錯誤: {ex.Message}");
            }
        }

        public void AddInfo(string message)
        {
            AddLogEntry(LogLevel.Info, message);
        }

        public void AddWarning(string message)
        {
            AddLogEntry(LogLevel.Warn, message);
        }

        public void AddError(string message, Exception exception = null)
        {
            AddLogEntry(LogLevel.Error, message, exception);
        }

        public void AddDebug(string message)
        {
            AddLogEntry(LogLevel.Debug, message);
        }

        public void ClearLogs()
        {
            try
            {
                LogEntries.Clear();
                _memoryTarget?.Logs?.Clear();
                LogCleared?.Invoke(this, EventArgs.Empty);
                logger.Info("日誌已清除");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "清除日誌時發生錯誤");
            }
        }

        public List<LogEntry> GetLogs(LogLevel? minLevel = null, DateTime? since = null)
        {
            try
            {
                var query = LogEntries.AsEnumerable();

                if (minLevel != null)
                {
                    query = query.Where(e => e.Level >= minLevel);
                }

                if (since.HasValue)
                {
                    query = query.Where(e => e.Timestamp >= since.Value);
                }

                return query.ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "獲取日誌時發生錯誤");
                return new List<LogEntry>();
            }
        }

        public List<LogEntry> GetErrorLogs()
        {
            return GetLogs(LogLevel.Error);
        }

        public List<LogEntry> GetWarningLogs()
        {
            return GetLogs(LogLevel.Warn);
        }

        public string GetLogsAsString(LogLevel? minLevel = null, DateTime? since = null)
        {
            try
            {
                var logs = GetLogs(minLevel, since);
                return string.Join("\n", logs.Select(l => l.ToString()));
            }
            catch (Exception ex)
            {
                logger.Error(ex, "轉換日誌為字符串時發生錯誤");
                return "日誌獲取失敗";
            }
        }

        public async Task<bool> ExportLogsAsync(string filePath, LogLevel? minLevel = null)
        {
            return await Task.Run(() => ExportLogs(filePath, minLevel));
        }

        public bool ExportLogs(string filePath, LogLevel? minLevel = null)
        {
            try
            {
                var logs = GetLogs(minLevel);
                var logText = string.Join("\n", logs.Select(l => l.ToString()));
                
                System.IO.File.WriteAllText(filePath, logText);
                logger.Info($"日誌已匯出到: {filePath}");
                
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "匯出日誌時發生錯誤");
                return false;
            }
        }

        private void UpdateLogs(object state)
        {
            try
            {
                if (_memoryTarget?.Logs == null || _disposed)
                    return;

                // 從記憶體目標獲取新的日誌條目
                var logs = _memoryTarget.Logs.ToList();
                
                // 處理新的日誌條目
                foreach (var log in logs.Skip(LogEntries.Count))
                {
                    if (TryParseLogEntry(log, out LogEntry entry))
                    {
                        if (LogEntries.Count >= MaxLogEntries)
                        {
                            LogEntries.RemoveAt(0);
                        }
                        
                        LogEntries.Add(entry);
                        LogEntryAdded?.Invoke(this, entry);
                    }
                }
            }
            catch (Exception ex)
            {
                // 避免在更新過程中拋出異常
                System.Diagnostics.Debug.WriteLine($"更新日誌時發生錯誤: {ex.Message}");
            }
        }

        private void CreateMemoryTarget()
        {
            try
            {
                var config = LogManager.Configuration ?? new NLog.Config.LoggingConfiguration();
                
                var memTarget = new MemoryTarget("memoryTarget");
                memTarget.Layout = "${longdate} ${uppercase:${level}} ${logger} ${message} ${exception:format=tostring}";
                memTarget.MaxLogsCount = 5000;
                
                config.AddTarget(memTarget);
                config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, memTarget);
                
                LogManager.Configuration = config;
                
                logger.Info("已創建記憶體日誌目標");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "創建記憶體日誌目標時發生錯誤");
            }
        }

        private bool TryParseLogEntry(string logText, out LogEntry entry)
        {
            entry = null;
            
            try
            {
                if (string.IsNullOrWhiteSpace(logText))
                    return false;

                // 簡單的日誌解析 (假設格式: timestamp level logger message)
                var parts = logText.Split(new char[] { ' ' }, 4);
                if (parts.Length < 4)
                    return false;

                if (!DateTime.TryParse($"{parts[0]} {parts[1]}", out DateTime timestamp))
                    timestamp = DateTime.Now;

                if (!Enum.TryParse<LogLevel>(parts[2], true, out LogLevel level))
                    level = LogLevel.Info;

                var logger = parts.Length > 3 ? parts[3] : "Unknown";
                var message = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : logText;

                entry = new LogEntry
                {
                    Timestamp = timestamp,
                    Level = level,
                    Logger = logger,
                    Message = message
                };

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解析日誌條目時發生錯誤: {ex.Message}");
                return false;
            }
        }

        private NLog.LogLevel GetNLogLevel(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => NLog.LogLevel.Debug,
                LogLevel.Info => NLog.LogLevel.Info,
                LogLevel.Warn => NLog.LogLevel.Warn,
                LogLevel.Error => NLog.LogLevel.Error,
                LogLevel.Fatal => NLog.LogLevel.Fatal,
                _ => NLog.LogLevel.Info
            };
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;

                if (disposing)
                {
                    _logUpdateTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    _logUpdateTimer?.Dispose();
                }
            }
        }

        ~LogService()
        {
            Dispose(false);
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Logger { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }

        public string LevelString => Level.ToString().ToUpper();
        public string TimestampString => Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");

        public override string ToString()
        {
            var result = $"[{TimestampString}] {LevelString} {Logger} {Message}";
            
            if (Exception != null)
            {
                result += $"\n  Exception: {Exception.Message}";
                if (!string.IsNullOrEmpty(Exception.StackTrace))
                {
                    result += $"\n  StackTrace: {Exception.StackTrace}";
                }
            }
            
            return result;
        }
    }

    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
        Fatal = 4
    }
}