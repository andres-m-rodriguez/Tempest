using System.ComponentModel;
using System.Runtime.ExceptionServices;
using Microsoft.UI.Xaml.Controls;

namespace Tempest;

/// <summary>The page flavor of <see cref="StatefulControl"/>: same [Command],
/// [Reactive], [Event] and [CanExecute] support, inheriting Page so Frame navigation
/// accepts it directly — no view-model detour. A mirror of StatefulControl because
/// C# has no multiple inheritance; change both when changing either. Navigation
/// parameters arrive through the ordinary OnNavigatedTo override, which fires before
/// Loaded — state seeded there is visible to a [Command, RunOnLoad].</summary>
public abstract class StatefulPage : Page, ITempestComponent, INotifyPropertyChanged
{
    private readonly List<IDisposable> _subscriptions = [];
    private readonly List<CommandStateBase> _commands = [];
    private IEventBus? _bus;

    protected StatefulPage()
    {
        DataContext = this;
        Loaded += (_, _) => Register();
        Unloaded += (_, _) => Unregister();
    }

    /// <summary>The bus this page registers against — the app-wide
    /// <see cref="TempestWinUI.Bus"/> unless one is assigned before the page loads
    /// (XAML has no constructor injection).</summary>
    public IEventBus Bus
    {
        get => _bus ??= TempestWinUI.Bus;
        set => _bus = value;
    }

    /// <summary>The app's services, set at startup through
    /// <see cref="TempestWinUI.UseServices"/> — Frame instantiates pages through their
    /// parameterless constructor, so resolution replaces constructor injection.</summary>
    protected IServiceProvider Services
        => TempestWinUI.Services
            ?? throw new InvalidOperationException("No service provider is configured. Call TempestWinUI.UseServices(provider) at startup.");

    public event PropertyChangedEventHandler? PropertyChanged;

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
            NotifyStateChanged();
            return Task.CompletedTask;
        });

    protected Task DispatchEvent(Func<Task> handler)
        => InvokeAsync(async () =>
        {
            await handler();
            NotifyStateChanged();
        });

    /// <summary>The blessed mutate-and-notify primitive: marshals to the dispatcher,
    /// runs a batch of property writes, broadcasts once.</summary>
    protected Task Mutate(Action mutation) => DispatchEvent(mutation);

    /// <summary>Async form of <see cref="Mutate(Action)"/>.</summary>
    protected Task Mutate(Func<Task> mutation) => DispatchEvent(mutation);

    /// <summary>Runs work on the page's dispatcher queue — the WinUI counterpart of
    /// ComponentBase.InvokeAsync, one of the four members the generated twin calls.</summary>
    protected Task InvokeAsync(Func<Task> work)
    {
        var completion = new TaskCompletionSource();
        var queued = DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await work();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        if (!queued)
            completion.SetException(new InvalidOperationException("The dispatcher queue is shut down."));
        return completion.Task;
    }

    void ITempestComponent.RegisterCommand(CommandStateBase command) => _commands.Add(command);

    void ITempestComponent.Rerender()
        => DispatcherQueue.TryEnqueue(NotifyStateChanged);

    void ITempestComponent.DispatchReaction(Func<Task> reaction)
        => DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await reaction();
                NotifyStateChanged();
            }
            catch (Exception ex)
            {
                // Surface through the dispatcher like a throwing event handler,
                // never an unobserved task.
                DispatcherQueue.TryEnqueue(() => ExceptionDispatchInfo.Capture(ex).Throw());
            }
        });

    private void Register()
    {
        Unregister();
        RegisterTempestHandlers(Bus);
    }

    private void Unregister()
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
