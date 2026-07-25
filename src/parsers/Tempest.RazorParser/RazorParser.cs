using Tempest.Parsing;
using Tempest.Pipeline;

namespace Tempest.RazorParser;

/// <summary>The .razor frontend of the pipeline — the exposed surface of this library.
/// Implements <see cref="IComponentParser{TSource}"/> over <see cref="RazorSource"/>;
/// all machinery lives in the internal <see cref="RazorEntryReader"/>. Failures come
/// back as <see cref="IError"/> data in the result — <see cref="InvalidRazorSourceError"/>
/// for a source breaking its contract, <see cref="RazorEngineError"/> for an unexpected
/// engine failure — never as exceptions. Every call comes from one underlying parse:
/// <see cref="ParseCacheService"/> memoizes the last result by the source's value
/// equality, so the shell's calls over one file parse it once.</summary>
public sealed class RazorParser : IComponentParser<RazorSource>
{
    private readonly RazorEntryReader _reader = new();
    private readonly ParseCacheService _cache = new();

    public Result<TempestDocument> ParseDocument(RazorSource source)
        => Entries(source);

    public Result<EquatableArray<SourceReactiveProperty>> ParseReactiveProperties(RazorSource source)
        => Entries(source).Map(e => e.Reactives);

    public Result<EquatableArray<SourceHook>> ParseHooks(RazorSource source)
        => Entries(source).Map(e => e.Hooks);

    public Result<EquatableArray<SourceCanExecute>> ParseCanExecutes(RazorSource source)
        => Entries(source).Map(e => e.CanExecutes);

    public Result<EquatableArray<SourceMethod>> ParseCommands(RazorSource source)
        => Entries(source).Map(e => e.Commands);

    public Result<EquatableArray<SourceMethod>> ParseEvents(RazorSource source)
        => Entries(source).Map(e => e.Events);

    public Result<EquatableArray<SourceInjection>> ParseInjections(RazorSource source)
        => Entries(source).Map(e => e.Injections);

    private Result<TempestDocument> Entries(RazorSource? source)
    {
        if (_cache.TryGet(source, out var cached))
            return cached;

        var entries = _reader.Read(source);
        _cache.Store(source, entries);
        return entries;
    }
}
