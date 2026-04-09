using System;
using System.IO;
using System.Windows;
using NLog;
using OPCGatewayTool.Services;

namespace OPCGatewayTool
{
    public partial class App : Application
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private LogService _logService;

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                // 確保必要的目錄存在
                EnsureDirectories();
                
                // 初始化日誌服務
                _logService = new LogService();
                
                // 初始化全域異常處理
                ExceptionHandler.Initialize(_logService);
                
                logger.Info("應用程式啟動");
                _logService.AddInfo("OPC Gateway 應用程式啟動");
                
                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "應用程式啟動時發生錯誤");
                
                var message = $"應用程式啟動失敗: {ex.Message}\n\n詳細錯誤:\n{ex}";
                MessageBox.Show(message, "啟動錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                
                Environment.Exit(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                logger.Info("應用程式正在關閉");
                _logService?.AddInfo("OPC Gateway 應用程式關閉");
                
                // 確保所有視窗都已正確清理
                foreach (Window window in Windows)
                {
                    if (window is MainWindow mainWindow)
                    {
                        try
                        {
                            // 確保主視窗的ViewModel已被清理
                            if (mainWindow.DataContext is IDisposable disposableViewModel)
                            {
                                disposableViewModel.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "清理主視窗資源時發生錯誤");
                        }
                    }
                }
                
                // 清理日誌服務
                _logService?.Dispose();
                
                // 強制垃圾收集以確保COM對象被釋放
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                logger.Info("應用程式已關閉");
                base.OnExit(e);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "應用程式關閉時發生錯誤");
            }
        }

        private void EnsureDirectories()
        {
            try
            {
                // 創建必要的目錄
                var directories = new[]
                {
                    "Config",
                    "Logs",
                    "Resources"
                };

                foreach (var dir in directories)
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                        logger.Info($"創建目錄: {dir}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "創建目錄時發生錯誤");
            }
        }
    }
}