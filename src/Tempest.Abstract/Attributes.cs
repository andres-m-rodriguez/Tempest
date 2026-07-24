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

/// <summary>Stacked on a [Command] — like [Event, Command] stacks roles — to run it
/// when the host initializes: Blazor's OnInitialized, XAML's Loaded, a store's
/// construction. Replaces the hand-written "execute my load command on startup"
/// lifecycle ritual; the run goes through TryExecute, so a failing load lands in
/// {Name}State.Error.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RunOnLoadAttribute : Attribute;

/// <summary>Marks the enablement gate of a [Command]: a bool property (or parameterless
/// bool method) the generated state uses as its ICommand.CanExecute predicate — the
/// [OnChanged] pattern applied to commands. Bare, the member's On{Command}CanExecute
/// name picks the command; the constructor argument targets one explicitly (the
/// command method's name), freeing the member name.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
public sealed class CanExecuteAttribute : Attribute
{
    public CanExecuteAttribute() { }

    public CanExecuteAttribute(string target) => Target = target;

    /// <summary>The gated command's method name; null resolves by the
    /// On{Command}CanExecute name convention.</summary>
    public string? Target { get; }
}
