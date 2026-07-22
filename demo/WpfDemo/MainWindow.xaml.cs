using System.Windows;
using Tempest;
using WpfDemo.Controls;

namespace WpfDemo;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void BumpClicked(object sender, RoutedEventArgs e)
        => TempestWpf.Bus.Publish(new TempestDemoControl.CounterBumped(5));
}
