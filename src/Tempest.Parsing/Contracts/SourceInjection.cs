using Tempest.Pipeline;

namespace Tempest.Parsing;

/// <summary>A component's constructor-injection request: the single explicit
/// parameterized constructor (primary or classic) of a component that declares no
/// parameterless one. XAML activation needs a parameterless constructor, so the
/// emitter bridges with one that resolves each parameter from the ambient provider
/// and chains. Only the symbol frontend produces these — razor components inject
/// through @inject, and a store is constructed by the container itself; whether an
/// injection is emitted is the compiler's host policy.</summary>
public sealed record SourceInjection(
    string Namespace,
    string ComponentName,
    /// <summary>Fully-qualified parameter types, in declaration order.</summary>
    EquatableArray<string> ParameterTypes,
    HostKind Host,
    SourceSpan Span);
