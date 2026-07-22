using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SharedStoreDemo.Shared;

namespace SharedStoreDemo.WinUi.Controls;

// Pure UI layer, mirroring the other WinUI demo's control — except this one owns no
// state at all: it's a plain UserControl whose every binding reads the shared
// DemoStore and whose every handler forwards to it.
public sealed partial class StoreControl : UserControl
{
    private static DemoStore Store => App.Store;

    public StoreControl()
    {
        InitializeComponent();
        DataContext = Store;
    }

    private void NameChanged(object sender, TextChangedEventArgs e)
        => Store.NameState.Value = ((TextBox)sender).Text;

    private void IncrementClicked(object sender, RoutedEventArgs e) => Store.CountState.Value++;

    private void ResetClicked(object sender, RoutedEventArgs e) => Store.CountState.Reset();

    private async void SaveClicked(object sender, RoutedEventArgs e) => await Store.SaveState.TryExecute();
}
