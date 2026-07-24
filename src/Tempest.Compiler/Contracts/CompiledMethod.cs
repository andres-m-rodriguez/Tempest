using Tempest.Parsing;

namespace Tempest.Compiler;

/// <summary>A validated [Command] and/or [Event] method, holding only what emission
/// needs. Its source counterpart (with shape flags and spans) is
/// <see cref="SourceMethod"/>; the compiler maps one into the other once the shape
/// rules pass. Type names are carried as source text, emitted back verbatim.</summary>
public sealed record CompiledMethod(
    string MethodName,
    bool IsCommand,
    bool IsEvent,
    /// <summary>True when the generated registration runs this command on host load.</summary>
    bool RunOnLoad,
    ReturnKind Kind,
    /// <summary>The T of Task&lt;T&gt;/ValueTask&lt;T&gt;, or the sync return type; null when the
    /// method returns nothing.</summary>
    string? ResultType,
    /// <summary>True when the method declares a trailing CancellationToken — opting into
    /// latest-wins cancellation.</summary>
    bool HasCancellationToken,
    /// <summary>The single parameter's type as written (an [Event] handler's record), if any.</summary>
    string? ParamType,
    string? ParamTypeName,
    /// <summary>The [CanExecute] member gating this command, when one resolved.</summary>
    CompiledCanExecute? CanExecute,
    /// <summary>Of the method — the generated state property matches it.</summary>
    string Accessibility);
