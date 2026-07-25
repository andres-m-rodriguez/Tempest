using Tempest.Parsing;
using Tempest.Pipeline;
using Xunit;

namespace Tempest.Compiler.Tests;

public class TempestCompilerTests
{
    private static readonly TempestCompiler Compiler = new();

    private static SourceMethod Method(
        string name = "Go", bool isCommand = true, bool isEvent = false,
        int parameterCount = 0, bool nestedRecord = false, bool hasCt = false,
        bool runOnLoad = false, HostKind host = HostKind.Component,
        string ns = "App", string component = "C",
        bool needsUsings = false, string fileUsings = "")
        => new(ns, component, name, isCommand, isEvent, runOnLoad, ReturnKind.Void, null, hasCt,
            parameterCount, nestedRecord ? "C.E" : null, nestedRecord ? "E" : null,
            nestedRecord, host, "private", needsUsings, fileUsings, SourceSpan.None);

    private static SourceReactiveProperty Reactive(
        string field = "_x", string property = "X", bool valid = true,
        HostKind host = HostKind.Component, string ns = "App", string component = "C",
        bool needsUsings = false, string fileUsings = "")
        => new(ns, component, field, property, "int", host, valid, "private",
            needsUsings, fileUsings, SourceSpan.None);

    private static SourceHook Hook(
        string method = "OnXChanged", string? target = null, bool returnsTask = false,
        int parameterCount = 1, string ns = "App", string component = "C")
        => new(ns, component, method, target, returnsTask, "value", parameterCount,
            "private", SourceSpan.None);

    private static SourceCanExecute Gate(
        string member = "OnGoCanExecute", string? target = null, bool returnsBool = true,
        bool isMethod = false, int parameterCount = 0, string ns = "App", string component = "C")
        => new(ns, component, member, target, returnsBool, isMethod, parameterCount, SourceSpan.None);

    private static SourceInjection Injection(
        string[]? parameterTypes = null, HostKind host = HostKind.Control,
        string ns = "App", string component = "C")
        => new(ns, component, new EquatableArray<string>(parameterTypes ?? ["global::App.IThing"]),
            host, SourceSpan.None);

    /// <summary>Builds the document the way a frontend does: a combined
    /// [Event, Command] method lands in both arrays.</summary>
    private static TempestDocument Document(
        SourceMethod[]? methods = null, SourceReactiveProperty[]? reactives = null,
        SourceHook[]? hooks = null, SourceCanExecute[]? canExecutes = null,
        SourceInjection[]? injections = null)
    {
        var all = methods ?? [];
        return new TempestDocument(
            new EquatableArray<SourceMethod>(all.Where(m => m.IsCommand).ToArray()),
            new EquatableArray<SourceMethod>(all.Where(m => m.IsEvent).ToArray()),
            new EquatableArray<SourceReactiveProperty>(reactives ?? []),
            new EquatableArray<SourceHook>(hooks ?? []),
            new EquatableArray<SourceCanExecute>(canExecutes ?? []),
            new EquatableArray<SourceInjection>(injections ?? []));
    }

    private static TempestCompilation Compile(params TempestDocument[] documents)
    {
        var result = Compiler.Compile(documents);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public void InjectionIsCarriedForXamlHostsOnly()
    {
        var control = Compile(Document(
            methods: [Method(host: HostKind.Control)],
            injections: [Injection()]));
        var carried = Assert.Single(control.Components).Injection;
        Assert.NotNull(carried);
        Assert.Equal("global::App.IThing", Assert.Single(carried.ParameterTypes));

        var store = Compile(Document(
            methods: [Method(host: HostKind.Store)],
            injections: [Injection(host: HostKind.Store)]));
        Assert.Null(Assert.Single(store.Components).Injection);
    }

    [Fact]
    public void ValidMembersCompileIntoOneComponent()
    {
        var compilation = Compile(Document(methods: [Method()], reactives: [Reactive()]));

        Assert.Empty(compilation.Diagnostics);
        var component = Assert.Single(compilation.Components);
        Assert.Equal("App", component.Namespace);
        Assert.Equal("C", component.Name);
        Assert.Equal(HostKind.Component, component.Host);
        Assert.Equal("Go", Assert.Single(component.Methods).MethodName);
        Assert.Equal("X", Assert.Single(component.Reactives).PropertyName);
    }

    [Fact]
    public void MissingHostBaseSuppressesTheComponent()
    {
        var compilation = Compile(Document(methods: [Method(host: HostKind.None), Method(name: "Ok")]));

        Assert.Empty(compilation.Components);
        var diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("TEM002", diagnostic.Id);
        Assert.Equal(TempestDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void MalformedEventHandlerIsExcludedWithDiagnostic()
    {
        var bad = Method(name: "OnE", isCommand: false, isEvent: true, parameterCount: 1, nestedRecord: false);
        var compilation = Compile(Document(methods: [bad, Method()]));

        Assert.Equal("TEM001", Assert.Single(compilation.Diagnostics).Id);
        Assert.Equal("Go", Assert.Single(Assert.Single(compilation.Components).Methods).MethodName);
    }

    [Fact]
    public void EventWithCancellationTokenNeedsCommand()
    {
        var eventOnly = Method(name: "OnE", isCommand: false, isEvent: true, parameterCount: 1, nestedRecord: true, hasCt: true);
        var eventCommand = Method(name: "OnF", isCommand: true, isEvent: true, parameterCount: 1, nestedRecord: true, hasCt: true);
        var compilation = Compile(Document(methods: [eventOnly, eventCommand]));

        Assert.Equal("TEM001", Assert.Single(compilation.Diagnostics).Id);
        Assert.Equal("OnF", Assert.Single(Assert.Single(compilation.Components).Methods).MethodName);
    }

    [Fact]
    public void CommandWithParametersIsExcludedWithDiagnostic()
    {
        var compilation = Compile(Document(methods: [Method(parameterCount: 1), Method(name: "Ok")]));

        Assert.Equal("TEM003", Assert.Single(compilation.Diagnostics).Id);
        Assert.Equal("Ok", Assert.Single(Assert.Single(compilation.Components).Methods).MethodName);
    }

    [Fact]
    public void InvalidReactiveFieldIsExcludedWithDiagnostic()
    {
        var compilation = Compile(Document(reactives: [Reactive(valid: false), Reactive(field: "_ok", property: "Ok")]));

        Assert.Equal("TEM007", Assert.Single(compilation.Diagnostics).Id);
        Assert.Equal("_ok", Assert.Single(Assert.Single(compilation.Components).Reactives).FieldName);
    }

    [Fact]
    public void CombinedEventCommandKeepsOneCopy()
    {
        var combined = Method(name: "OnE", isCommand: true, isEvent: true, parameterCount: 1, nestedRecord: true);
        var compilation = Compile(Document(methods: [combined]));

        Assert.Empty(compilation.Diagnostics);
        var method = Assert.Single(Assert.Single(compilation.Components).Methods);
        Assert.True(method.IsCommand);
        Assert.True(method.IsEvent);
    }

    [Fact]
    public void ConventionHookWiresToPascalTwin()
    {
        var compilation = Compile(Document(
            reactives: [Reactive()],
            hooks: [Hook("OnXChanged", returnsTask: true)]));

        Assert.Empty(compilation.Diagnostics);
        var hook = Assert.Single(Assert.Single(compilation.Components).Reactives).Hook;
        Assert.NotNull(hook);
        Assert.Equal("OnXChanged", hook.MethodName);
        Assert.True(hook.ReturnsTask);
    }

    [Fact]
    public void ExplicitTargetMatchesFieldNameOrTwin()
    {
        var byField = Compile(Document(reactives: [Reactive()], hooks: [Hook("Whatever", target: "_x")]));
        var byTwin = Compile(Document(reactives: [Reactive()], hooks: [Hook("Whatever", target: "X")]));

        Assert.Equal("Whatever", Assert.Single(Assert.Single(byField.Components).Reactives).Hook?.MethodName);
        Assert.Equal("Whatever", Assert.Single(Assert.Single(byTwin.Components).Reactives).Hook?.MethodName);
    }

    [Fact]
    public void UnmatchedHookIsAnError()
    {
        var compilation = Compile(Document(reactives: [Reactive()], hooks: [Hook("OnNopeChanged")]));

        var diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("TEM008", diagnostic.Id);
        Assert.Equal(TempestDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Null(Assert.Single(Assert.Single(compilation.Components).Reactives).Hook);
    }

    [Fact]
    public void HookOnlyComponentStillDiagnoses()
    {
        var compilation = Compile(Document(hooks: [Hook("OnNopeChanged")]));

        Assert.Empty(compilation.Components);
        Assert.Equal("TEM008", Assert.Single(compilation.Diagnostics).Id);
    }

    [Fact]
    public void WrongArityHookIsAnError()
    {
        var compilation = Compile(Document(
            reactives: [Reactive()],
            hooks: [Hook("OnXChanged", parameterCount: 0)]));

        Assert.Equal("TEM009", Assert.Single(compilation.Diagnostics).Id);
        Assert.Null(Assert.Single(Assert.Single(compilation.Components).Reactives).Hook);
    }

    [Fact]
    public void DuplicateHookWarnsAndFirstWins()
    {
        var compilation = Compile(Document(
            reactives: [Reactive()],
            hooks: [Hook("OnXChanged"), Hook("Second", target: "_x")]));

        var diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("TEM010", diagnostic.Id);
        Assert.Equal(TempestDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("OnXChanged", Assert.Single(Assert.Single(compilation.Components).Reactives).Hook?.MethodName);
    }

    [Fact]
    public void RunOnLoadFlagCarriesThrough()
    {
        var compilation = Compile(Document(methods: [Method(runOnLoad: true)]));

        Assert.True(Assert.Single(Assert.Single(compilation.Components).Methods).RunOnLoad);
    }

    [Fact]
    public void RunOnLoadOnAnEventCommandIsAnError()
    {
        var combined = Method(name: "OnE", isCommand: true, isEvent: true,
            parameterCount: 1, nestedRecord: true, runOnLoad: true);
        var compilation = Compile(Document(methods: [combined]));

        Assert.Equal("TEM014", Assert.Single(compilation.Diagnostics).Id);
        Assert.Empty(compilation.Components);
    }

    [Fact]
    public void ConventionGateWiresToItsCommand()
    {
        var compilation = Compile(Document(methods: [Method()], canExecutes: [Gate("OnGoCanExecute")]));

        Assert.Empty(compilation.Diagnostics);
        var gate = Assert.Single(Assert.Single(compilation.Components).Methods).CanExecute;
        Assert.NotNull(gate);
        Assert.Equal("OnGoCanExecute", gate.MemberName);
        Assert.False(gate.IsMethod);
    }

    [Fact]
    public void CanPrefixedNameAloneIsNotTheConvention()
    {
        var compilation = Compile(Document(methods: [Method()], canExecutes: [Gate("CanGo")]));

        Assert.Equal("TEM011", Assert.Single(compilation.Diagnostics).Id);
    }

    [Fact]
    public void ExplicitGateTargetMatchesCommandName()
    {
        var compilation = Compile(Document(
            methods: [Method()],
            canExecutes: [Gate("HasSession", target: "Go", isMethod: true)]));

        Assert.Empty(compilation.Diagnostics);
        var gate = Assert.Single(Assert.Single(compilation.Components).Methods).CanExecute;
        Assert.Equal("HasSession", gate?.MemberName);
        Assert.True(gate?.IsMethod);
    }

    [Fact]
    public void UnmatchedGateIsAnError()
    {
        var compilation = Compile(Document(methods: [Method()], canExecutes: [Gate("CanNope")]));

        Assert.Equal("TEM011", Assert.Single(compilation.Diagnostics).Id);
        Assert.Null(Assert.Single(Assert.Single(compilation.Components).Methods).CanExecute);
    }

    [Fact]
    public void MisshapenGateIsAnError()
    {
        var notBool = Compile(Document(methods: [Method()], canExecutes: [Gate(returnsBool: false)]));
        var hasParams = Compile(Document(methods: [Method()], canExecutes: [Gate(isMethod: true, parameterCount: 1)]));

        Assert.Equal("TEM013", Assert.Single(notBool.Diagnostics).Id);
        Assert.Equal("TEM013", Assert.Single(hasParams.Diagnostics).Id);
    }

    [Fact]
    public void DuplicateGateWarnsAndFirstWins()
    {
        var compilation = Compile(Document(
            methods: [Method()],
            canExecutes: [Gate("OnGoCanExecute"), Gate("AlsoGates", target: "Go")]));

        var diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("TEM012", diagnostic.Id);
        Assert.Equal(TempestDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("OnGoCanExecute", Assert.Single(Assert.Single(compilation.Components).Methods).CanExecute?.MemberName);
    }

    [Fact]
    public void GateAndCommandWireAcrossDocuments()
    {
        var compilation = Compile(
            Document(methods: [Method()]),
            Document(canExecutes: [Gate("OnGoCanExecute")]));

        Assert.Empty(compilation.Diagnostics);
        Assert.NotNull(Assert.Single(Assert.Single(compilation.Components).Methods).CanExecute);
    }

    [Fact]
    public void HookAndFieldWireAcrossDocuments()
    {
        var compilation = Compile(
            Document(reactives: [Reactive()]),
            Document(hooks: [Hook("OnXChanged")]));

        Assert.Empty(compilation.Diagnostics);
        Assert.NotNull(Assert.Single(Assert.Single(compilation.Components).Reactives).Hook);
    }

    [Fact]
    public void ComponentsMergeAcrossDocumentsAndSplitByName()
    {
        var compilation = Compile(
            Document(methods: [Method(name: "A")]),
            Document(methods: [Method(name: "B"), Method(name: "Other", component: "D")]));

        Assert.Equal(2, compilation.Components.Count);
        var c = Assert.Single(compilation.Components, x => x.Name == "C");
        Assert.Equal(new[] { "A", "B" }, c.Methods.Select(m => m.MethodName));
        Assert.Equal("Other", Assert.Single(Assert.Single(compilation.Components, x => x.Name == "D").Methods).MethodName);
    }

    [Fact]
    public void DuplicateMembersAcrossDocumentsKeepOneCopy()
    {
        var method = Method();
        var compilation = Compile(Document(methods: [method]), Document(methods: [method]));

        Assert.Single(Assert.Single(compilation.Components).Methods);
    }

    [Fact]
    public void UsingsStayEmptyWhenMembersResolveSymbols()
    {
        var compilation = Compile(Document(methods: [Method(fileUsings: "My.Ns")]));

        Assert.Empty(Assert.Single(compilation.Components).Usings);
    }

    [Fact]
    public void UsingsCollectSortedWithAmbientAndWithoutStandard()
    {
        var compilation = Assert.IsType<TempestCompilation>(Compiler.Compile(
            [Document(methods: [Method(needsUsings: true, fileUsings: "Zebra.Ns\nSystem")])],
            ambientUsings: ["Alpha.Ns\nSystem.Threading"]).Value);

        Assert.Equal(
            new[] { "Alpha.Ns", "Zebra.Ns" },
            Assert.Single(compilation.Components).Usings);
    }

    [Fact]
    public void HostKindCarriesThrough()
    {
        var compilation = Compile(Document(methods: [Method(host: HostKind.Store)]));

        Assert.Equal(HostKind.Store, Assert.Single(compilation.Components).Host);
    }

    [Fact]
    public void ControlAndStoreHostsGetPublicStates()
    {
        var control = Compile(Document(
            methods: [Method(host: HostKind.Control)],
            reactives: [Reactive(host: HostKind.Control)]));
        var component = Compile(Document(methods: [Method()]));

        Assert.Equal("public", Assert.Single(Assert.Single(control.Components).Methods).Accessibility);
        Assert.Equal("public", Assert.Single(Assert.Single(control.Components).Reactives).Accessibility);
        Assert.Equal("private", Assert.Single(Assert.Single(component.Components).Methods).Accessibility);
    }

    [Fact]
    public void NullDocumentSequenceFailsWithError()
    {
        var result = Compiler.Compile(null);

        Assert.False(result.IsSuccess);
        Assert.Equal("CMP001", Assert.IsType<InvalidCompileInputError>(result.Error).Code);
    }

    [Fact]
    public void NullDocumentInsideSequenceFailsWithError()
    {
        var result = Compiler.Compile([Document(), null!]);

        Assert.IsType<InvalidCompileInputError>(result.Error);
    }

    [Fact]
    public void EmptyInputCompilesToEmpty()
    {
        Assert.Equal(TempestCompilation.Empty, Compile());
    }

    [Fact]
    public void CompilationIsDeterministicByValue()
    {
        TempestCompilation Run() => Compile(Document(
            methods: [Method(), Method(name: "OnE", isCommand: false, isEvent: true, parameterCount: 1, nestedRecord: true)],
            reactives: [Reactive()]));

        Assert.Equal(Run(), Run());
    }
}
