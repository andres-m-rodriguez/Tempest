using System.Diagnostics.CodeAnalysis;

namespace Tempest.Parsing;

/// <summary>The seam a shell implements so pipeline stages can pull a source by
/// component name — the only identity the pipeline knows. TSource is the frontend's
/// plain-value source (the same type its <see cref="IComponentParser{TSource}"/>
/// takes), so resolution and parsing compose without either learning where sources
/// live: implementations that touch the file system belong to the shell.</summary>
public interface ISourceResolver<TSource>
{
    ResolveResult TryResolve(string componentName, [NotNullWhen(true)] out TSource? source);
}
