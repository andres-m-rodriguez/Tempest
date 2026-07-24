using System.ComponentModel;

namespace Tempest;

/// <summary>The headless host: state, [Command] work and [Event] doorbells with no UI
/// attached — one store class shared by any number of UI layers on any host. Change
/// notification is an INotifyPropertyChanged broadcast (empty property name): XAML
/// binds straight at the store, Blazor subscribes PropertyChanged → StateHasChanged.
/// Construction takes the bus (ordinary constructor DI) and registers immediately —
/// safe because derived field initializers run before the base constructor body, so
/// the generated registration sees initialized fields. There is no dispatcher here:
/// notifications run on the caller's context, and command continuations return to the
/// UI context that started them.</summary>
public abstract class StatefulStore : ITempestComponent, INotifyPropertyChanged, IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];
    private readonly List<CommandStateBase> _commands = [];
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    protected StatefulStore(IEventBus bus)
    {
        Bus = bus;
        RegisterTempestHandlers(bus);
    }

    protected IEventBus Bus { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Overridden by the source generator to wire up handlers and states.</summary>
    protected virtual void RegisterTempestHandlers(IEventBus bus)
    {
    }

    protected void SubscribeEvent<TEvent>(Func<TEvent, Task> handler)
        => _subscriptions.Add(Bus.Subscribe(typeof(TEvent), e => handler((TEvent)e)));

    protected Task DispatchEvent(Action handler)
    {
        handler();
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    protected async Task DispatchEvent(Func<Task> handler)
    {
        await handler();
        NotifyStateChanged();
    }

    /// <summary>Headless: there is no context to marshal to, so work runs inline —
    /// one of the four members the generated twin calls.</summary>
    protected Task InvokeAsync(Func<Task> work) => work();

    /// <summary>The blessed mutate-and-notify primitive: run a batch of property
    /// writes, broadcast once. Off-thread callers are marshalled to the
    /// SynchronizationContext the store was constructed on (the UI thread, in
    /// practice), so INPC never fires from a worker thread.</summary>
    protected Task Mutate(Action mutation)
        => Mutate(() =>
        {
            mutation();
            return Task.CompletedTask;
        });

    /// <summary>Async form of <see cref="Mutate(Action)"/>.</summary>
    protected Task Mutate(Func<Task> mutation)
    {
        if (_context is null || SynchronizationContext.Current == _context)
            return DispatchEvent(mutation);

        var completion = new TaskCompletionSource();
        _context.Post(async _ =>
        {
            try
            {
                await mutation();
                NotifyStateChanged();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }, null);
        return completion.Task;
    }

    void ITempestComponent.RegisterCommand(CommandStateBase command) => _commands.Add(command);

    void ITempestComponent.Rerender() => NotifyStateChanged();

    void ITempestComponent.DispatchReaction(Func<Task> reaction) => RunReaction(reaction);

    // async void on purpose: a throwing hook surfaces on the caller's
    // SynchronizationContext like a throwing event handler, never an unobserved task.
    private async void RunReaction(Func<Task> reaction)
    {
        await reaction();
        NotifyStateChanged();
    }

    public virtual void Dispose()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();
    }

    private void NotifyStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

        // State changed, so [CanExecute] members may have too — bound controls re-gate.
        foreach (var command in _commands)
            command.RaiseCanExecuteChanged();
    }
}
