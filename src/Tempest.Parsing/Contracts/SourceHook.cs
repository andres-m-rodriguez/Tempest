using Tempest.Pipeline;

namespace Tempest.Parsing;

/// <summary>An [OnChanged] hook method exactly as a frontend read it — kept even when
/// unresolvable, so the compiler can diagnose (TEM008) instead of the parser silently
/// dropping it. Which [Reactive] field it watches is the compiler's decision: the
/// explicit attribute target when present, else the On{Field}Changed name convention —
/// hook and field may come from different sources of one component.</summary>
public sealed record SourceHook(
    string Namespace,
    string ComponentName,
    string MethodName,
    /// <summary>The attribute's argument when written as [OnChanged("...")] or
    /// [OnChanged(nameof(...))]: a field name or its PascalCase twin. Null for bare
    /// [OnChanged], which resolves by the On{Field}Changed name convention.</summary>
    string? ExplicitTarget,
    bool ReturnsTask,
    /// <summary>The first parameter's name as written; empty when there is none.</summary>
    string ParamName,
    /// <summary>Total parameter count — the shape rule demands exactly one (the new value).</summary>
    int ParameterCount,
    /// <summary>As written, e.g. "private" — may be empty.</summary>
    string Accessibility,
    SourceSpan Span);
