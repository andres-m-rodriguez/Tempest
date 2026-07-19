using Microsoft.AspNetCore.Components;

namespace Tempest;

/// <summary>The layout flavor of <see cref="StatefulComponent"/>: same [Command],
/// [Reactive] and [Event] support, but inheriting LayoutComponentBase so Blazor
/// accepts it as a layout. Kept as a mirror of StatefulComponent because C# has
/// no multiple inheritance — change both when changing either.</summary>
public abstract class StatefulLayoutComponent : LayoutComponentBase, ITempestComponent, IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];

    [Inject] protected IEventBus Bus { get; set; } = default!;

    protected override void OnInitialized() => RegisterTempestHandlers(Bus);

    /// <summary>Overridden by the source generator to wire up handlers and states.</summary>
    protected virtual void RegisterTempestHandlers(IEventBus bus)
    {
    }

    protected void SubscribeEvent<TEvent>(Func<TEvent, Task> handler)
        => _subscriptions.Add(Bus.Subscribe(typeof(TEvent), e => handler((TEvent)e)));

    protected Task DispatchEvent(Action handler)
        => InvokeAsync(() =>
        {
            handler();
            StateHasChanged();
        });

    protected Task DispatchEvent(Func<Task> handler)
        => InvokeAsync(async () =>
        {
            await handler();
            StateHasChanged();
        });

    void ITempestComponent.Rerender() => _ = InvokeAsync(StateHasChanged);

    void ITempestComponent.DispatchReaction(Func<Task> reaction)
        => _ = InvokeAsync(async () =>
        {
            try
            {
                await reaction();
                StateHasChanged();
            }
            catch (Exception ex)
            {
                await DispatchExceptionAsync(ex);
            }
        });

    public virtual void Dispose()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();
    }
}
