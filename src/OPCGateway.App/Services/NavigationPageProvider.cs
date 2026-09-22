using Wpf.Ui.Abstractions;

namespace OPCGateway.App.Services;

/// <summary>讓 WPF-UI 的 NavigationView 透過 DI 容器建立頁面。</summary>
public sealed class NavigationPageProvider : INavigationViewPageProvider
{
    private readonly IServiceProvider _services;

    public NavigationPageProvider(IServiceProvider services)
    {
        _services = services;
    }

    public object? GetPage(Type pageType) => _services.GetService(pageType);
}
