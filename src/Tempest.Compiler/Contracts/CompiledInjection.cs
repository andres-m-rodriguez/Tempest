using Tempest.Pipeline;

namespace Tempest.Compiler;

/// <summary>A validated constructor-injection request: the emitter bridges XAML's
/// parameterless activation with a generated constructor that resolves each parameter
/// from the ambient provider and chains to the user's. Present only for XAML hosts —
/// that's compiler policy: razor injects through @inject and a store is constructed
/// by the container itself.</summary>
public sealed record CompiledInjection(
    /// <summary>Fully-qualified parameter types, in declaration order.</summary>
    EquatableArray<string> ParameterTypes);
