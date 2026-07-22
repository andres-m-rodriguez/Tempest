using System.Diagnostics.CodeAnalysis;
using Tempest.Pipeline;

namespace Tempest.Parsing;

/// <summary>The in-memory resolver: component name → source, ordinal like C#
/// identifiers. Tests and virtual components register directly; anything that reads
/// files implements <see cref="ISourceResolver{TSource}"/> in the shell instead.</summary>
public sealed class SourceRegistry<TSource> : ISourceResolver<TSource>
{
    private readonly Dictionary<string, TSource> _sources = new(StringComparer.Ordinal);

    public Result Add(string componentName, TSource source)
    {
        if (_sources.ContainsKey(componentName))
            return Result.Fail(new DuplicateComponentError(componentName));

        _sources[componentName] = source;
        return Result.Ok();
    }

    public bool Contains(string componentName) => _sources.ContainsKey(componentName);

    public IReadOnlyCollection<string> Names => _sources.Keys;

    public ResolveResult TryResolve(string componentName, [NotNullWhen(true)] out TSource? source)
        => _sources.TryGetValue(componentName, out source!)
            ? ResolveResult.Resolved
            : ResolveResult.NotFound;
}
