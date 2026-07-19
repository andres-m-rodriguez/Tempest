namespace Tempest.Model;

/// <summary>Everything the DSL knows about one component — the internal representation
/// the tool works with, produced by the assembler from raw parse results and consumed by
/// the emitter. Members whose shape rules failed are excluded here — they exist only as
/// <see cref="DiagnosticModel"/>s.</summary>
public sealed record ComponentModel(
    string Namespace,
    string Name,
    EquatableArray<MethodModel> Methods,
    EquatableArray<ReactiveModel> Reactives,
    /// <summary>Using directives the generated file needs so razor-sourced type names
    /// (emitted as written) resolve: the source file's plus every _Imports.razor's.</summary>
    EquatableArray<string> Usings);
