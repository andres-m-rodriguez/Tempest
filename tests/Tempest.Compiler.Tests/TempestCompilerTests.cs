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
        string field = "_x", string property = "X", bool valid = true, bool inherits = true,
        string ns = "App", string component = "C", bool fromRazor = false, string fileUsings = "")
        => new(ns, component, field, property, "int", inherits, valid, "private",
            fromRazor, fileUsings, SourceSpan.None);

    private static EntryHook Hook(
        string method = "OnXChanged", string? target = null, bool returnsTask = false,
        int parameterCount = 1, string ns = "App", string component = "C")
        => new(ns, component, method, target, returnsTask, parameterCount, SourceSpan.None);

    private static SourceEntries Entries(
        EntryMethod[]? methods = null, EntryReactive[]? reactives = null, EntryHook[]? hooks = null)
        => new(
            new EquatableArray<EntryMethod>(methods ?? []),
            new EquatableArray<EntryReactive>(reactives ?? []),
            new EquatableArray<EntryHook>(hooks ?? []));

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
        var result = Compiler.Compile([Entries(reactives: [Reactive(valid: false), Reactive(field: "_ok", property: "Ok")])]);

        Assert.Equal("TEM007", Assert.Single(result.Diagnostics).Id);
        Assert.Equal("_ok", Assert.Single(Assert.Single(result.Components).Reactives).FieldName);
    }

    [Fact]
    public void ConventionHookWiresToPascalTwin()
    {
        var result = Compiler.Compile([Entries(
            reactives: [Reactive()],
            hooks: [Hook("OnXChanged", returnsTask: true)])]);

        Assert.Empty(result.Diagnostics);
        var hook = Assert.Single(Assert.Single(result.Components).Reactives).Hook;
        Assert.NotNull(hook);
        Assert.Equal("OnXChanged", hook!.MethodName);
        Assert.True(hook.ReturnsTask);
    }

    [Fact]
    public void ExplicitTargetMatchesFieldName()
    {
        var result = Compiler.Compile([Entries(
            reactives: [Reactive()],
            hooks: [Hook("Whatever", target: "_x")])]);

        Assert.Empty(result.Diagnostics);
        Assert.Equal("Whatever", Assert.Single(Assert.Single(result.Components).Reactives).Hook!.MethodName);
    }

    [Fact]
    public void ExplicitTargetMatchesPascalTwin()
    {
        var result = Compiler.Compile([Entries(
            reactives: [Reactive()],
            hooks: [Hook("Whatever", target: "X")])]);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(Assert.Single(Assert.Single(result.Components).Reactives).Hook);
    }

    [Fact]
    public void UnmatchedHookIsAnError()
    {
        var result = Compiler.Compile([Entries(
            reactives: [Reactive()],
            hooks: [Hook("OnNopeChanged")])]);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TEM008", diagnostic.Id);
        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.Null(Assert.Single(Assert.Single(result.Components).Reactives).Hook);
    }

    [Fact]
    public void WrongShapeHookIsAnError()
    {
        var result = Compiler.Compile([Entries(
            reactives: [Reactive()],
            hooks: [Hook("OnXChanged", parameterCount: 2)])]);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TEM009", diagnostic.Id);
        Assert.Null(Assert.Single(Assert.Single(result.Components).Reactives).Hook);
    }

    [Fact]
    public void DuplicateHooksKeepFirstAndWarn()
    {
        var result = Compiler.Compile([Entries(
            reactives: [Reactive()],
            hooks: [Hook("OnXChanged"), Hook("AlsoWatches", target: "_x")])]);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TEM010", diagnostic.Id);
        Assert.Equal(Severity.Warning, diagnostic.Severity);
        Assert.Equal("OnXChanged", Assert.Single(Assert.Single(result.Components).Reactives).Hook!.MethodName);
    }

    [Fact]
    public void HookOnlyComponentStillReportsUnmatched()
    {
        var result = Compiler.Compile([Entries(hooks: [Hook("OnXChanged")])]);

        Assert.Empty(result.Components);
        Assert.Equal("TEM008", Assert.Single(result.Diagnostics).Id);
    }

    [Fact]
    public void HookAndFieldFromDifferentSourcesStillWire()
    {
        var result = Compiler.Compile([
            Entries(reactives: [Reactive()]),
            Entries(hooks: [Hook("OnXChanged")])]);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(Assert.Single(Assert.Single(result.Components).Reactives).Hook);
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

                [OnChanged]
                private void OnQueryChanged(string v) { _ = v; }

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
        var reactive = Assert.Single(component.Reactives);
        Assert.Equal("Query", reactive.PropertyName);
        Assert.Equal("OnQueryChanged", reactive.Hook!.MethodName);
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
        var input = new[] { Entries(methods: [Method()], reactives: [Reactive()], hooks: [Hook("OnXChanged")]) };

        Assert.Equal(Compiler.Compile(input), Compiler.Compile(input));
    }
}
