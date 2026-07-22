using Tempest.Parsing;
using Xunit;

namespace Tempest.RazorParser.Tests;

public class SourceRegistryTests
{
    private static RazorSource Source(string name) => new(name, "@code { }");

    [Fact]
    public void RegisteredSourceResolves()
    {
        var registry = new SourceRegistry<RazorSource>();
        Assert.True(registry.Add("Cart", Source("Cart")).IsSuccess);

        var result = registry.TryResolve("Cart", out var source);

        Assert.Equal(ResolveResult.Resolved, result);
        Assert.Equal(Source("Cart"), source);
    }

    [Fact]
    public void UnknownNameIsNotFound()
    {
        var registry = new SourceRegistry<RazorSource>();

        Assert.Equal(ResolveResult.NotFound, registry.TryResolve("Missing", out var source));
        Assert.Null(source);
    }

    [Fact]
    public void DuplicateNameFailsWithError()
    {
        var registry = new SourceRegistry<RazorSource>();
        registry.Add("Cart", Source("Cart"));

        var result = registry.Add("Cart", Source("Cart"));

        Assert.False(result.IsSuccess);
        var error = Assert.IsType<DuplicateComponentError>(result.Error);
        Assert.Equal("PRS001", error.Code);
        Assert.Equal("Cart", error.ComponentName);
    }

    [Fact]
    public void ResolutionComposesWithParsing()
    {
        var registry = new SourceRegistry<RazorSource>();
        registry.Add("Cart", new RazorSource("Cart", "@code { [Command] void Go() { } }"));

        Assert.Equal(ResolveResult.Resolved, registry.TryResolve("Cart", out var source));
        var document = new RazorParser().ParseDocument(source!);

        Assert.True(document.IsSuccess);
        Assert.Equal("Go", Assert.Single(document.Value.Commands).MethodName);
    }
}
