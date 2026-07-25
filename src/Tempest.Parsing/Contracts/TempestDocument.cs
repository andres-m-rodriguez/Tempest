using Tempest.Pipeline;

namespace Tempest.Parsing;

/// <summary>The full parsed document: every member one source contributes, exactly as
/// written and before any shape validation or grouping — returned whole by
/// <see cref="IComponentParser{TSource}"/>.ParseDocument, or one array per per-kind
/// call for shells that pipeline member kinds separately. A combined
/// [Event, Command] method appears in both method arrays; the compiler groups by
/// identity. Value equality throughout, so incremental caching and test assertions
/// compare whole results directly.</summary>
public sealed record TempestDocument(
    EquatableArray<SourceMethod> Commands,
    EquatableArray<SourceMethod> Events,
    EquatableArray<SourceReactiveProperty> Reactives,
    EquatableArray<SourceHook> Hooks,
    EquatableArray<SourceCanExecute> CanExecutes,
    EquatableArray<SourceInjection> Injections)
{
    public static TempestDocument Empty { get; } = new(
        EquatableArray<SourceMethod>.Empty,
        EquatableArray<SourceMethod>.Empty,
        EquatableArray<SourceReactiveProperty>.Empty,
        EquatableArray<SourceHook>.Empty,
        EquatableArray<SourceCanExecute>.Empty,
        EquatableArray<SourceInjection>.Empty);
}
