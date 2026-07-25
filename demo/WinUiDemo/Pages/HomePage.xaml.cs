using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tempest;
using WinUiDemo.Controls;

namespace WinUiDemo.Pages;

// A plain Page: the stateful piece is the hosted Counter, and this page talks to it
// the only way anyone does — by publishing its event records on the bus.
public sealed partial class HomePage : Page
{
    public HomePage() => InitializeComponent();

    private void BumpOneClicked(object sender, RoutedEventArgs e)
        => TempestWinUI.Bus.Publish(new Counter.Bumped(1));

    private void BumpFiveClicked(object sender, RoutedEventArgs e)
        => TempestWinUI.Bus.Publish(new Counter.Bumped(5));

    private void ZeroClicked(object sender, RoutedEventArgs e)
        => TempestWinUI.Bus.Publish(new Counter.Zeroed());
    private void Substract(object sender, RoutedEventArgs e)
    => TempestWinUI.Bus.Publish(new Counter.Decrement(1));
}
