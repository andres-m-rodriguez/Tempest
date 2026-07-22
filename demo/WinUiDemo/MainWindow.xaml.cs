using Microsoft.UI.Xaml;
using Tempest;
using WinUiDemo.Controls;

namespace WinUiDemo;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void BumpClicked(object sender, RoutedEventArgs e)
        => TempestWinUI.Bus.Publish(new TempestDemoControl.CounterBumped(5));
}
