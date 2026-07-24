# Tempest — the C# file

One plain C# class is the whole unit. Four attributes declare meaning; a source
generator emits a partial-class twin with the state machinery. No base-class
ceremony beyond inheriting a host, no interfaces to implement, no boilerplate.

```csharp
using Tempest;

namespace Shop;

public sealed partial class TodoStore(ITodoApi api, IEventBus bus) : StatefulStore(bus)
{
    [Reactive] private string _newTitle = "";
    [Reactive] private List<TodoItem> _todos = [];

    [OnChanged]
    private void OnNewTitleChanged(string value) => Validate(value);

    [Command]
    private async Task<List<TodoItem>> Load(CancellationToken ct)
        => await api.GetTodosAsync(ct);

    [Command]
    private async Task Add(CancellationToken ct)
    {
        var created = await api.AddTodoAsync(_newTitle, ct);
        TodosState.Value = [.. _todos, created];
        NewTitleState.Reset();
    }

    public sealed record TodoCompleted(int Id);
    [Event]
    private async Task OnTodoCompleted(TodoCompleted e)
    {
        await api.CompleteAsync(e.Id);
        TodosState.Value = [.. _todos.Where(t => t.Id != e.Id)];
    }
}
```

## The class

- Must be `partial` (the generated twin is the other half) and inherit a host base — TEM002 otherwise:
  - `StatefulComponent` / `StatefulLayoutComponent` — Blazor (also implicit for `.razor` files)
  - `StatefulControl` — XAML (Tempest.Wpf and Tempest.WinUI hosts)
  - `StatefulStore` — headless, constructor-DI, shared by any UI (Tempest.Abstract)
- Constructor injection is ordinary C#; `StatefulStore` takes the `IEventBus` it registers against.

## `[Reactive]` — state the class owns

- On a non-public, non-static field whose name differs from its PascalCase twin (TEM007).
- Generates `{Pascal}State : ReactiveState<T>` — `Value` (writing re-renders and rings the hook), `SetSilently`, `Initial`, `IsDirty`, `Reset()`.

## `[Command]` — work the class does

- Parameterless, or a single trailing `CancellationToken` for latest-wins cancellation (TEM003).
- Generates `{Name}State`: `IsLoading`, `Error`, `Execute()`, `TryExecute()`.
- States implement `ICommand` (Execute → `TryExecute`, CanExecute gates on `IsLoading`, CanExecuteChanged on both loading edges), so XAML `Command="{x:Bind SaveState}"` bindings take them directly; an `[Event, Command]` state takes its record via `CommandParameter`.
- `[Command, RunOnLoad]` runs the command when the host initializes (Blazor `OnInitialized`, XAML `Loaded`, a store's construction) through `TryExecute` — a failing load lands in `{Name}State.Error`. Not valid on an `[Event, Command]` (TEM014): there is no record to run it with.
- Return type picks the state: `void`/`Task`/`ValueTask` → `CommandState`; `T`/`Task<T>`/`ValueTask<T>` → `CommandState<T>` with `Result`/`HasResult`.

## `[Event]` — the outside world reaching in

- Handler takes exactly one parameter: a record **nested in this class** — the record is the contract (TEM001). Anyone publishes it: `Bus.Publish(new TodoStore.TodoCompleted(3))`.
- `[Event, Command]` combines both: an `EventCommandState<TEvent>` named after the record, bus-triggered through `TryExecute` so a publish never throws in the publisher; may add the trailing token.

## `[CanExecute]` — gating a command

- A bool property or parameterless bool method (TEM013); the generated state reads it as its `ICommand.CanExecute` predicate, so bound XAML controls enable and disable themselves.
- Bare `[CanExecute]` resolves by name: `On{Command}CanExecute` (`OnNextCanExecute` → `Next`) — the same grammar as `[OnChanged]`. `[CanExecute(nameof(Next))]` targets explicitly (the command's method name) and frees the member name.
- Unresolvable gate: TEM011. Two gates on one command: first wins, TEM012.

## `[OnChanged]` — reacting to a reactive

- An ordinary method (`void` or `Task`) taking exactly one parameter, the new value (TEM009).
- Bare `[OnChanged]` resolves by name: `On{Field}Changed`. `[OnChanged("_title")]` / `[OnChanged(nameof(_title))]` targets explicitly (field name or its PascalCase twin) and frees the method name.
- Unresolvable hook: TEM008. Two hooks on one field: first wins, TEM010.

## The generated twin

Framework-neutral C#: only `Tempest.Abstract` types plus four host-base members
(`RegisterTempestHandlers`, `SubscribeEvent`, `DispatchEvent`, `InvokeAsync`).
One emitter for every host — Blazor vs XAML vs store is a runtime base-class
difference, never a codegen fork.

```csharp
partial class TodoStore
{
    public CommandState<List<TodoItem>> LoadState { get; }   // lazy, per member
    public CommandState AddState { get; }
    public ReactiveState<string> NewTitleState { get; }      // invokes OnNewTitleChanged
    public ReactiveState<List<TodoItem>> TodosState { get; }

    protected override void RegisterTempestHandlers(IEventBus bus)
    {
        // SubscribeEvent<TodoCompleted>(…); touch reactives so Initial captures initializers
    }
}
```

## Diagnostics

| Id | Severity | Rule |
|---|---|---|
| TEM001 | error | `[Event]` handler must take exactly one nested-record parameter |
| TEM002 | error | class uses Tempest attributes but inherits no host base |
| TEM003 | error | `[Command]` must be parameterless (+ optional trailing ct) |
| TEM007 | error | `[Reactive]` field must be non-public, non-static, name ≠ twin |
| TEM008 | error | `[OnChanged]` matches no `[Reactive]` field |
| TEM009 | error | `[OnChanged]` must take exactly one parameter |
| TEM010 | warning | field already has a hook; duplicate ignored |
| TEM011 | error | `[CanExecute]` matches no `[Command]` |
| TEM012 | warning | command already has a gate; duplicate ignored |
| TEM013 | error | `[CanExecute]` must be a bool property or parameterless bool method |
| TEM014 | error | `[RunOnLoad]` cannot run an `[Event, Command]` |

## Open questions

- ~~**State accessibility**~~ *(resolved)*: the state property mirrors its member's accessibility for Blazor components (markup compiles into the class, so private is reachable), and is forced `public` for `StatefulControl` and `StatefulStore` hosts — WPF's binding engine reads only public properties, and a store's whole point is being consumed from outside. Host policy lives in the compiler; the emitter stays neutral.
- **Host contract**: the four-member surface is a convention between emitter and bases; consider making it a real interface/abstract base in Core.
- **Namespaces**: `Tempest.*` assemblies currently share the root `Tempest` namespace; revisit before a third host ships.
- **Later: `Tempest.WinUI.DependencyInjection`** — container wiring for WinUI (bus from `IServiceProvider`, store registration helpers); today the one-liner `TempestWinUI.Bus = services.GetRequiredService<IEventBus>()` covers it.
