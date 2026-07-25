using Tempest;

namespace WinUiDemo.Controls;

// A component with no click handlers and no public methods: its whole surface is the
// two event records. Anyone with the bus can drive it — the hosting page, another
// component, a background service — without holding a reference to it.
public sealed partial class Counter : StatefulControl
{
    public Counter() => InitializeComponent();

    [Reactive] private int _count;

    public string LastChangeText { get; private set; } = "Waiting for events";

    public sealed record Bumped(int By);
    [Event]
    private void OnBumped(Bumped e)
    {
        CountState.Value += e.By;
        LastChangeText = $"Bumped by {e.By}";
    }

    public sealed record Zeroed;
    [Event]
    private void OnZeroed(Zeroed e)
    {
        CountState.Reset();
        LastChangeText = "Reset to zero";
    }
    public sealed record Decrement(int By);
    [Event]
    private void OnDecrement(Decrement e)
    {
        CountState.Value -= e.By;
        LastChangeText = $"Decremented by {e.By}";
    }
}
