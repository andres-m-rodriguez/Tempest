using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Tempest;
using WinUiDemo.Services;

namespace WinUiDemo;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // One call wires DI: pages and controls resolve their constructor parameters
        // from this provider, and a registered IEventBus would replace the ambient bus.
        TempestWinUI.UseServices(new ServiceCollection()
            .AddSingleton<QuoteService>()
            .BuildServiceProvider());

        _window = new MainWindow();
        _window.Activate();
    }
}
