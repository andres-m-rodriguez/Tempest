using Microsoft.UI.Xaml;
using SharedStoreDemo.Shared;
using Tempest;

namespace SharedStoreDemo.WinUi;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void BumpClicked(object sender, RoutedEventArgs e)
        => TempestWinUI.Bus.Publish(new DemoStore.CounterBumped(5));
}
