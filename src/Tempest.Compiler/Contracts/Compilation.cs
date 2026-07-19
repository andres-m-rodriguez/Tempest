using Tempest.Model;

namespace Tempest.Compiler;

/// <summary>What compiling produced: the components whose members survived the shape
/// rules, and a diagnostic for every member that did not. Diagnostics are data (not an
/// injected sink) because Roslyn incremental caching needs value-equatable outputs.</summary>
public sealed record Compilation(
    EquatableArray<ComponentModel> Components,
    EquatableArray<DiagnosticModel> Diagnostics);
