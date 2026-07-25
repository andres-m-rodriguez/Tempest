using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Tempest.Generators;
using Xunit;

namespace Tempest.Generators.Tests;

public class TempestGeneratorTests
{
    private sealed class TestAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(text);
    }

    private static GeneratorDriverRunResult Run(params (string Path, string Text)[] files)
        => Run(files, sources: []);

    private static GeneratorDriverRunResult Run(
        (string Path, string Text)[] files, (string Path, string Text)[] sources)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            sources.Select(s => CSharpSyntaxTree.ParseText(s.Text, path: s.Path)),
            sources.Length == 0
                ? [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]
                : RuntimeReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            generators: [new TempestGenerator().AsSourceGenerator()],
            additionalTexts: files.Select(f => (AdditionalText)new TestAdditionalText(f.Path, f.Text)));

        return driver.RunGenerators(compilation).GetRunResult();
    }

    private const string Cart = """
        @namespace Demo.Pages
        @inherits Tempest.StatefulComponent

        <h1>Cart</h1>

        @code {
            [Reactive] private string _query = "";

            [Command]
            private async Task Save(CancellationToken ct) => await Task.Delay(1, ct);
        }
        """;

    [Fact]
    public void GeneratesStateTwinForRazorComponent()
    {
        var result = Run(("Pages/Cart.razor", Cart));

        Assert.Empty(result.Diagnostics);
        var generated = Assert.Single(Assert.Single(result.Results).GeneratedSources);
        Assert.Equal("Demo.Pages.Cart.Tempest.g.cs", generated.HintName);

        var source = generated.SourceText.ToString();
        Assert.Contains("namespace Demo.Pages", source);
        Assert.Contains("partial class Cart", source);
        Assert.Contains("SaveState", source);
        Assert.Contains("QueryState", source);
    }

    [Fact]
    public void BadCommandShapeReportsTem003AtTheRealFile()
    {
        var result = Run(("Pages/Cart.razor", """
            @inherits Tempest.StatefulComponent
            @code {
                [Command] private void Go(int amount) { }
            }
            """));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TEM003", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.EndsWith("Cart.razor", diagnostic.Location.GetLineSpan().Path);
    }

    [Fact]
    public void ImportsUsingsFlowIntoGeneratedFiles()
    {
        var result = Run(
            ("_Imports.razor", "@using My.Models\n@using System.Text"),
            ("Cart.razor", """
                @inherits Tempest.StatefulComponent
                @code {
                    [Reactive] private string _q = "";
                }
                """));

        var source = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();
        Assert.Contains("using My.Models;", source);
        Assert.Contains("using System.Text;", source);
    }

    [Fact]
    public void PlainRazorFileProducesNothing()
    {
        var result = Run(("Index.razor", "<h1>Hello</h1>\n@code { private int _x; }"));

        Assert.Empty(result.Diagnostics);
        Assert.Empty(Assert.Single(result.Results).GeneratedSources);
    }

    [Fact]
    public void MissingHostBaseSuppressesGenerationWithTem002()
    {
        var result = Run(("Cart.razor", """
            @inherits My.Custom.PageBase
            @code {
                [Command] private void Go() { }
            }
            """));

        Assert.Equal("TEM002", Assert.Single(result.Diagnostics).Id);
        Assert.Empty(Assert.Single(result.Results).GeneratedSources);
    }

    [Fact]
    public void GeneratedSourceIsSyntacticallyValid()
    {
        var result = Run(("Pages/Cart.razor", Cart));

        var source = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();
        var errors = CSharpSyntaxTree.ParseText(source).GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(errors);
    }

    [Fact]
    public void CodeBehindClassGeneratesThroughTheSymbolFrontend()
    {
        var result = Run(files: [], sources:
        [
            ("Store/CartStore.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using Tempest;

                namespace Demo.Store;

                public sealed partial class CartStore : StatefulComponent
                {
                    [Reactive] private int _count;

                    [OnChanged]
                    private void OnCountChanged(int value) { }

                    [Command]
                    private Task Refresh(CancellationToken ct) => Task.Delay(1, ct);
                }
                """),
        ]);

        Assert.Empty(result.Diagnostics);
        var generated = Assert.Single(Assert.Single(result.Results).GeneratedSources);
        Assert.Equal("Demo.Store.CartStore.Tempest.g.cs", generated.HintName);

        var source = generated.SourceText.ToString();
        Assert.Contains("RefreshState", source);
        Assert.Contains("CountState", source);
        Assert.Contains("OnCountChanged(__v)", source);
        Assert.DoesNotContain("using Demo.Store;", source);   // symbol path needs no ambient usings
    }

    [Fact]
    public void PrimaryConstructorEmitsInjectionBridgeForXamlPage()
    {
        var result = Run(files: [], sources:
        [
            // Tempest.WinUI targets Windows, so the host base is stubbed by name —
            // host resolution matches simple names.
            ("Stubs.cs", "namespace Tempest { public abstract class StatefulPage { } }"),
            ("Pages/GuildPage.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using Tempest;

                namespace Demo.Pages;

                public interface IGuildsClient { }

                public sealed partial class GuildPage(IGuildsClient client) : StatefulPage
                {
                    [Command]
                    private Task Load(CancellationToken ct) => Task.CompletedTask;
                }
                """),
        ]);

        Assert.Empty(result.Diagnostics);
        var source = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();
        Assert.Contains("public GuildPage()", source);
        Assert.Contains("global::Tempest.TempestServices.Resolve<global::Demo.Pages.IGuildsClient>()", source);
        Assert.Contains("InitializeComponent();", source);
    }

    [Fact]
    public void HookInCodeBehindWiresToRazorFieldAcrossFrontends()
    {
        var result = Run(
            files:
            [
                ("Pages/Cart.razor", """
                    @namespace Demo.Pages
                    @inherits Tempest.StatefulComponent
                    @code {
                        [Reactive] private string _query = "";
                    }
                    """),
            ],
            sources:
            [
                ("Pages/Cart.razor.cs", """
                    using Tempest;

                    namespace Demo.Pages;

                    public sealed partial class Cart : StatefulComponent
                    {
                        [OnChanged]
                        private void OnQueryChanged(string value) { }
                    }
                    """),
            ]);

        Assert.Empty(result.Diagnostics);
        var source = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();
        Assert.Contains("QueryState", source);
        Assert.Contains("OnQueryChanged(__v)", source);
    }

    [Fact]
    public void RunOnLoadCommandFiresInGeneratedRegistration()
    {
        var result = Run(("Cart.razor", """
            @inherits Tempest.StatefulComponent
            @code {
                [Command, RunOnLoad]
                private Task Load(CancellationToken ct) => Task.CompletedTask;
            }
            """));

        Assert.Empty(result.Diagnostics);
        var source = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();
        Assert.Contains("_ = LoadState.TryExecute();", source);
    }

    [Fact]
    public void CanExecuteGateFlowsIntoTheGeneratedPredicate()
    {
        var result = Run(("Cart.razor", """
            @inherits Tempest.StatefulComponent
            @code {
                [Command] private void Next() { }
                [CanExecute] public bool OnNextCanExecute { get; private set; }
            }
            """));

        Assert.Empty(result.Diagnostics);
        var source = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();
        Assert.Contains("() => OnNextCanExecute);", source);
    }

    [Fact]
    public void GeneratedCodeCompilesAgainstTheRuntime()
    {
        var result = Run(("Pages/Cart.razor", Cart));
        var generated = Assert.Single(Assert.Single(result.Results).GeneratedSources);

        var user = CSharpSyntaxTree.ParseText("""
            namespace Demo.Pages
            {
                public partial class Cart : global::Tempest.StatefulComponent
                {
                    private string _query = "";

                    private async System.Threading.Tasks.Task Save(System.Threading.CancellationToken ct)
                        => await System.Threading.Tasks.Task.Delay(1, ct);
                }
            }
            """);

        var compilation = CSharpCompilation.Create(
            "EndToEnd",
            [user, generated.SyntaxTree],
            RuntimeReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            "End-to-end compilation failed:\n" + string.Join("\n", errors) +
            "\n\n" + generated.SourceText);
    }

    private static IReadOnlyList<MetadataReference> RuntimeReferences()
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(System.IO.Path.PathSeparator))
            byName[System.IO.Path.GetFileName(path)] = path;

        foreach (var assembly in new[]
                 {
                     typeof(Tempest.CommandAttribute).Assembly,
                     typeof(Tempest.StatefulComponent).Assembly,
                     typeof(Microsoft.AspNetCore.Components.ComponentBase).Assembly,
                 })
            byName[System.IO.Path.GetFileName(assembly.Location)] = assembly.Location;

        return byName.Values.Select(MetadataReference (p) => MetadataReference.CreateFromFile(p)).ToList();
    }

    [Fact]
    public void SecondRunWithSameInputsIsCached()
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            generators: [new TempestGenerator().AsSourceGenerator()],
            additionalTexts: [new TestAdditionalText("Cart.razor", Cart)],
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        var second = driver
            .RunGenerators(compilation)
            .RunGenerators(compilation)
            .GetRunResult();

        var steps = Assert.Single(second.Results).TrackedOutputSteps;
        Assert.All(
            steps.SelectMany(kvp => kvp.Value).SelectMany(step => step.Outputs),
            output => Assert.Equal(IncrementalStepRunReason.Cached, output.Reason));
    }
}
