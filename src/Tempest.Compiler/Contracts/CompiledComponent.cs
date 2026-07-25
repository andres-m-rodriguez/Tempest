using Tempest.Pipeline;
using Tempest.Parsing;

namespace Tempest.Compiler;

/// <summary>Everything the pipeline knows about one component after validation — the
/// unit the emitter consumes. Members that failed a shape rule are excluded here; they
/// exist only as <see cref="TempestDiagnostic"/>s.</summary>
public sealed record CompiledComponent(
    string Namespace,
    string Name,
    /// <summary>The Tempest host base the component inherits. Host-specific policy (a
    /// store's state accessibility, for one) resolves from this in the compiler; the
    /// emitter stays single and neutral.</summary>
    HostKind Host,
    EquatableArray<CompiledMethod> Methods,
    EquatableArray<CompiledReactive> Reactives,
    /// <summary>The constructor-injection bridge to emit, when the component's only
    /// explicit constructor takes parameters and the host is a XAML one — null
    /// otherwise (host policy lives here, the emitter stays neutral).</summary>
    CompiledInjection? Injection,
    /// <summary>Using directives the generated file needs so razor-sourced type names
    /// (carried as written) resolve: the source files' own plus every ambient import.
    /// Empty when every member resolved symbols itself.</summary>
    EquatableArray<string> Usings);
