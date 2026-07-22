using Tempest;

namespace SharedStoreDemo.Shared;

/// <summary>The one class both apps share: every [Reactive], [Command], [OnChanged]
/// and [Event] lives here, once — the Blazor page and the WinUI window are pure UI
/// layers over it. The generated twin (public states, per the Store host policy) is
/// the entire surface they bind to.</summary>
public sealed partial class DemoStore(IEventBus bus) : StatefulStore(bus)
{
    [Reactive] private int _count;
    [Reactive] private string _name = "";

    public string Greeting { get; private set; } = "";
    public string SavedAtText { get; private set; } = "Not saved yet";
    public string DirtyText => NameState.IsDirty ? "Dirty" : "Pristine";
    public string SaveLabel => SaveState.IsLoading ? "Saving…" : "Save";
    public bool SaveEnabled => !SaveState.IsLoading;

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
}
