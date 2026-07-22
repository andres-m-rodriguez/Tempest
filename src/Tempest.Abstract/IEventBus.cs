namespace Tempest;

public interface IEventBus
{
    /// <summary>Fire-and-forget publish of a parameterless event record.</summary>
    void Publish<TEvent>() where TEvent : new();

    /// <summary>Fire-and-forget publish.</summary>
    void Publish(object evt);

    /// <summary>Publish and await all handlers.</summary>
    Task PublishAsync(object evt);

    /// <summary>Register a handler for an event type. Dispose the result to unregister.</summary>
    IDisposable Subscribe(Type eventType, Func<object, Task> handler);
}
