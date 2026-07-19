namespace Tempest.Model.Entry;

/// <summary>The pipeline's entry point: the members one source contributes, exactly as
/// written and before any shape validation or grouping. Hooks travel separately from
/// reactives because hook-to-field resolution is the compiler stage's job (the two can
/// live in different sources of the same component). Value equality throughout, so
/// incremental caching and test assertions compare whole results directly.</summary>
public sealed record SourceEntries(
    EquatableArray<EntryMethod> Methods,
    EquatableArray<EntryReactive> Reactives,
    EquatableArray<EntryHook> Hooks)
{
    public static SourceEntries Empty { get; } = new(
        EquatableArray<EntryMethod>.Empty,
        EquatableArray<EntryReactive>.Empty,
        EquatableArray<EntryHook>.Empty);
}
