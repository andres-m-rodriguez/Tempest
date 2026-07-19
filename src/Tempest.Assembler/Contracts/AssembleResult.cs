using Tempest.Model;

namespace Tempest.Assembler;

/// <summary>What assembling produced: the components whose members survived the shape
/// rules, and a diagnostic for every member that didn't. Diagnostics are data (not an
/// injected sink) because Roslyn's incremental caching needs value-equatable outputs.</summary>
public sealed record AssembleResult(
    EquatableArray<ComponentModel> Components,
    EquatableArray<DiagnosticModel> Diagnostics);
