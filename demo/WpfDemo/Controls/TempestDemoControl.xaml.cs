using System.Windows;
using Tempest;

namespace WpfDemo.Controls;

// The markup lives in TempestDemoControl.xaml, the attributed members here in the
// code-behind, and the generated twin lands in TempestDemoControl.Tempest.g.cs —
// XAML's native shape of the same component the Blazor demo declares in a @code block.
public partial class TempestDemoControl : StatefulControl
{
    public TempestDemoControl() => InitializeComponent();

    [Reactive] private int _count;
    [Reactive] private string _name = "";

    public string Greeting { get; private set; } = "";
    public string SavedAtText { get; private set; } = "Not saved yet";
    public string DirtyText => NameState.IsDirty ? "Dirty" : "Pristine";

    [OnChanged]
    private void OnNameChanged(string value)
        => Greeting = value.Length == 0 ? "" : $"Hello, {value}!";

    [Command]
    private async Task Save(CancellationToken ct)
    {
        await Task.Delay(800, ct);
        SavedAtText = $"Saved at {DateTime.Now:T}";
    }

    public sealed record CounterBumped(int By);

    [Event]
    private void OnCounterBumped(CounterBumped e)
        => CountState.Value += e.By;

    private void IncrementClicked(object sender, RoutedEventArgs e) => CountState.Value++;

    private void ResetClicked(object sender, RoutedEventArgs e) => CountState.Reset();

    private async void SaveClicked(object sender, RoutedEventArgs e) => await SaveState.TryExecute();
}
