namespace Tempest.Model;

/// <summary>How a [Command] method returns its work — decides which state class the
/// emitter news up and how the call is adapted into the async lifecycle.</summary>
public enum ReturnKind
{
    Void,
    Task,
    TaskOfT,
    ValueTask,
    ValueTaskOfT,
    Sync,
}

/// <summary>A validated [Command] and/or [Event] method, holding only what emission
/// needs. Its entry-point counterpart (with shape flags and spans) is
/// <see cref="Entry.EntryMethod"/>; the assembler maps one into the other once the
/// shape rules pass. Type names are carried as source text, emitted back verbatim.</summary>
public sealed record MethodModel(
    string MethodName,
    bool IsCommand,
    bool IsEvent,
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
    /// <summary>Of the method — the generated state property matches it.</summary>
    string Accessibility);
