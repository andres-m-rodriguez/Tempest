using Tempest.Pipeline;

namespace Tempest.Parsing;

/// <summary>The contract every parse frontend implements: the full document in one
/// call, plus one explicit method per member kind so a frontend can't silently skip
/// one. TSource is the frontend's own plain-value description of one input (a .razor
/// file's text, a C# type symbol, …), so each frontend stays testable without a
/// compiler harness — and parsing never learns where the source lives; the host that
/// built TSource owns that association. Fallible: every call returns a
/// <see cref="Result{T}"/>, so failures come back as <see cref="IError"/> data, never
/// as exceptions.</summary>
public interface IComponentParser<in TSource>
{
    /// <summary>
    /// The full parsed document — everything the source contributes, in one call. The
    /// per-kind methods below return the same document's slices, for shells that wire
    /// one incremental pipeline per member kind.
    /// </summary>
    Result<TempestDocument> ParseDocument(TSource source);

    /// <summary>
    /// Every [Reactive] field the source declares, shape-valid or not.
    /// </summary>
    Result<EquatableArray<SourceReactiveProperty>> ParseReactiveProperties(TSource source);

    /// <summary>
    /// Every [OnChanged] hook the source declares, resolvable or not — which field a
    /// hook watches is the compiler's decision.
    /// </summary>
    Result<EquatableArray<SourceHook>> ParseHooks(TSource source);

    /// <summary>
    /// Every [CanExecute] member the source declares, resolvable or not — which
    /// command a gate serves is the compiler's decision.
    /// </summary>
    Result<EquatableArray<SourceCanExecute>> ParseCanExecutes(TSource source);

    /// <summary>
    /// Every method bearing [Command]. A combined [Event, Command] method is
    /// returned here and by <see cref="ParseEvents"/> with identical flags; the compiler
    /// groups by identity.
    /// </summary>
    Result<EquatableArray<SourceMethod>> ParseCommands(TSource source);

    /// <summary>
    /// Every method bearing [Event]. A combined [Event, Command] method is
    /// returned here and by <see cref="ParseCommands"/> with identical flags; the
    /// compiler groups by identity.
    /// </summary>
    Result<EquatableArray<SourceMethod>> ParseEvents(TSource source);
}
