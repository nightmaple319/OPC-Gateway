using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using OPCGateway.App.Logging;
using OPCGateway.App.Services;
using OPCGateway.App.ViewModels;
using OPCGateway.App.Views;
using OPCGateway.App.Views.Pages;
using OPCGateway.Core.Configuration;
using OPCGateway.Core.Da;
using OPCGateway.Core.Engine;
using OPCGateway.Core.Logging;
using OPCGateway.Core.Ua;
using Wpf.Ui.Abstractions;

namespace OPCGateway.App;

public partial class App : Application
{
    private static readonly NLog.Logger BootLogger = LogManager.GetCurrentClassLogger();
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            var logBuffer = ConfigureLogging();

            _host = Host.CreateDefaultBuilder(e.Args)
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
                    logging.AddNLog();
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton(logBuffer);
                    services.AddSingleton<ConfigStore>(sp => new ConfigStore(sp.GetRequiredService<ILogger<ConfigStore>>()));
                    services.AddSingleton<IDaClient, TitaniumDaClient>();
                    services.AddSingleton<IUaServerHost>(sp =>
                        new UaServerHost(sp.GetRequiredService<ILogger<UaServerHost>>(), builder => builder.AddNLog()));
                    services.AddSingleton<GatewayEngine>();
                    services.AddSingleton<ThemeService>();
                    services.AddSingleton<INavigationViewPageProvider, NavigationPageProvider>();

                    services.AddSingleton<ShellViewModel>();
                    services.AddSingleton<DashboardViewModel>();
                    services.AddSingleton<MappingViewModel>();
                    services.AddSingleton<SettingsViewModel>();
                    services.AddSingleton<LogViewModel>();

                    services.AddSingleton<DashboardPage>();
                    services.AddSingleton<MappingPage>();
                    services.AddSingleton<SettingsPage>();
                    services.AddSingleton<LogPage>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            _host.Start();

            // 先讀設定裡的主題再顯示視窗，避免先以系統主題畫一次再切換的閃爍
            GatewayConfig? config = null;
            try
            {
                config = _host.Services.GetRequiredService<ConfigStore>().Load();
                _host.Services.GetRequiredService<ThemeService>().Apply(ThemeService.Parse(config.Options.Theme));
            }
            catch (Exception ex)
            {
                BootLogger.Warn(ex, "預先載入設定失敗，改由主視窗初始化時處理");
            }

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();

            var shell = _host.Services.GetRequiredService<ShellViewModel>();
            _ = shell.InitializeAsync(config);
        }
        catch (Exception ex)
        {
            BootLogger.Fatal(ex, "應用程式啟動失敗");
            MessageBox.Show($"應用程式啟動失敗：{ex.Message}\n\n{ex}", "OPC Gateway", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host != null)
            {
                var shell = _host.Services.GetService<ShellViewModel>();
                shell?.SaveConfigQuietly();

                var engine = _host.Services.GetService<GatewayEngine>();
                if (engine != null)
                {
                    try { engine.StopAsync().Wait(TimeSpan.FromSeconds(10)); }
                    catch (Exception ex) { BootLogger.Warn(ex, "關閉時停止閘道失敗"); }
                }

                _host.StopAsync(TimeSpan.FromSeconds(5)).Wait();
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            BootLogger.Error(ex, "應用程式關閉時發生錯誤");
        }
        finally
        {
            LogManager.Shutdown();
            base.OnExit(e);
        }
    }

    private static LogBuffer ConfigureLogging()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "NLog.config");
        if (File.Exists(configPath))
            LogManager.Setup().LoadConfigurationFromFile(configPath);

        var buffer = new LogBuffer(2000);
        var target = new LogBufferTarget(buffer);

        var configuration = LogManager.Configuration ?? new NLog.Config.LoggingConfiguration();
        configuration.AddTarget(target);
        configuration.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, target, "*");
        LogManager.Configuration = configuration;
        LogManager.ReconfigExistingLoggers();

        BootLogger.Info("OPC Gateway {Version} 啟動", typeof(App).Assembly.GetName().Version);
        return buffer;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        BootLogger.Error(e.Exception, "UI 執行緒未處理的例外");
        MessageBox.Show($"發生未預期的錯誤：{e.Exception.Message}\n\n詳細資訊已寫入 Logs 資料夾。", "OPC Gateway", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        BootLogger.Fatal(e.ExceptionObject as Exception, "未處理的例外（IsTerminating={Terminating}）", e.IsTerminating);
        LogManager.Flush();
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();

        // OPC UA 堆疊 1.5.378 在伺服器啟動時會重建 RequestQueue，舊佇列的工作迴圈以
        // ArgumentNullException（SemaphoreSlim 已釋放）結束。這是堆疊內部行為，不影響伺服器運作，
        // 降為 Debug 以免污染 UI 的錯誤計數；其他未觀察例外仍以 Error 記錄。
        var flattened = e.Exception.Flatten();
        var isUaStackNoise = flattened.InnerExceptions.All(ex =>
            ex is ArgumentNullException && (ex.StackTrace?.Contains("Opc.Ua.ServerBase") ?? false));

        if (isUaStackNoise)
            BootLogger.Debug(e.Exception, "OPC UA 堆疊內部的未觀察工作例外（已知，可忽略）");
        else
            BootLogger.Error(e.Exception, "未觀察的工作例外");
    }
}
