using System.Windows.Controls;
using OPCGateway.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace OPCGateway.App.Views.Pages;

public partial class MappingPage : Page, INavigableView<MappingViewModel>
{
    public MappingPage(MappingViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public MappingViewModel ViewModel { get; }
}
