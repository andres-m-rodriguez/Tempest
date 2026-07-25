using Tempest.Parsing;

namespace Tempest.Compiler;

/// <summary>One component's members gathered across every document that contributed to
/// it (a .razor file and a partial .cs, one day), deduplicated by identity — the unit
/// the shape rules judge.</summary>
internal sealed record ComponentBucket(
    string Namespace,
    string Name,
    IReadOnlyList<SourceMethod> Methods,
    IReadOnlyList<SourceReactiveProperty> Reactives,
    IReadOnlyList<SourceHook> Hooks,
    IReadOnlyList<SourceCanExecute> CanExecutes,
    SourceInjection? Injection);
