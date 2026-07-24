using Microsoft.AspNetCore.Components;

namespace Tempest;

/// <summary>Base class for components that own their state and expose
/// [Command] work, [Reactive] values and [Event] doorbells.
/// Layouts use <see cref="StatefulLayoutComponent"/> instead.</summary>
public abstract class StatefulComponent : ComponentBase, ITempestComponent, IDisposable
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


    /// <summary>The blessed mutate-and-notify primitive: marshals through InvokeAsync,
    /// runs a batch of property writes, re-renders once.</summary>
    protected Task Mutate(Action mutation) => DispatchEvent(mutation);

    /// <summary>Async form of <see cref="Mutate(Action)"/>.</summary>
    protected Task Mutate(Func<Task> mutation) => DispatchEvent(mutation);
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
