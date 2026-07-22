using Tempest.Pipeline;

namespace Tempest.Compiler;

/// <summary>What compiling produced: the components whose members survived the shape
/// rules, and a diagnostic for every member that did not. Diagnostics are data in the
/// result (not a side-channel) because Roslyn incremental caching needs value-equatable
/// outputs.</summary>
public sealed record TempestCompilation(
    EquatableArray<CompiledComponent> Components,
    EquatableArray<TempestDiagnostic> Diagnostics)
{
    public static TempestCompilation Empty { get; } = new(
        EquatableArray<CompiledComponent>.Empty,
        EquatableArray<TempestDiagnostic>.Empty);
}
