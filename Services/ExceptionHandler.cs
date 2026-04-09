using System;
using System.IO;
using System.Windows;
using System.Threading.Tasks;
using NLog;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace OPCGatewayTool.Services
{
    public static class ExceptionHandler
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private static LogService _logService;
        private static bool _isInitialized;

        public static event EventHandler<UnhandledExceptionEventArgs> UnhandledException;

        public static void Initialize(LogService logService = null)
        {
            if (_isInitialized)
                return;

            _logService = logService;

            // 設置未處理異常的處理器
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            
            // WPF 特定的異常處理
            if (Application.Current != null)
            {
                Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
            }

            _isInitialized = true;
            logger.Info("全域異常處理器已初始化");
        }

        public static void HandleException(Exception ex, string context = "", bool showMessageBox = true, bool logException = true)
        {
            try
            {
                var message = $"[{context}] 發生異常: {ex.Message}";
                
                if (logException)
                {
                    logger.Error(ex, message);
                    _logService?.AddError(message, ex);
                }

                if (showMessageBox)
                {
                    var userMessage = GetUserFriendlyMessage(ex, context);
                    ShowErrorMessage(userMessage);
                }
            }
            catch (Exception handlerEx)
            {
                // 避免在異常處理過程中發生無限遞迴
                logger.Fatal(handlerEx, "異常處理器本身發生異常");
                
                try
                {
                    MessageBox.Show(
                        $"系統發生嚴重錯誤，請重啟應用程式。\n詳細錯誤: {handlerEx.Message}",
                        "嚴重錯誤",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch
                {
                    // 如果連 MessageBox 都無法顯示，則記錄到事件日誌或控制台
                    Console.WriteLine($"Fatal Error: {handlerEx}");
                }
            }
        }

        public static void HandleOPCException(Exception ex, string opcOperation = "")
        {
            string context = string.IsNullOrEmpty(opcOperation) ? "OPC操作" : $"OPC操作-{opcOperation}";
            
            if (ex is COMException comEx)
            {
                var message = $"COM異常 (HRESULT: 0x{comEx.HResult:X8}): {GetCOMErrorDescription(comEx)}";
                HandleException(comEx, context, true, true);
            }
            else
            {
                HandleException(ex, context, true, true);
            }
        }

        public static void HandleUIException(Exception ex, string uiContext = "")
        {
            string context = string.IsNullOrEmpty(uiContext) ? "UI操作" : $"UI操作-{uiContext}";
            
            // UI異常通常需要顯示給使用者，但不需要終止程式
            HandleException(ex, context, true, true);
        }

        public static void HandleConfigurationException(Exception ex, string configContext = "")
        {
            string context = string.IsNullOrEmpty(configContext) ? "配置操作" : $"配置操作-{configContext}";
            HandleException(ex, context, true, true);
        }

        private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                var message = $"未處理的應用程式域異常 (終止: {e.IsTerminating})";
                
                logger.Fatal(ex, message);
                _logService?.AddError(message, ex);

                if (e.IsTerminating)
                {
                    // 嘗試保存重要數據或執行清理操作
                    PerformEmergencyCleanup();
                    
                    var userMessage = $"應用程式遇到嚴重錯誤並即將關閉。\n\n錯誤詳情: {ex?.Message ?? "未知錯誤"}";
                    MessageBox.Show(userMessage, "嚴重錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                UnhandledException?.Invoke(sender, new UnhandledExceptionEventArgs(ex, e.IsTerminating));
            }
            catch (Exception handlerEx)
            {
                logger.Fatal(handlerEx, "處理未處理異常時發生錯誤");
            }
        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                var message = "未觀察的任務異常";
                logger.Error(e.Exception, message);
                _logService?.AddError(message, e.Exception);
                
                // 標記異常已被處理，避免程式終止
                e.SetObserved();
            }
            catch (Exception handlerEx)
            {
                logger.Fatal(handlerEx, "處理未觀察任務異常時發生錯誤");
            }
        }

        private static void Current_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                var message = "WPF Dispatcher 未處理異常";
                logger.Error(e.Exception, message);
                _logService?.AddError(message, e.Exception);
                
                var userMessage = GetUserFriendlyMessage(e.Exception, "界面操作");
                ShowErrorMessage(userMessage);
                
                // 標記異常已被處理，避免程式終止
                e.Handled = true;
            }
            catch (Exception handlerEx)
            {
                logger.Fatal(handlerEx, "處理WPF異常時發生錯誤");
                e.Handled = false; // 如果處理失敗，讓程式終止
            }
        }

        private static string GetUserFriendlyMessage(Exception ex, string context)
        {
            if (ex == null)
                return $"在執行 {context} 時發生未知錯誤。";

            return ex switch
            {
                COMException comEx => $"OPC通訊錯誤: {GetCOMErrorDescription(comEx)}",
                UnauthorizedAccessException => $"權限不足: 無法執行 {context} 操作。請以管理員身份運行程式。",
                FileNotFoundException fileEx => $"找不到檔案: {fileEx.FileName ?? "未知檔案"}",
                DirectoryNotFoundException => "找不到指定的目錄。",
                TimeoutException => $"操作超時: {context} 執行時間過長。",
                InvalidOperationException => $"操作無效: {ex.Message}",
                ArgumentException => $"參數錯誤: {ex.Message}",
                NotImplementedException => $"功能尚未實現: {context}",
                OutOfMemoryException => "記憶體不足，請關閉其他應用程式後重試。",
                Win32Exception win32Ex => $"系統錯誤 ({win32Ex.NativeErrorCode}): {win32Ex.Message}",
                _ => $"在執行 {context} 時發生錯誤: {ex.Message}"
            };
        }

        private static string GetCOMErrorDescription(COMException comEx)
        {
            return comEx.HResult switch
            {
                unchecked((int)0x800401F3) => "OPC伺服器無效或未註冊 (INVALID_OBJECTID)",
                unchecked((int)0x800401F0) => "CoInitialize未調用 (CO_E_NOTINITIALIZED)", 
                unchecked((int)0x80040154) => "類別未註冊 (REGDB_E_CLASSNOTREG)",
                unchecked((int)0x800706BA) => "RPC伺服器無法使用 (RPC_S_SERVER_UNAVAILABLE)",
                unchecked((int)0x80040155) => "介面未註冊 (REGDB_E_IIDNOTREG)",
                unchecked((int)0x80010108) => "對象已斷開連接 (RPC_E_DISCONNECTED)",
                unchecked((int)0xC0040001) => "OPC伺服器錯誤: 無效的處理",
                unchecked((int)0xC0040004) => "OPC伺服器錯誤: 無效的項目ID",
                unchecked((int)0xC0040006) => "OPC伺服器錯誤: 未知的項目ID",
                unchecked((int)0xC0040007) => "OPC伺服器錯誤: 無效的項目路徑",
                unchecked((int)0xC0040008) => "OPC伺服器錯誤: 未知的路徑",
                _ => $"COM錯誤 (HRESULT: 0x{comEx.HResult:X8}): {comEx.Message}"
            };
        }

        private static void ShowErrorMessage(string message)
        {
            try
            {
                if (Application.Current?.Dispatcher?.CheckAccess() == true)
                {
                    MessageBox.Show(message, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        MessageBox.Show(message, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "顯示錯誤訊息時發生異常");
                
                // 備用方案：使用控制台輸出
                Console.WriteLine($"Error: {message}");
            }
        }

        private static void PerformEmergencyCleanup()
        {
            try
            {
                logger.Info("執行緊急清理操作");
                
                // 這裡可以添加緊急清理邏輯，例如：
                // 1. 保存重要配置
                // 2. 關閉OPC連接
                // 3. 釋放資源
                // 4. 通知其他組件準備關閉
                
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "執行緊急清理時發生異常");
            }
        }
    }

    public class UnhandledExceptionEventArgs : EventArgs
    {
        public Exception Exception { get; }
        public bool IsTerminating { get; }

        public UnhandledExceptionEventArgs(Exception exception, bool isTerminating)
        {
            Exception = exception;
            IsTerminating = isTerminating;
        }
    }
}