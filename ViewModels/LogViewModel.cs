using System;
using System.Text;
using System.Windows.Input;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;

namespace OPCGatewayTool.ViewModels
{
    /// <summary>
    /// 負責日誌累積、批次刷新 UI、清除，以及每秒更新時間。
    /// </summary>
    public class LogViewModel : ObservableObject
    {
        private readonly StringBuilder _logBuilder = new StringBuilder();
        private string _logMessages = "";
        private DateTime _currentTime = DateTime.Now;
        private volatile bool _logDirty;

        public string LogMessages
        {
            get => _logMessages;
            private set => SetProperty(ref _logMessages, value);
        }

        public DateTime CurrentTime
        {
            get => _currentTime;
            set => SetProperty(ref _currentTime, value);
        }

        public ICommand ClearLogCommand { get; }

        public LogViewModel()
        {
            ClearLogCommand = new RelayCommand(ClearLog);
        }

        /// <summary>
        /// 附加一條日誌訊息。執行緒安全，且不會立即刷新 UI（由 FlushIfDirty 批次刷新）。
        /// </summary>
        public void AddMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            lock (_logBuilder)
            {
                _logBuilder.AppendLine($"[{timestamp}] {message}");

                // 限制日誌長度，保留最後 40000 字元
                if (_logBuilder.Length > 50000)
                {
                    var text = _logBuilder.ToString();
                    _logBuilder.Clear();
                    _logBuilder.Append(text.Substring(text.Length - 40000));
                }
            }

            _logDirty = true;
        }

        /// <summary>
        /// 清除所有日誌訊息。
        /// </summary>
        public void ClearLog()
        {
            lock (_logBuilder)
            {
                _logBuilder.Clear();
            }
            LogMessages = "";
        }

        /// <summary>
        /// 由 MainViewModel 的 UI Timer 呼叫，將 StringBuilder 內容批次刷新到 UI 綁定的屬性。
        /// </summary>
        public void FlushIfDirty()
        {
            if (_logDirty)
            {
                _logDirty = false;
                lock (_logBuilder)
                {
                    LogMessages = _logBuilder.ToString();
                }
            }
        }
    }
}
