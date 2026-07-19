using Tempest.Model;
using Tempest.Model.Entry;
using Xunit;

namespace Tempest.Compiler.Tests;

public class TempestCompilerTests
{
    private static readonly TempestCompiler Compiler = new();

    private static EntryMethod Method(
        string name = "Go", bool isCommand = true, bool isEvent = false,
        int parameterCount = 0, bool nestedRecord = false, bool hasCt = false,
        bool inherits = true, string ns = "App", string component = "C",
        bool fromRazor = false, string fileUsings = "")
        => new(ns, component, name, isCommand, isEvent, ReturnKind.Void, null, hasCt,
            parameterCount, nestedRecord ? "C.E" : null, nestedRecord ? "E" : null,
            nestedRecord, inherits, "private", fromRazor, fileUsings, SourceSpan.None);

    private static EntryReactive Reactive(
        string field = "_x", bool valid = true, bool inherits = true, EntryHook? hook = null,
        string ns = "App", string component = "C", bool fromRazor = false, string fileUsings = "")
        => new(ns, component, field, "X", "int", hook, inherits, valid, "private",
            fromRazor, fileUsings, SourceSpan.None);

    private static SourceEntries Entries(EntryMethod[]? methods = null, EntryReactive[]? reactives = null)
        => new(
            new EquatableArray<EntryMethod>(methods ?? []),
            new EquatableArray<EntryReactive>(reactives ?? []));

    [Fact]
    public void ValidMembersCompileIntoOneComponent()
    {
        var result = Compiler.Compile([Entries(methods: [Method()], reactives: [Reactive()])]);

        Assert.Empty(result.Diagnostics);
        var component = Assert.Single(result.Components);
        Assert.Equal("App", component.Namespace);
        Assert.Equal("C", component.Name);
        Assert.Equal("Go", Assert.Single(component.Methods).MethodName);
        Assert.Equal("X", Assert.Single(component.Reactives).PropertyName);
    }

    [Fact]
    public void MissingHostBaseSuppressesTheComponent()
    {
        var result = Compiler.Compile([Entries(methods: [Method(inherits: false), Method(name: "Ok")])]);

        Assert.Empty(result.Components);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TEM002", diagnostic.Id);
        Assert.Equal(Severity.Error, diagnostic.Severity);
    }

    [Fact]
    public void MalformedEventHandlerIsExcludedWithDiagnostic()
    {
        var bad = Method(name: "OnE", isCommand: false, isEvent: true, parameterCount: 1, nestedRecord: false);
        var result = Compiler.Compile([Entries(methods: [bad, Method()])]);

        Assert.Equal("TEM001", Assert.Single(result.Diagnostics).Id);
        Assert.Equal("Go", Assert.Single(Assert.Single(result.Components).Methods).MethodName);
    }

    [Fact]
    public void EventWithCancellationTokenNeedsCommand()
    {
        var eventOnly = Method(name: "OnE", isCommand: false, isEvent: true, parameterCount: 1, nestedRecord: true, hasCt: true);
        var eventCommand = eventOnly with { MethodName = "OnF", IsCommand = true };
        var result = Compiler.Compile([Entries(methods: [eventOnly, eventCommand])]);

        Assert.Equal("TEM001", Assert.Single(result.Diagnostics).Id);
        Assert.Equal("OnF", Assert.Single(Assert.Single(result.Components).Methods).MethodName);
    }

    [Fact]
    public void CommandWithParametersIsExcludedWithDiagnostic()
    {
        var result = Compiler.Compile([Entries(methods: [Method(parameterCount: 1), Method(name: "Ok")])]);

        Assert.Equal("TEM003", Assert.Single(result.Diagnostics).Id);
        Assert.Equal("Ok", Assert.Single(Assert.Single(result.Components).Methods).MethodName);
    }

    [Fact]
    public void InvalidReactiveFieldIsExcludedWithDiagnostic()
    {
        var result = Compiler.Compile([Entries(reactives: [Reactive(valid: false), Reactive(field: "_ok")])]);

        Assert.Equal("TEM007", Assert.Single(result.Diagnostics).Id);
        Assert.Equal("_ok", Assert.Single(Assert.Single(result.Components).Reactives).FieldName);
    }

    [Fact]
    public void NonPartialHookIsDroppedWithWarning()
    {
        var hook = new EntryHook("private", ReturnsTask: false, "v", IsPartial: false, "OnXChanged", SourceSpan.None);
        var result = Compiler.Compile([Entries(reactives: [Reactive(hook: hook)])]);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TEM008", diagnostic.Id);
        Assert.Equal(Severity.Warning, diagnostic.Severity);
        Assert.Null(Assert.Single(Assert.Single(result.Components).Reactives).Hook);
    }

    [Fact]
    public void PartialHookIsCarriedIntoTheModel()
    {
        var hook = new EntryHook("private", ReturnsTask: true, "v", IsPartial: true, "OnXChanged", SourceSpan.None);
        var result = Compiler.Compile([Entries(reactives: [Reactive(hook: hook)])]);

        Assert.Empty(result.Diagnostics);
        var model = Assert.Single(Assert.Single(result.Components).Reactives).Hook;
        Assert.NotNull(model);
        Assert.Equal("OnXChanged", model!.MethodName);
        Assert.True(model.ReturnsTask);
    }

    [Fact]
    public void DuplicateMethodsAcrossSourcesAreDeduped()
    {
        var result = Compiler.Compile([Entries(methods: [Method()]), Entries(methods: [Method()])]);

        Assert.Single(Assert.Single(result.Components).Methods);
    }

    [Fact]
    public void MembersGroupByNamespaceAndComponent()
    {
        var result = Compiler.Compile([Entries(methods:
            [Method(component: "A"), Method(component: "B"), Method(ns: "Other", component: "A")])]);

        Assert.Equal(3, result.Components.Count);
    }

    [Fact]
    public void RazorMemberUsingsAreMergedSortedAndFiltered()
    {
        var method = Method(fromRazor: true, fileUsings: "My.Lib\nSystem.Threading");
        var result = Compiler.Compile([Entries(methods: [method])], importsUsings: ["Zebra.Ns\nSystem"]);

        Assert.Equal(new[] { "My.Lib", "Zebra.Ns" }, Assert.Single(result.Components).Usings);
    }

    [Fact]
    public void PureCSharpComponentsGetNoUsings()
    {
        var result = Compiler.Compile([Entries(methods: [Method()])], importsUsings: ["Some.Ns"]);

        Assert.Empty(Assert.Single(result.Components).Usings);
    }

    [Fact]
    public void RazorParseFeedsStraightIntoCompilation()
    {
        var text = """
            @inherits Tempest.StatefulComponent
            @code {
                [Reactive] private string _query = "";

                [Command]
                private Task Save() => Task.CompletedTask;
            }
            """;
        var parsed = new Tempest.RazorParser.RazorParser().Parse(
            new Tempest.RazorParser.RazorSource("Pages/Cart.razor", text, "Demo", "Pages/Cart.razor"));
        var result = Compiler.Compile([parsed]);

        Assert.Empty(result.Diagnostics);
        var component = Assert.Single(result.Components);
        Assert.Equal("Cart", component.Name);
        Assert.Equal("Demo.Pages", component.Namespace);
        Assert.Equal("Save", Assert.Single(component.Methods).MethodName);
        Assert.Equal("Query", Assert.Single(component.Reactives).PropertyName);
    }

    [Fact]
    public void NoInputYieldsNothing()
    {
        var result = Compiler.Compile([]);

        Assert.Empty(result.Components);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void CompilingTwiceIsValueEqual()
    {
        var input = new[] { Entries(methods: [Method()], reactives: [Reactive()]) };

        Assert.Equal(Compiler.Compile(input), Compiler.Compile(input));
    }
}
