using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Tempest.Compiler;
using Tempest.Pipeline;
using Tempest.Parsing;
using Xunit;

namespace Tempest.Emit.Tests;

public class TempestEmitterTests
{
    private static readonly TempestEmitter Emitter = new();

    private static CompiledComponent Component(
        string ns = "Demo",
        string name = "Widget",
        CompiledMethod[]? methods = null,
        CompiledReactive[]? reactives = null,
        CompiledInjection? injection = null,
        string[]? usings = null)
        => new(
            ns,
            name,
            HostKind.Component,
            new EquatableArray<CompiledMethod>(methods ?? []),
            new EquatableArray<CompiledReactive>(reactives ?? []),
            injection,
            new EquatableArray<string>(usings ?? []));

    private static CompiledMethod Command(
        string name, ReturnKind kind, string? resultType = null, bool ct = false,
        CompiledCanExecute? canExecute = null, bool runOnLoad = false,
        string accessibility = "public")
        => new(name, IsCommand: true, IsEvent: false, runOnLoad, kind, resultType, ct,
            ParamType: null, ParamTypeName: null, canExecute, accessibility);

    private static CompiledMethod EventHandler(
        string name, ReturnKind kind, string paramType, string paramTypeName,
        bool isCommand = false, bool ct = false, string accessibility = "private")
        => new(name, isCommand, IsEvent: true, RunOnLoad: false, kind, ResultType: null, ct,
            paramType, paramTypeName, CanExecute: null, accessibility);

    private static CompiledReactive Reactive(
        string field = "_title", string property = "Title", string type = "string",
        CompiledHook? hook = null, string accessibility = "private")
        => new(field, property, type, hook, accessibility);

    /// <summary>Emits and asserts the result is syntactically valid C#.</summary>
    private static string EmitValid(CompiledComponent component)
    {
        var result = Emitter.Emit(component);
        Assert.True(result.IsSuccess);

        var source = result.Value.Source;
        var errors = CSharpSyntaxTree.ParseText(source).GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            "Generated source has syntax errors:\n" + string.Join("\n", errors) + "\n\n" + source);
        return source;
    }

    [Fact]
    public void InjectionEmitsParameterlessBridgeConstructor()
    {
        var source = EmitValid(Component(
            methods: [Command("Save", ReturnKind.Task)],
            injection: new CompiledInjection(new EquatableArray<string>(
                ["global::App.IThing", "global::App.IOther"]))));

        Assert.Contains("public Widget()", source);
        Assert.Contains("global::Tempest.TempestServices.Resolve<global::App.IThing>(),", source);
        Assert.Contains("global::Tempest.TempestServices.Resolve<global::App.IOther>())", source);
        Assert.Contains("InitializeComponent();", source);
    }

    [Fact]
    public void VoidCommandWrapsCallAndReturnsCompletedTask()
    {
        var source = EmitValid(Component(methods: [Command("Clear", ReturnKind.Void)]));

        Assert.Contains("private global::Tempest.CommandState? __clearState;", source);
        Assert.Contains(
            "public global::Tempest.CommandState ClearState => __clearState ??= new global::Tempest.CommandState(this, __ct => { Clear(); return global::System.Threading.Tasks.Task.CompletedTask; });",
            source);
    }

    [Fact]
    public void TaskCommandPassesDelegateDirectly()
    {
        var source = EmitValid(Component(methods: [Command("Save", ReturnKind.Task)]));

        Assert.Contains(
            "public global::Tempest.CommandState SaveState => __saveState ??= new global::Tempest.CommandState(this, __ct => Save());",
            source);
    }

    [Fact]
    public void TaskOfTCommandGetsResultBearingState()
    {
        var source = EmitValid(Component(methods: [Command("Load", ReturnKind.TaskOfT, resultType: "int")]));

        Assert.Contains("private global::Tempest.CommandState<int>? __loadState;", source);
        Assert.Contains("__loadState ??= new global::Tempest.CommandState<int>(this, __ct => Load());", source);
    }

    [Fact]
    public void ValueTaskCommandsAdaptWithAsTask()
    {
        var source = EmitValid(Component(methods: [
            Command("Sync", ReturnKind.ValueTask),
            Command("Fetch", ReturnKind.ValueTaskOfT, resultType: "string")]));

        Assert.Contains("__ct => Sync().AsTask());", source);
        Assert.Contains("new global::Tempest.CommandState<string>(this, __ct => Fetch().AsTask());", source);
    }

    [Fact]
    public void SyncResultCommandWrapsInTaskFromResult()
    {
        var source = EmitValid(Component(methods: [Command("Compute", ReturnKind.Sync, resultType: "double")]));

        Assert.Contains(
            "__ct => global::System.Threading.Tasks.Task.FromResult(Compute()));",
            source);
    }

    [Fact]
    public void CanExecuteGatesEmitAsPredicates()
    {
        var property = EmitValid(Component(methods: [
            Command("Next", ReturnKind.Task, canExecute: new CompiledCanExecute("CanNext", IsMethod: false))]));
        var method = EmitValid(Component(methods: [
            Command("Save", ReturnKind.Task, canExecute: new CompiledCanExecute("HasSession", IsMethod: true))]));

        Assert.Contains("new global::Tempest.CommandState(this, __ct => Next(), () => CanNext);", property);
        Assert.Contains("new global::Tempest.CommandState(this, __ct => Save(), () => HasSession());", method);
    }

    [Fact]
    public void CommandWithCancellationTokenForwardsIt()
    {
        var source = EmitValid(Component(methods: [Command("Save", ReturnKind.Task, ct: true)]));

        Assert.Contains("__ct => Save(__ct));", source);
    }

    [Fact]
    public void EventCommandGetsEventCommandStateNamedAfterRecord()
    {
        var source = EmitValid(Component(methods: [
            EventHandler("OnAdded", ReturnKind.Task, "Widget.ItemAdded", "ItemAdded", isCommand: true)]));

        Assert.Contains("private global::Tempest.EventCommandState<Widget.ItemAdded>? __itemAddedState;", source);
        Assert.Contains("ItemAddedState => __itemAddedState ??=", source);
        Assert.Contains(
            "SubscribeEvent<Widget.ItemAdded>(e => InvokeAsync(() => ItemAddedState.TryExecute(e)));",
            source);
    }

    [Fact]
    public void PlainEventHandlersSubscribeThroughDispatchEvent()
    {
        var source = EmitValid(Component(methods: [
            EventHandler("OnTask", ReturnKind.Task, "Widget.A", "A"),
            EventHandler("OnVoid", ReturnKind.Void, "Widget.B", "B")]));

        Assert.Contains("SubscribeEvent<Widget.A>(e => DispatchEvent(() => OnTask(e)));", source);
        Assert.Contains("SubscribeEvent<Widget.B>(e => DispatchEvent(() => { OnVoid(e); }));", source);
    }

    [Fact]
    public void ReactiveStateWiresFieldAndTouchesInRegistration()
    {
        var source = EmitValid(Component(reactives: [Reactive()]));

        Assert.Contains("private global::Tempest.ReactiveState<string>? __titleState;", source);
        Assert.Contains("private global::Tempest.ReactiveState<string> TitleState => __titleState ??=", source);
        Assert.Contains("() => _title,", source);
        Assert.Contains("__v => _title = __v,", source);
        Assert.Contains("_ = TitleState;", source);
    }

    [Fact]
    public void HookedReactiveInvokesItsHook()
    {
        var voidHook = EmitValid(Component(reactives: [Reactive(hook: new CompiledHook("OnTitleChanged", ReturnsTask: false))]));
        var taskHook = EmitValid(Component(reactives: [Reactive(hook: new CompiledHook("OnTitleChanged", ReturnsTask: true))]));

        Assert.Contains("__v => { OnTitleChanged(__v); return global::System.Threading.Tasks.Task.CompletedTask; });", voidHook);
        Assert.Contains("__v => OnTitleChanged(__v));", taskHook);
    }

    [Fact]
    public void CommandsAloneEmitNoRegistrationOverride()
    {
        var source = EmitValid(Component(methods: [Command("Save", ReturnKind.Task)]));

        Assert.DoesNotContain("RegisterTempestHandlers", source);
    }

    [Fact]
    public void RunOnLoadCommandFiresInRegistration()
    {
        var source = EmitValid(Component(methods: [Command("Load", ReturnKind.Task, runOnLoad: true)]));

        Assert.Contains("RegisterTempestHandlers", source);
        Assert.Contains("_ = LoadState.TryExecute();", source);
    }

    [Fact]
    public void UsingsAndNamespaceAreEmitted()
    {
        var source = EmitValid(Component(usings: ["My.Models", "System.Text"], reactives: [Reactive()]));

        Assert.Contains("using My.Models;", source);
        Assert.Contains("using System.Text;", source);
        Assert.Contains("namespace Demo", source);
        Assert.Contains("partial class Widget", source);
    }

    [Fact]
    public void GlobalNamespaceComponentEmitsWithoutNamespaceBlock()
    {
        var result = Emitter.Emit(Component(ns: "", reactives: [Reactive()]));

        Assert.True(result.IsSuccess);
        Assert.Equal("Widget.Tempest.g.cs", result.Value.HintName);
        Assert.DoesNotContain("namespace", result.Value.Source.Replace("global::System", ""));
    }

    [Fact]
    public void HintNameIsNamespaceQualified()
    {
        var result = Emitter.Emit(Component(reactives: [Reactive()]));

        Assert.True(result.IsSuccess);
        Assert.Equal("Demo.Widget.Tempest.g.cs", result.Value.HintName);
    }

    [Fact]
    public void NullComponentFailsWithError()
    {
        var result = Emitter.Emit(null);

        Assert.False(result.IsSuccess);
        Assert.Equal("EMT001", Assert.IsType<InvalidEmitInputError>(result.Error).Code);
    }

    [Fact]
    public void EmissionIsDeterministicByValue()
    {
        GeneratedFile Run()
        {
            var result = Emitter.Emit(Component(
                methods: [Command("Save", ReturnKind.Task)],
                reactives: [Reactive()]));
            Assert.True(result.IsSuccess);
            return result.Value;
        }

        Assert.Equal(Run(), Run());
    }
}
