namespace Tempest;

/// <summary>Marks async work this component does. The generator emits a
/// {Name}State property (CommandState) with IsLoading/IsError/Error and
/// Execute()/TryExecute(). The method may declare an optional trailing
/// CancellationToken for latest-wins cancellation.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandAttribute : Attribute;

/// <summary>Marks a handler for a nested record of this component,
/// invoked when that record is published on the bus.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EventAttribute : Attribute;

/// <summary>Marks a private field as reactive. The generator emits a
/// {PascalCase}State property (ReactiveState&lt;T&gt;) wrapping the field;
/// writes through its Value re-render and invoke the field's [OnChanged]
/// hook when one is declared.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class ReactiveAttribute : Attribute;

/// <summary>Marks a change hook for a [Reactive] field: an ordinary method taking the
/// new value, invoked after each write through the field's state twin. Bare, the
/// method's On{Field}Changed name picks the field; the constructor argument targets one
/// explicitly (the field's name or its PascalCase twin), freeing the method name.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OnChangedAttribute : Attribute
{
    public OnChangedAttribute() { }

    public OnChangedAttribute(string target) => Target = target;

    /// <summary>The watched field (name or PascalCase twin); null resolves by the
    /// On{Field}Changed name convention.</summary>
    public string? Target { get; }
}
