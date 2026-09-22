using System.Windows.Controls;
using OPCGateway.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace OPCGateway.App.Views.Pages;

public partial class DashboardPage : Page, INavigableView<DashboardViewModel>
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public DashboardViewModel ViewModel { get; }
}
