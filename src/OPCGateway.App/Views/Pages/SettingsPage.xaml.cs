using System.Windows.Controls;
using OPCGateway.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace OPCGateway.App.Views.Pages;

public partial class SettingsPage : Page, INavigableView<SettingsViewModel>
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }
}
