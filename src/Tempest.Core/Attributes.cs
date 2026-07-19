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
/// writes through its Value re-render and ring the optional partial
/// On{PascalCase}Changed(T value) hook implemented on the component.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class ReactiveAttribute : Attribute;
