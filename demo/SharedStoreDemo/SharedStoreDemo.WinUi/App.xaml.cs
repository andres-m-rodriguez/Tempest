using Microsoft.UI.Xaml;
using SharedStoreDemo.Shared;
using Tempest;

namespace SharedStoreDemo.WinUi;

public partial class App : Application
{
    /// <summary>The same store class the WASM app injects — here as one app-wide
    /// instance on the ambient bus.</summary>
    public static DemoStore Store { get; } = new(TempestWinUI.Bus);

    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
