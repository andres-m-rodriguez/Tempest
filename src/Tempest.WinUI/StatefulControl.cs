using System.ComponentModel;
using System.Runtime.ExceptionServices;
using Microsoft.UI.Xaml.Controls;

namespace Tempest;

/// <summary>Base class for WinUI controls that own their state and expose [Command]
/// work, [Reactive] values and [Event] doorbells — the same four-member contract
/// <c>StatefulComponent</c> implements for Blazor, backed by the DispatcherQueue and
/// INotifyPropertyChanged: a re-render is a PropertyChanged broadcast (empty property
/// name), which re-evaluates every binding rooted at this control. DataContext
/// defaults to the control itself, so bindings like {Binding CountState.Value} just
/// work. Handlers register on Loaded and unregister on Unloaded.</summary>
public abstract class StatefulControl : UserControl, ITempestComponent, INotifyPropertyChanged
{
    private readonly List<IDisposable> _subscriptions = [];
    private IEventBus? _bus;

    protected StatefulControl()
    {
        DataContext = this;
        Loaded += (_, _) => Register();
        Unloaded += (_, _) => Unregister();
    }

    /// <summary>The bus this control registers against — the app-wide
    /// <see cref="TempestWinUI.Bus"/> unless one is assigned before the control loads
    /// (XAML has no constructor injection).</summary>
    public IEventBus Bus
    {
        get => _bus ??= TempestWinUI.Bus;
        set => _bus = value;
    }

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

    /// <summary>Runs work on the control's dispatcher queue — the WinUI counterpart of
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
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
