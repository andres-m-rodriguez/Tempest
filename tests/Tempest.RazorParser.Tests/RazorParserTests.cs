using Tempest.Model;
using Tempest.Model.Entry;
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

    private static SourceEntries ParseCart()
        => Parser.Parse(new RazorSource("Pages/Cart.razor", Cart, "Demo", "Pages/Cart.razor"));

    private static EntryMethod Method(SourceEntries result, string name)
        => Assert.Single(result.Methods, m => m.MethodName == name);

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
        Assert.True(save.FromRazor);
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
        var handler = Method(ParseCart(), "OnItemAdded");

        Assert.True(handler.IsEvent);
        Assert.False(handler.IsCommand);
        Assert.Equal(1, handler.ParameterCount);
        Assert.Equal("ItemAdded", handler.ParamTypeName);
        Assert.True(handler.ParamIsNestedRecordOfComponent);
    }

    [Fact]
    public void ReactiveFieldGetsPascalTwinAndHook()
    {
        var reactive = Assert.Single(ParseCart().Reactives);

        Assert.Equal("_query", reactive.FieldName);
        Assert.Equal("Query", reactive.PropertyName);
        Assert.Equal("string", reactive.TypeText);
        Assert.True(reactive.IsValidField);
        Assert.Equal("private", reactive.Accessibility);
        Assert.NotNull(reactive.Hook);
        Assert.Equal("OnQueryChanged", reactive.Hook!.MethodName);
        Assert.False(reactive.Hook.ReturnsTask);
        Assert.Equal("value", reactive.Hook.ParamName);
        Assert.False(reactive.Hook.IsPartial);
    }

    [Fact]
    public void ComponentIdentityComesFromFileAndBuildProperties()
    {
        var result = ParseCart();
        var save = Method(result, "Save");

        Assert.Equal("Cart", save.ComponentName);
        Assert.Equal("Demo.Pages", save.Namespace);
        Assert.True(save.InheritsHostBase);
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
    }

    [Fact]
    public void ExplicitNamespaceDirectiveWins()
    {
        var text = "@namespace Custom.Ns\n@code { [Command] void Go() { } }";
        var result = Parser.Parse(new RazorSource("A.razor", text, "Root", "Sub/A.razor"));

        Assert.Equal("Custom.Ns", Method(result, "Go").Namespace);
    }

    [Fact]
    public void NamespaceFallsBackToRootPlusTargetDirectory()
    {
        var text = "@code { [Command] void Go() { } }";
        var result = Parser.Parse(new RazorSource("A.razor", text, "My.App", "Pages/Sub-Dir/A.razor"));

        Assert.Equal("My.App.Pages.Sub_Dir", Method(result, "Go").Namespace);
    }

    [Fact]
    public void ComponentNameIsSanitized()
    {
        var text = "@code { [Command] void Go() { } }";
        var result = Parser.Parse(new RazorSource("Widgets/1my-widget.razor", text));

        Assert.Equal("_1my_widget", Method(result, "Go").ComponentName);
    }

    [Fact]
    public void MissingInheritsIsFlagged()
    {
        var text = "@code { [Command] void Go() { } }";
        var result = Parser.Parse(new RazorSource("A.razor", text));

        Assert.False(Method(result, "Go").InheritsHostBase);
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
        var result = Parser.Parse(new RazorSource("A.razor", text));

        Assert.Equal(3, result.Reactives.Count);
        Assert.All(result.Reactives, r => Assert.False(r.IsValidField));
    }

    [Fact]
    public void MultipleDeclaratorsYieldOneReactiveEach()
    {
        var text = "@code { [Reactive] private int _a, _b; }";
        var result = Parser.Parse(new RazorSource("A.razor", text));

        Assert.Equal(2, result.Reactives.Count);
        Assert.Equal(new[] { "A", "B" }, result.Reactives.Select(r => r.PropertyName));
    }

    [Fact]
    public void HookInAnotherCodeBlockStillWires()
    {
        var text = """
            @code {
                [Reactive] private string _title = "";
            }
            <p>markup between blocks</p>
            @code {
                private async Task OnTitleChanged(string v) { await Task.Yield(); }
            }
            """;
        var result = Parser.Parse(new RazorSource("A.razor", text));

        var reactive = Assert.Single(result.Reactives);
        Assert.NotNull(reactive.Hook);
        Assert.True(reactive.Hook!.ReturnsTask);
    }

    [Fact]
    public void FileWithoutTempestAttributesIsEmpty()
    {
        var text = "@code { private int _plain; void Helper() { } }";
        Assert.Equal(SourceEntries.Empty, Parser.Parse(new RazorSource("A.razor", text)));
    }

    [Fact]
    public void AttributeNameVariantsAreRecognized()
    {
        var text = "@code { [CommandAttribute] void Go() { } }";
        var result = Parser.Parse(new RazorSource("A.razor", text));

        Assert.True(Method(result, "Go").IsCommand);
    }
}
