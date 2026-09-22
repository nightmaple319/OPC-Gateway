using System.ComponentModel;
using System.Windows;
using OPCGateway.App.Services;
using OPCGateway.App.ViewModels;
using OPCGateway.App.Views.Pages;
using OPCGateway.Core.Engine;
using Wpf.Ui.Abstractions;

namespace OPCGateway.App.Views;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly GatewayEngine _engine;
    private bool _closeConfirmed;

    public MainWindow(ShellViewModel viewModel, GatewayEngine engine, INavigationViewPageProvider pageProvider, ThemeService themeService)
    {
        _viewModel = viewModel;
        _engine = engine;
        DataContext = viewModel;
        InitializeComponent();

        RootNavigation.SetPageProviderService(pageProvider);
        Loaded += (_, _) =>
        {
            themeService.Attach(this);
            RootNavigation.Navigate(typeof(DashboardPage));
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closeConfirmed && _engine.State != GatewayRunState.Stopped)
        {
            var result = MessageBox.Show(
                "閘道正在運作中。關閉程式會停止 OPC UA 伺服器並中斷 OPC DA 連線，已連線的 UA 客戶端將收到斷線。\n\n確定要關閉嗎？",
                "關閉 OPC Gateway", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _closeConfirmed = true;
        }

        // 設定由 App.OnExit 統一儲存一次
        base.OnClosing(e);
    }
}
