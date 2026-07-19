namespace Tempest.Model.Entry;

/// <summary>The pipeline's entry point: the members one source contributes, exactly as
/// written and before any shape validation or grouping. The assembler folds many of
/// these into internal <see cref="ComponentModel"/>s. Value equality throughout, so
/// incremental caching and test assertions compare whole results directly.</summary>
public sealed record SourceEntries(
    EquatableArray<EntryMethod> Methods,
    EquatableArray<EntryReactive> Reactives)
{
    public static SourceEntries Empty { get; } =
        new(EquatableArray<EntryMethod>.Empty, EquatableArray<EntryReactive>.Empty);
}
