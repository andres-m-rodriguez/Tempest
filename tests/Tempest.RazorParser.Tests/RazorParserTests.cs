using Tempest.Pipeline;
using Tempest.Parsing;
using Xunit;

namespace Tempest.RazorParser.Tests;

public class RazorParserTests
{
    private static readonly RazorParser Parser = new();

    private const string Cart = """
        @page "/cart"
        @using System.Text
        @inherits Tempest.StatefulComponent

        <h1>Cart</h1>

        @code {
            [Reactive] private string _query = "";

            [OnChanged]
            private void OnQueryChanged(string value) { _ = value; }

            public sealed record ItemAdded(int Id);

            [Event]
            private void OnItemAdded(ItemAdded e) { }

            [Command]
            private async Task Save(CancellationToken ct) => await Task.Delay(1, ct);

            [Command]
            private Task<int> Count() => Task.FromResult(0);

            [Command]
            protected int Total() => 0;

            [Command]
            private ValueTask Flush() => default;

            [Command]
            private System.Threading.Tasks.Task Go() => Task.CompletedTask;
        }
        """;

    /// <summary>The full document, unwrapped — these fixtures are all well-formed
    /// sources, so failure is a test failure.</summary>
    private static TempestDocument ParseAll(RazorSource source)
    {
        var document = Parser.ParseDocument(source);
        Assert.True(document.IsSuccess);
        return document.Value;
    }

    private static TempestDocument ParseAll(string componentName, string text, string fallbackNamespace = "")
        => ParseAll(new RazorSource(componentName, text, fallbackNamespace));

    private static TempestDocument ParseCart()
        => ParseAll(new RazorSource("Cart", Cart, "Demo.Pages"));

    private static SourceMethod Method(TempestDocument result, string name)
        => Assert.Single(result.Commands.Concat(result.Events).Distinct(), m => m.MethodName == name);

    [Fact]
    public void CommandWithCancellationToken()
    {
        var save = Method(ParseCart(), "Save");

        Assert.True(save.IsCommand);
        Assert.False(save.IsEvent);
        Assert.Equal(ReturnKind.Task, save.Kind);
        Assert.Null(save.ResultType);
        Assert.True(save.HasCancellationToken);
        Assert.Equal(0, save.ParameterCount);
        Assert.Equal("private", save.Accessibility);
        Assert.True(save.NeedsAmbientUsings);
    }

    [Fact]
    public void ReturnKindsClassifyFromText()
    {
        var result = ParseCart();

        Assert.Equal((ReturnKind.TaskOfT, "int"), (Method(result, "Count").Kind, Method(result, "Count").ResultType));
        Assert.Equal((ReturnKind.Sync, "int"), (Method(result, "Total").Kind, Method(result, "Total").ResultType));
        Assert.Equal(ReturnKind.ValueTask, Method(result, "Flush").Kind);
        Assert.Equal(ReturnKind.Task, Method(result, "Go").Kind);
    }

    [Fact]
    public void EventHandlerSeesNestedRecord()
    {
        var result = ParseCart();
        var handler = Method(result, "OnItemAdded");

        Assert.True(handler.IsEvent);
        Assert.False(handler.IsCommand);
        Assert.Equal(1, handler.ParameterCount);
        Assert.Equal("ItemAdded", handler.ParamTypeName);
        Assert.True(handler.ParamIsNestedRecordOfComponent);
        Assert.DoesNotContain(result.Commands, m => m.MethodName == "OnItemAdded");
    }

    [Fact]
    public void CombinedEventCommandAppearsInBothArraysIdentically()
    {
        var text = """
            @code {
                public sealed record Ping(int Id);
                [Event, Command]
                private Task OnPing(Ping e) => Task.CompletedTask;
            }
            """;
        var result = ParseAll("A", text);

        var asCommand = Assert.Single(result.Commands);
        var asEvent = Assert.Single(result.Events);
        Assert.Equal(asCommand, asEvent);
        Assert.True(asCommand.IsCommand);
        Assert.True(asCommand.IsEvent);
    }

    [Fact]
    public void ReactiveFieldGetsPascalTwin()
    {
        var reactive = Assert.Single(ParseCart().Reactives);

        Assert.Equal("_query", reactive.FieldName);
        Assert.Equal("Query", reactive.PropertyName);
        Assert.Equal("string", reactive.TypeText);
        Assert.True(reactive.IsValidField);
        Assert.Equal("private", reactive.Accessibility);
        Assert.True(reactive.NeedsAmbientUsings);
    }

    [Fact]
    public void BareOnChangedIsCollectedUnresolved()
    {
        var hook = Assert.Single(ParseCart().Hooks);

        Assert.Equal("OnQueryChanged", hook.MethodName);
        Assert.Null(hook.ExplicitTarget);
        Assert.Equal("value", hook.ParamName);
        Assert.Equal(1, hook.ParameterCount);
        Assert.Equal("private", hook.Accessibility);
        Assert.False(hook.ReturnsTask);
        Assert.Equal("Cart", hook.ComponentName);
        Assert.Equal("Demo.Pages", hook.Namespace);
    }

    [Fact]
    public void ExplicitOnChangedTargetsAreExtracted()
    {
        var text = """
            @code {
                [Reactive] private int _count;
                [Reactive] private int _total;
                [OnChanged("_count")] private void CountBumped(int v) { }
                [OnChanged(nameof(Total))] private void TotalBumped(int v) { }
            }
            """;
        var result = ParseAll("A", text);

        Assert.Equal(new[] { "_count", "Total" }, result.Hooks.Select(h => h.ExplicitTarget));
    }

    [Fact]
    public void DuplicateHooksAreBothCollected()
    {
        var text = """
            @code {
                [Reactive] private int _count;
                [OnChanged("_count")] private void First(int v) { }
                [OnChanged(nameof(_count))] private void Second(int v) { }
            }
            """;
        var result = ParseAll("A", text);

        // Which one wins — and the TEM010 warning — is the compiler's decision.
        Assert.Equal(new[] { "First", "Second" }, result.Hooks.Select(h => h.MethodName));
    }

    [Fact]
    public void CanExecuteMembersAreCollectedUnresolved()
    {
        var text = """
            @code {
                [Command] private void Next() { }
                [CanExecute] public bool CanNext { get; private set; }
                [CanExecute(nameof(Next))] private bool HasSession() => true;
                [CanExecute] public int NotABool { get; set; }
            }
            """;
        var result = ParseAll("A", text);

        Assert.Equal(3, result.CanExecutes.Count);

        var property = Assert.Single(result.CanExecutes, c => c.MemberName == "CanNext");
        Assert.Null(property.ExplicitTarget);
        Assert.True(property.ReturnsBool);
        Assert.False(property.IsMethod);

        var method = Assert.Single(result.CanExecutes, c => c.MemberName == "HasSession");
        Assert.Equal("Next", method.ExplicitTarget);
        Assert.True(method.ReturnsBool);
        Assert.True(method.IsMethod);
        Assert.Equal(0, method.ParameterCount);

        Assert.False(Assert.Single(result.CanExecutes, c => c.MemberName == "NotABool").ReturnsBool);
    }

    [Fact]
    public void UnattributedOnMethodIsNotAHook()
    {
        var text = """
            @code {
                [Reactive] private int _x;
                private void OnXChanged(int v) { }
            }
            """;
        var result = ParseAll("A", text);

        Assert.Empty(result.Hooks);
    }

    [Fact]
    public void ComponentIdentityComesFromTheSourceValues()
    {
        var save = Method(ParseCart(), "Save");

        Assert.Equal("Cart", save.ComponentName);
        Assert.Equal("Demo.Pages", save.Namespace);
        Assert.Equal(HostKind.Component, save.Host);
        Assert.Equal("System.Text", save.FileUsings);
    }

    [Fact]
    public void SpanPointsAtTheIdentifierInTheOriginalFile()
    {
        var save = Method(ParseCart(), "Save");

        Assert.Equal("Save", Cart.Substring(save.Span.Start, save.Span.Length));
        Assert.Equal(Cart.Take(save.Span.Start).Count(c => c == '\n'), save.Span.StartLine);
    }

    [Fact]
    public void ParsingIsDeterministicByValue()
    {
        Assert.Equal(ParseCart(), ParseCart());
        Assert.Equal(ParseCart(), new TempestDocument(
            new RazorParser().ParseCommands(new RazorSource("Cart", Cart, "Demo.Pages")).Value,
            new RazorParser().ParseEvents(new RazorSource("Cart", Cart, "Demo.Pages")).Value,
            new RazorParser().ParseReactiveProperties(new RazorSource("Cart", Cart, "Demo.Pages")).Value,
            new RazorParser().ParseHooks(new RazorSource("Cart", Cart, "Demo.Pages")).Value,
            new RazorParser().ParseCanExecutes(new RazorSource("Cart", Cart, "Demo.Pages")).Value,
            new RazorParser().ParseInjections(new RazorSource("Cart", Cart, "Demo.Pages")).Value));
    }

    [Fact]
    public void SuccessCarriesNoError()
    {
        var result = Parser.ParseCommands(new RazorSource("Cart", Cart, "Demo.Pages"));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public void NullSourceFailsWithInvalidSourceError()
    {
        var result = Parser.ParseCommands(null!);

        Assert.False(result.IsSuccess);
        var error = Assert.IsType<InvalidRazorSourceError>(result.Error);
        Assert.Equal("RZP001", error.Code);
        Assert.Equal("", error.ComponentName);
    }

    [Fact]
    public void NullTextFailsWithInvalidSourceError()
    {
        var result = Parser.ParseReactiveProperties(new RazorSource("A", null!));

        var error = Assert.IsType<InvalidRazorSourceError>(result.Error);
        Assert.Equal("A", error.ComponentName);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void EmptyComponentNameFailsWithInvalidSourceError()
    {
        var result = Parser.ParseEvents(new RazorSource("", "@code { }"));

        Assert.IsType<InvalidRazorSourceError>(result.Error);
    }

    [Fact]
    public void NullFallbackNamespaceFailsWithInvalidSourceError()
    {
        var result = Parser.ParseCommands(new RazorSource("A", "@code { }", FallbackNamespace: null!));

        Assert.IsType<InvalidRazorSourceError>(result.Error);
    }

    [Fact]
    public void AllThreeMethodsSurfaceTheSameError()
    {
        var source = new RazorSource("A", null!);

        Assert.Equal(Parser.ParseCommands(source).Error, Parser.ParseEvents(source).Error);
        Assert.Equal(Parser.ParseCommands(source).Error, Parser.ParseReactiveProperties(source).Error);
    }

    [Fact]
    public void ExplicitNamespaceDirectiveWins()
    {
        var text = "@namespace Custom.Ns\n@code { [Command] void Go() { } }";
        var result = ParseAll("A", text, "Root.Sub");

        Assert.Equal("Custom.Ns", Method(result, "Go").Namespace);
    }

    [Fact]
    public void FallbackNamespaceAppliesWithoutADirective()
    {
        var text = "@code { [Command] void Go() { } }";
        var result = ParseAll("A", text, "My.App.Pages");

        Assert.Equal("My.App.Pages", Method(result, "Go").Namespace);
    }

    [Fact]
    public void ComponentNameIsSanitized()
    {
        var text = "@code { [Command] void Go() { } }";
        var result = ParseAll("1my-widget", text);

        Assert.Equal("_1my_widget", Method(result, "Go").ComponentName);
    }

    [Fact]
    public void MissingInheritsIsTheImplicitComponentHost()
    {
        var text = "@code { [Command] void Go() { } }";
        var result = ParseAll("A", text);

        Assert.Equal(HostKind.Component, Method(result, "Go").Host);
    }

    [Theory]
    [InlineData("Tempest.StatefulComponent", HostKind.Component)]
    [InlineData("StatefulComponent", HostKind.Component)]
    [InlineData("Tempest.StatefulLayoutComponent", HostKind.LayoutComponent)]
    [InlineData("StatefulControl", HostKind.Control)]
    [InlineData("Tempest.StatefulPage", HostKind.Control)]
    [InlineData("Tempest.StatefulStore", HostKind.Store)]
    [InlineData("SomeOtherBase", HostKind.None)]
    [InlineData("My.Custom.PageBase", HostKind.None)]
    public void InheritsDirectiveMapsToHostKind(string baseType, HostKind expected)
    {
        var text = $"@inherits {baseType}\n@code {{ [Command] void Go() {{ }} }}";
        var result = ParseAll("A", text);

        Assert.Equal(expected, Method(result, "Go").Host);
    }

    [Fact]
    public void PublicOrStaticReactiveFieldsAreInvalid()
    {
        var text = """
            @code {
                [Reactive] public string _a = "";
                [Reactive] private static string _b = "";
                [Reactive] private string Same = "";
            }
            """;
        var result = ParseAll("A", text);

        Assert.Equal(3, result.Reactives.Count);
        Assert.All(result.Reactives, r => Assert.False(r.IsValidField));
    }

    [Fact]
    public void MultipleDeclaratorsYieldOneReactiveEach()
    {
        var text = "@code { [Reactive] private int _a, _b; }";
        var result = ParseAll("A", text);

        Assert.Equal(2, result.Reactives.Count);
        Assert.Equal(new[] { "A", "B" }, result.Reactives.Select(r => r.PropertyName));
    }

    [Fact]
    public void HookInAnotherCodeBlockIsCollected()
    {
        var text = """
            @code {
                [Reactive] private string _title = "";
            }
            <p>markup between blocks</p>
            @code {
                [OnChanged]
                private async Task OnTitleChanged(string v) { await Task.Yield(); }
            }
            """;
        var result = ParseAll("A", text);

        var hook = Assert.Single(result.Hooks);
        Assert.Equal("OnTitleChanged", hook.MethodName);
        Assert.True(hook.ReturnsTask);
    }

    [Fact]
    public void FileWithoutTempestAttributesIsEmpty()
    {
        var text = "@code { private int _plain; void Helper() { } }";
        Assert.Equal(TempestDocument.Empty, ParseAll("A", text));
    }

    [Fact]
    public void AttributeNameVariantsAreRecognized()
    {
        var text = "@code { [CommandAttribute] void Go() { } }";
        var result = ParseAll("A", text);

        Assert.True(Method(result, "Go").IsCommand);
    }

    // Block extraction is owned by RazorTokenizer's C#-aware brace scan; its guarantees
    // are asserted through the parser: a closing brace hidden in any literal or comment
    // must not end the @code block early.

    [Fact]
    public void BraceInsideStringDoesNotEndTheBlock()
    {
        var text = """@code { string s = "}"; [Command] void After() { } }""";
        var result = ParseAll("A", text);

        Assert.True(Method(result, "After").IsCommand);
    }

    [Fact]
    public void BraceInsideVerbatimAndInterpolatedStringsDoesNotEndTheBlock()
    {
        var text = """@code { string v = @"} "" }"; string i = $@"} x"; [Command] void After() { } }""";
        var result = ParseAll("A", text);

        Assert.True(Method(result, "After").IsCommand);
    }

    [Fact]
    public void EscapedQuoteAndCharLiteralBracesDoNotEndTheBlock()
    {
        var text = """@code { string s = "\"}"; char c = '}'; char q = '\''; [Command] void After() { } }""";
        var result = ParseAll("A", text);

        Assert.True(Method(result, "After").IsCommand);
    }

    [Fact]
    public void BracesInsideCommentsDoNotEndTheBlock()
    {
        var text = "@code { // } not the end\n /* } */ [Command] void After() { } }";
        var result = ParseAll("A", text);

        Assert.True(Method(result, "After").IsCommand);
    }

    [Fact]
    public void NestedBracesStayInsideTheBlock()
    {
        var text = "@code { [Command] void Go() { if (true) { } } }\n<p>tail</p>";
        var result = ParseAll("A", text);

        Assert.True(Method(result, "Go").IsCommand);
    }

    [Fact]
    public void EscapedAtIsNotACodeBlock()
    {
        var text = "@@code { [Command] void Go() { } }";
        Assert.Equal(TempestDocument.Empty, ParseAll("A", text));
    }

    [Fact]
    public void MethodsAcrossMultipleBlocksAllParse()
    {
        var text = "@code { [Command] void A() { } }\n<p>mid</p>\n@code { [Command] void B() { } }";
        var result = ParseAll("A", text);

        Assert.Equal(2, result.Commands.Count);
        Assert.Equal(new[] { "A", "B" }, result.Commands.Select(m => m.MethodName));
    }
}
