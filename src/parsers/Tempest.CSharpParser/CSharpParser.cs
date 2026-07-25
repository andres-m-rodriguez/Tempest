using Microsoft.CodeAnalysis;
using Tempest.Parsing;
using Tempest.Pipeline;

namespace Tempest.CSharpParser;

/// <summary>The C# frontend of the pipeline — the exposed surface of this library.
/// Implements <see cref="IComponentParser{TSource}"/> over the component's type symbol
/// (XAML code-behind, .razor.cs partials, headless stores: anywhere Tempest members
/// live in ordinary C#). No memoization: symbols are live compiler objects, so the
/// shell caches the equatable documents this produces, never the inputs.</summary>
public sealed class CSharpParser : IComponentParser<INamedTypeSymbol>
{
    private readonly SymbolEntryReader _reader = new();

    public Result<TempestDocument> ParseDocument(INamedTypeSymbol source)
        => _reader.Read(source);

    public Result<EquatableArray<SourceReactiveProperty>> ParseReactiveProperties(INamedTypeSymbol source)
        => _reader.Read(source).Map(d => d.Reactives);

    public Result<EquatableArray<SourceHook>> ParseHooks(INamedTypeSymbol source)
        => _reader.Read(source).Map(d => d.Hooks);

    public Result<EquatableArray<SourceCanExecute>> ParseCanExecutes(INamedTypeSymbol source)
        => _reader.Read(source).Map(d => d.CanExecutes);

    public Result<EquatableArray<SourceMethod>> ParseCommands(INamedTypeSymbol source)
        => _reader.Read(source).Map(d => d.Commands);

    public Result<EquatableArray<SourceMethod>> ParseEvents(INamedTypeSymbol source)
        => _reader.Read(source).Map(d => d.Events);

    public Result<EquatableArray<SourceInjection>> ParseInjections(INamedTypeSymbol source)
        => _reader.Read(source).Map(d => d.Injections);
}
