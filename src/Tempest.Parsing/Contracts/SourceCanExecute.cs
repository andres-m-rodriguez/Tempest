using Tempest.Pipeline;

namespace Tempest.Parsing;

/// <summary>A [CanExecute] member exactly as a frontend read it — kept even when
/// unresolvable or misshapen, so the compiler can diagnose instead of the parser
/// silently dropping it. Which [Command] it gates is the compiler's decision: the
/// explicit attribute target when present, else the On{Command}CanExecute name
/// convention — gate and command may come from different sources of one component.</summary>
public sealed record SourceCanExecute(
    string Namespace,
    string ComponentName,
    string MemberName,
    /// <summary>The attribute's argument when written as [CanExecute("...")] or
    /// [CanExecute(nameof(...))]: the gated command's method name. Null for bare
    /// [CanExecute], which resolves by the On{Command}CanExecute name convention.</summary>
    string? ExplicitTarget,
    /// <summary>True when the member's type is bool — the shape rule's first demand.</summary>
    bool ReturnsBool,
    /// <summary>True for a method, false for a property — the emitter calls one and
    /// reads the other.</summary>
    bool IsMethod,
    /// <summary>A method's parameter count — the shape rule demands zero. Always zero
    /// for properties.</summary>
    int ParameterCount,
    SourceSpan Span);
