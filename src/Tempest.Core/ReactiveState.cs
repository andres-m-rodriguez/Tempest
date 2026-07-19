namespace Tempest;

/// <summary>
/// Generated per [Reactive] field: the field's public twin. Writes through
/// <see cref="Value"/> re-render and ring the component's optional
/// <c>On{Name}Changed(T value)</c> partial hook; <see cref="SetSilently"/> re-renders
/// without ringing; writing the raw field does neither.
/// </summary>
public sealed class ReactiveState<T>(
    ITempestComponent component,
    Func<T> getter,
    Action<T> setter,
    T initial,
    Func<T, Task>? hook = null)
{
    private readonly ITempestComponent _component = component;
    private readonly Func<T> _getter = getter;
    private readonly Action<T> _setter = setter;
    private readonly Func<T, Task>? _hook = hook;

    /// <summary>The field's value when this state was created (its initializer, in practice).</summary>
    public T Initial { get; } = initial;

    /// <summary>True when <see cref="Value"/> differs from <see cref="Initial"/>.</summary>
    public bool IsDirty => !EqualityComparer<T>.Default.Equals(_getter(), Initial);

    /// <summary>Change-check → assign → re-render → ring the hook.</summary>
    public T Value
    {
        get => _getter();
        set
        {
            if (EqualityComparer<T>.Default.Equals(_getter(), value))
                return;
            _setter(value);
            _component.Rerender();
            if (_hook is { } hook)
                _component.DispatchReaction(() => hook(value));
        }
    }

    /// <summary>Change-check → assign → re-render — the hook is not called.</summary>
    public void SetSilently(T value)
    {
        if (EqualityComparer<T>.Default.Equals(_getter(), value))
            return;
        _setter(value);
        _component.Rerender();
    }

    /// <summary>Back to <see cref="Initial"/>, silently.</summary>
    public void Reset() => SetSilently(Initial);
}
