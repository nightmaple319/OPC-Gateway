using System.Windows.Controls;
using OPCGateway.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace OPCGateway.App.Views.Pages;

public partial class LogPage : Page, INavigableView<LogViewModel>
{
    public LogPage(LogViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        viewModel.ScrollToEndRequested += ScrollToEnd;
    }

    public LogViewModel ViewModel { get; }

    private void ScrollToEnd()
    {
        var count = LogList.Items.Count;
        if (count == 0)
            return;
        var last = LogList.Items[count - 1];
        LogList.ScrollIntoView(last);
    }
}
