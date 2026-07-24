using Tempest.Pipeline;

namespace Tempest.Parsing;

/// <summary>A [Command] and/or [Event] method exactly as a frontend read it — entry-point
/// data, before any shape validation. Carries everything the shape rules judge (parameter
/// counts, host-base inheritance, spans for diagnostics); the compiler maps survivors
/// into its internal method model.</summary>
public sealed record SourceMethod(
    string Namespace,
    string ComponentName,
    string MethodName,
    bool IsCommand,
    bool IsEvent,
    /// <summary>True when [RunOnLoad] is stacked on the command — the generated
    /// registration runs it when the host initializes.</summary>
    bool RunOnLoad,
    ReturnKind Kind,
    /// <summary>The T of Task&lt;T&gt;/ValueTask&lt;T&gt;, or the sync return type; null when the
    /// method returns nothing.</summary>
    string? ResultType,
    /// <summary>True when the method declares a trailing CancellationToken.</summary>
    bool HasCancellationToken,
    /// <summary>Parameters excluding a trailing CancellationToken.</summary>
    int ParameterCount,
    /// <summary>The single parameter's type as written (an [Event] handler's record), if any.</summary>
    string? ParamType,
    string? ParamTypeName,
    /// <summary>True when the parameter is a record nested in this component — the [Event]
    /// handler shape rule.</summary>
    bool ParamIsNestedRecordOfComponent,
    /// <summary>The Tempest host base the component inherits; None is the TEM002 case.
    /// Razor parsing can only infer this (implicit Component, or @inherits).</summary>
    HostKind Host,
    /// <summary>Of the method — the generated state property matches it.</summary>
    string Accessibility,
    /// <summary>True when the frontend read type names as written instead of resolving
    /// symbols, so the generated file must import <see cref="FileUsings"/> plus the
    /// project's ambient imports for them to resolve. A capability of the frontend, not
    /// its identity — the compiler never branches on where an entry came from.</summary>
    bool NeedsAmbientUsings,
    /// <summary>'\n'-joined using directives of the source file; empty when the frontend
    /// resolves symbols itself.</summary>
    string FileUsings,
    SourceSpan Span);
