using Microsoft.Extensions.Logging;

namespace Tempest;

public sealed class EventBus(ILogger<EventBus>? logger = null) : IEventBus
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Subscription>> _handlers = [];

    public void Publish<TEvent>() where TEvent : new() => Publish(new TEvent());

    public void Publish(object evt) => _ = PublishGuardedAsync(evt);

    public async Task PublishAsync(object evt)
    {
        foreach (var handler in Snapshot(evt))
            await handler(evt);
    }

    public IDisposable Subscribe(Type eventType, Func<object, Task> handler)
    {
        var subscription = new Subscription(this, eventType, handler);
        lock (_gate)
        {
            if (!_handlers.TryGetValue(eventType, out var list))
                _handlers[eventType] = list = [];
            list.Add(subscription);
        }
        return subscription;
    }

    private Func<object, Task>[] Snapshot(object evt)
    {
        Func<object, Task>[] handlers;
        lock (_gate)
        {
            handlers = _handlers.TryGetValue(evt.GetType(), out var list)
                ? [.. list.Select(s => s.Handler)]
                : [];
        }

        if (handlers.Length == 0)
            logger?.LogWarning(
                "Tempest: published {EventType} but no live component handles it. " +
                "Check for a typo or a disposed target.", evt.GetType());

        return handlers;
    }

    private async Task PublishGuardedAsync(object evt)
    {
        try
        {
            await PublishAsync(evt);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Tempest: unhandled exception while handling {EventType}.", evt.GetType());
        }
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_gate)
        {
            if (_handlers.TryGetValue(subscription.EventType, out var list))
            {
                list.Remove(subscription);
                if (list.Count == 0)
                    _handlers.Remove(subscription.EventType);
            }
        }
    }

    private sealed class Subscription(EventBus bus, Type eventType, Func<object, Task> handler) : IDisposable
    {
        public Type EventType { get; } = eventType;
        public Func<object, Task> Handler { get; } = handler;
        public void Dispose() => bus.Unsubscribe(this);
    }
}
