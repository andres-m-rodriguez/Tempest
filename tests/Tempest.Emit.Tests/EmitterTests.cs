using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Tempest.Emit;
using Tempest.Model;
using Xunit;

namespace Tempest.Emit.Tests;

public class EmitterTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ComponentModel Component(
        string ns = "Demo",
        string name = "Widget",
        MethodModel[]? methods = null,
        ReactiveModel[]? reactives = null,
        string[]? usings = null)
        => new(
            ns,
            name,
            new EquatableArray<MethodModel>(methods ?? []),
            new EquatableArray<ReactiveModel>(reactives ?? []),
            new EquatableArray<string>(usings ?? []));

    private static MethodModel Command(
        string name,
        ReturnKind kind,
        string? resultType = null,
        bool ct = false,
        string accessibility = "public")
        => new(name, IsCommand: true, IsEvent: false, kind, resultType, ct,
               ParamType: null, ParamTypeName: null, accessibility);

    private static MethodModel EventHandler(
        string name,
        ReturnKind kind,
        string paramType,
        string paramTypeName,
        bool isCommand = false,
        bool ct = false,
        string accessibility = "private")
        => new(name, isCommand, IsEvent: true, kind, ResultType: null, ct,
               paramType, paramTypeName, accessibility);

    /// <summary>Emits and asserts the result is syntactically valid C#.</summary>
    private static string EmitValid(ComponentModel component)
    {
        var source = new Emitter().Emit(component);
        var tree = CSharpSyntaxTree.ParseText(source);
        var errors = tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            "Generated source has syntax errors:\n" +
            string.Join("\n", errors) + "\n\n" + source);
        return source;
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n");

    // ── Plain [Command] return-kind coverage ─────────────────────────────────

    [Fact]
    public void VoidCommand_WrapsCallAndReturnsCompletedTask()
    {
        var source = EmitValid(Component(methods: [Command("Clear", ReturnKind.Void)]));
        Assert.Contains("private global::Tempest.CommandState? __clearState;", source);
        Assert.Contains(
            "public global::Tempest.CommandState ClearState => __clearState ??= new global::Tempest.CommandState(this, __ct => { Clear(); return global::System.Threading.Tasks.Task.CompletedTask; });",
            source);
    }

    [Fact]
    public void TaskCommand_PassesDelegateDirectly()
    {
        var source = EmitValid(Component(methods: [Command("Save", ReturnKind.Task)]));
        Assert.Contains(
            "public global::Tempest.CommandState SaveState => __saveState ??= new global::Tempest.CommandState(this, __ct => Save());",
            source);
    }

    [Fact]
    public void TaskOfTCommand_GetsResultBearingState()
    {
        var source = EmitValid(Component(methods: [Command("Load", ReturnKind.TaskOfT, resultType: "int")]));
        Assert.Contains("private global::Tempest.CommandState<int>? __loadState;", source);
        Assert.Contains(
            "public global::Tempest.CommandState<int> LoadState => __loadState ??= new global::Tempest.CommandState<int>(this, __ct => Load());",
            source);
    }

    [Fact]
    public void ValueTaskCommand_AdaptsWithAsTask()
    {
        var source = EmitValid(Component(methods: [Command("Sync", ReturnKind.ValueTask)]));
        Assert.Contains(
            "public global::Tempest.CommandState SyncState => __syncState ??= new global::Tempest.CommandState(this, __ct => Sync().AsTask());",
            source);
    }

    [Fact]
    public void ValueTaskOfTCommand_GetsResultBearingState_AndAsTask()
    {
        var source = EmitValid(Component(methods: [Command("Fetch", ReturnKind.ValueTaskOfT, resultType: "string")]));
        Assert.Contains(
            "public global::Tempest.CommandState<string> FetchState => __fetchState ??= new global::Tempest.CommandState<string>(this, __ct => Fetch().AsTask());",
            source);
    }

    [Fact]
    public void SyncResultCommand_WrapsInTaskFromResult()
    {
        var source = EmitValid(Component(methods: [Command("Compute", ReturnKind.Sync, resultType: "double")]));
        Assert.Contains(
            "public global::Tempest.CommandState<double> ComputeState => __computeState ??= new global::Tempest.CommandState<double>(this, __ct => global::System.Threading.Tasks.Task.FromResult(Compute()));",
            source);
    }

    [Fact]
    public void CommandWithCancellationToken_ForwardsTheToken()
    {
        var source = EmitValid(Component(methods: [Command("Save", ReturnKind.Task, ct: true)]));
        Assert.Contains("__ct => Save(__ct));", source);
    }

    [Fact]
    public void CommandWithoutCancellationToken_CallsWithNoArguments()
    {
        var source = EmitValid(Component(methods: [Command("Save", ReturnKind.Task, ct: false)]));
        Assert.Contains("__ct => Save());", source);
    }

    [Fact]
    public void PlainCommandsOnly_NoRegisterTempestHandlersOverride()
    {
        var source = EmitValid(Component(methods: [Command("Save", ReturnKind.Task)]));
        Assert.DoesNotContain("RegisterTempestHandlers", source);
    }

    // ── [Event]-only handlers ────────────────────────────────────────────────

    [Fact]
    public void EventOnlyTaskHandler_SubscribesViaDispatchEvent_NoStateProperty()
    {
        var source = EmitValid(Component(methods:
            [EventHandler("OnPing", ReturnKind.Task, "global::Demo.Widget.Ping", "Ping")]));
        Assert.Contains(
            "SubscribeEvent<global::Demo.Widget.Ping>(e => DispatchEvent(() => OnPing(e)));",
            source);
        Assert.DoesNotContain("State =>", source);
    }

    [Fact]
    public void EventOnlyValueTaskHandler_AdaptsWithAsTask()
    {
        var source = EmitValid(Component(methods:
            [EventHandler("OnPing", ReturnKind.ValueTask, "global::Demo.Widget.Ping", "Ping")]));
        Assert.Contains(
            "SubscribeEvent<global::Demo.Widget.Ping>(e => DispatchEvent(() => OnPing(e).AsTask()));",
            source);
    }

    [Fact]
    public void EventOnlyVoidHandler_WrapsCallInBlockLambda()
    {
        var source = EmitValid(Component(methods:
            [EventHandler("OnPing", ReturnKind.Void, "global::Demo.Widget.Ping", "Ping")]));
        Assert.Contains(
            "SubscribeEvent<global::Demo.Widget.Ping>(e => DispatchEvent(() => { OnPing(e); }));",
            source);
    }

    // ── [Event, Command] ─────────────────────────────────────────────────────

    [Fact]
    public void EventCommand_EmitsEventCommandState_NamedAfterTheRecord()
    {
        var source = EmitValid(Component(methods:
            [EventHandler("OnRefresh", ReturnKind.Task, "global::Demo.Widget.Refresh", "Refresh", isCommand: true)]));
        Assert.Contains("private global::Tempest.EventCommandState<global::Demo.Widget.Refresh>? __refreshState;", source);
        Assert.Contains(
            "private global::Tempest.EventCommandState<global::Demo.Widget.Refresh> RefreshState => __refreshState ??= new global::Tempest.EventCommandState<global::Demo.Widget.Refresh>(this, (__e, __ct) => OnRefresh(__e));",
            source);
    }

    [Fact]
    public void EventCommand_SubscribesViaTryExecute()
    {
        // Bus-triggered commands run through TryExecute so a publish can't blow up
        // in the publisher; the error lands in {Name}State.Error.
        var source = EmitValid(Component(methods:
            [EventHandler("OnRefresh", ReturnKind.Task, "global::Demo.Widget.Refresh", "Refresh", isCommand: true)]));
        Assert.Contains(
            "SubscribeEvent<global::Demo.Widget.Refresh>(e => InvokeAsync(() => RefreshState.TryExecute(e)));",
            source);
    }

    [Fact]
    public void EventCommandWithCancellationToken_ForwardsBothArguments()
    {
        var source = EmitValid(Component(methods:
            [EventHandler("OnRefresh", ReturnKind.Task, "global::Demo.Widget.Refresh", "Refresh", isCommand: true, ct: true)]));
        Assert.Contains("(__e, __ct) => OnRefresh(__e, __ct));", source);
    }

    [Fact]
    public void EventCommandVoidHandler_WrapsCallAndReturnsCompletedTask()
    {
        var source = EmitValid(Component(methods:
            [EventHandler("OnRefresh", ReturnKind.Void, "global::Demo.Widget.Refresh", "Refresh", isCommand: true)]));
        Assert.Contains(
            "(__e, __ct) => { OnRefresh(__e); return global::System.Threading.Tasks.Task.CompletedTask; });",
            source);
    }

    // ── [Reactive] ───────────────────────────────────────────────────────────

    [Fact]
    public void ReactiveWithoutHook_PassesNullHook_NoPartialDeclaration()
    {
        var source = EmitValid(Component(reactives:
            [new ReactiveModel("_count", "Count", "int", Hook: null, "private")]));
        Assert.Contains("private global::Tempest.ReactiveState<int>? __countState;", source);
        Assert.Contains("private global::Tempest.ReactiveState<int> CountState => __countState ??= new global::Tempest.ReactiveState<int>(", source);
        Assert.Contains("() => _count,", source);
        Assert.Contains("__v => _count = __v,", source);
        Assert.Contains("            null);", source);
        Assert.DoesNotContain("partial", Normalize(source).Replace("partial class", ""));
    }

    [Fact]
    public void ReactiveWithVoidHook_WrapsCallInCompletedTask_NoDeclaration()
    {
        var source = EmitValid(Component(reactives:
            [new ReactiveModel("_name", "Name", "string", new HookModel("OnNameChanged", ReturnsTask: false), "private")]));
        Assert.DoesNotContain("partial void OnNameChanged", source);
        Assert.Contains("__v => { OnNameChanged(__v); return global::System.Threading.Tasks.Task.CompletedTask; });", source);
    }

    [Fact]
    public void ReactiveWithTaskHook_PassesDelegateDirectly_NoDeclaration()
    {
        var source = EmitValid(Component(reactives:
            [new ReactiveModel("_items", "Items", "List<string>", new HookModel("OnItemsChanged", ReturnsTask: true), "private")]));
        Assert.DoesNotContain("partial global::System.Threading.Tasks.Task OnItemsChanged", source);
        Assert.Contains("__v => OnItemsChanged(__v));", source);
    }


    [Fact]
    public void Reactives_AreTouchedInRegisterTempestHandlers()
    {
        // Touching each reactive state makes Initial capture the field's initializer value.
        var source = EmitValid(Component(reactives:
            [
                new ReactiveModel("_count", "Count", "int", null, "private"),
                new ReactiveModel("_name", "Name", "string", null, "private"),
            ]));
        Assert.Contains("protected override void RegisterTempestHandlers(global::Tempest.IEventBus bus)", source);
        Assert.Contains("_ = CountState;", source);
        Assert.Contains("_ = NameState;", source);
    }

    // ── Usings, namespace, accessibility ─────────────────────────────────────

    [Fact]
    public void Usings_AreEmittedAfterTheStandardSet()
    {
        var source = Normalize(EmitValid(Component(
            methods: [Command("Save", ReturnKind.Task)],
            usings: ["Demo.Shared", "Microsoft.AspNetCore.Components"])));
        Assert.Contains(
            "using System;\nusing System.Collections.Generic;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing Demo.Shared;\nusing Microsoft.AspNetCore.Components;\n",
            source);
    }

    [Fact]
    public void Header_HasAutoGeneratedCommentAndNullableEnable()
    {
        var source = Normalize(EmitValid(Component(methods: [Command("Save", ReturnKind.Task)])));
        Assert.StartsWith("// <auto-generated by Tempest />\n#nullable enable\n", source);
    }

    [Fact]
    public void EmptyNamespace_EmitsClassAtTopLevel()
    {
        var source = Normalize(EmitValid(Component(ns: "", methods: [Command("Save", ReturnKind.Task)])));
        Assert.DoesNotContain("namespace", source);
        Assert.Contains("\npartial class Widget\n{\n", source);
        Assert.Contains("\n    public global::Tempest.CommandState SaveState", source);
    }

    [Fact]
    public void NamedNamespace_WrapsClassInNamespaceBlock()
    {
        var source = Normalize(EmitValid(Component(ns: "Demo.App", methods: [Command("Save", ReturnKind.Task)])));
        Assert.Contains("namespace Demo.App\n{\n    partial class Widget\n    {\n", source);
        Assert.EndsWith("    }\n}\n", source);
    }

    [Fact]
    public void Accessibility_OfMemberPropagatesToStateProperty()
    {
        var source = EmitValid(Component(
            methods: [Command("Save", ReturnKind.Task, accessibility: "internal")],
            reactives: [new ReactiveModel("_count", "Count", "int", null, "protected")]));
        Assert.Contains("internal global::Tempest.CommandState SaveState", source);
        Assert.Contains("protected global::Tempest.ReactiveState<int> CountState", source);
    }

    // ── Hint name ────────────────────────────────────────────────────────────

    [Fact]
    public void HintName_IncludesNamespaceWhenPresent()
        => Assert.Equal("Demo.App.Widget.Tempest.g.cs",
            Emitter.HintName(Component(ns: "Demo.App", name: "Widget")));

    [Fact]
    public void HintName_OmitsNamespaceWhenGlobal()
        => Assert.Equal("Widget.Tempest.g.cs",
            Emitter.HintName(Component(ns: "", name: "Widget")));

    // ── Golden case: full-string equality over every feature at once ─────────

    [Fact]
    public void GoldenComponent_MatchesExactExpectedOutput()
    {
        var component = new ComponentModel(
            "Demo.App",
            "Counter",
            new EquatableArray<MethodModel>(
            [
                new MethodModel("Save", IsCommand: true, IsEvent: false, ReturnKind.Task,
                    ResultType: null, HasCancellationToken: true,
                    ParamType: null, ParamTypeName: null, "public"),
                new MethodModel("OnRefresh", IsCommand: true, IsEvent: true, ReturnKind.Task,
                    ResultType: null, HasCancellationToken: false,
                    "global::Demo.App.Counter.Refresh", "Refresh", "private"),
            ]),
            new EquatableArray<ReactiveModel>(
            [
                new ReactiveModel("_count", "Count", "int",
                    new HookModel("OnCountChanged", ReturnsTask: true),
                    "private"),
            ]),
            new EquatableArray<string>(["Demo.Shared"]));

        var expected = """
            // <auto-generated by Tempest />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Demo.Shared;

            namespace Demo.App
            {
                partial class Counter
                {
                    private global::Tempest.CommandState? __saveState;

                    /// <summary>State for the Save command: IsLoading, Error, Execute, TryExecute.</summary>
                    public global::Tempest.CommandState SaveState => __saveState ??= new global::Tempest.CommandState(this, __ct => Save(__ct));

                    private global::Tempest.EventCommandState<global::Demo.App.Counter.Refresh>? __refreshState;

                    /// <summary>State for the Refresh command: IsLoading, Error, Execute, TryExecute.</summary>
                    private global::Tempest.EventCommandState<global::Demo.App.Counter.Refresh> RefreshState => __refreshState ??= new global::Tempest.EventCommandState<global::Demo.App.Counter.Refresh>(this, (__e, __ct) => OnRefresh(__e));

                    private global::Tempest.ReactiveState<int>? __countState;

                    /// <summary>State for the _count field: Value, SetSilently, IsDirty, Reset.</summary>
                    private global::Tempest.ReactiveState<int> CountState => __countState ??= new global::Tempest.ReactiveState<int>(
                        this,
                        () => _count,
                        __v => _count = __v,
                        _count,
                        __v => OnCountChanged(__v));

                    protected override void RegisterTempestHandlers(global::Tempest.IEventBus bus)
                    {
                        SubscribeEvent<global::Demo.App.Counter.Refresh>(e => InvokeAsync(() => RefreshState.TryExecute(e)));
                        _ = CountState;
                    }
                }
            }

            """;

        var actual = EmitValid(component);
        Assert.Equal(Normalize(expected), Normalize(actual));
    }

    // ── Syntactic validity of a kitchen-sink component ───────────────────────

    [Fact]
    public void KitchenSink_ParsesWithoutSyntaxErrors()
    {
        var component = Component(
            ns: "Demo.App",
            name: "Kitchen",
            methods:
            [
                Command("A", ReturnKind.Void),
                Command("B", ReturnKind.Task, ct: true),
                Command("C", ReturnKind.TaskOfT, resultType: "int"),
                Command("D", ReturnKind.ValueTask),
                Command("E", ReturnKind.ValueTaskOfT, resultType: "string", ct: true),
                Command("F", ReturnKind.Sync, resultType: "bool"),
                EventHandler("OnP", ReturnKind.Task, "global::Demo.App.Kitchen.P", "P"),
                EventHandler("OnQ", ReturnKind.ValueTask, "global::Demo.App.Kitchen.Q", "Q"),
                EventHandler("OnR", ReturnKind.Void, "global::Demo.App.Kitchen.R", "R"),
                EventHandler("OnS", ReturnKind.Task, "global::Demo.App.Kitchen.S", "S", isCommand: true, ct: true),
            ],
            reactives:
            [
                new ReactiveModel("_a", "A2", "int", null, "private"),
                new ReactiveModel("_b", "B2", "string", new HookModel("OnB2Changed", false), "private"),
                new ReactiveModel("_c", "C2", "List<int>", new HookModel("OnC2Changed", true), "protected"),
            ],
            usings: ["Demo.Shared"]);

        EmitValid(component);
    }
}

