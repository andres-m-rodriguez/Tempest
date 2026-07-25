using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinUiDemo.Pages;

namespace WinUiDemo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PageHost.Navigate(typeof(HomePage));
    }

    private void NavChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var target = (args.SelectedItem as NavigationViewItem)?.Tag switch
        {
            "quotes" => typeof(QuotePage),
            _ => typeof(HomePage),
        };
        if (PageHost.CurrentSourcePageType != target)
            PageHost.Navigate(target);
    }

    private void BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (PageHost.CanGoBack)
            PageHost.GoBack();
    }

    private void PageNavigated(object sender, NavigationEventArgs e)
        => Nav.IsBackEnabled = PageHost.CanGoBack;
}
