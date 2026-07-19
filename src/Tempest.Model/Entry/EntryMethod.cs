namespace Tempest.Model.Entry;

/// <summary>A [Command] and/or [Event] method exactly as a frontend read it — entry-point
/// data, before any shape validation. Carries everything the shape rules judge (parameter
/// counts, host-base inheritance, spans for diagnostics); the assembler maps survivors
/// into the internal <see cref="MethodModel"/>.</summary>
public sealed record EntryMethod(
    string Namespace,
    string ComponentName,
    string MethodName,
    bool IsCommand,
    bool IsEvent,
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
    /// <summary>True when the component inherits a Tempest host base (Blazor's
    /// StatefulComponent/StatefulLayoutComponent). Razor parsing can only infer this.</summary>
    bool InheritsHostBase,
    /// <summary>Of the method — the generated state property matches it.</summary>
    string Accessibility,
    bool FromRazor,
    /// <summary>'\n'-joined @using directives of the source .razor file; empty for C# sources.</summary>
    string FileUsings,
    SourceSpan Span);
