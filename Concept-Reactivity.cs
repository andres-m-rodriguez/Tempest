// ═════════════════════════════════════════════════════════════════════════════════════════
//  Concept: the reactivity core
//  One platform-neutral C# layer. Blazor sits on it today; WinUI sits on it later.
// ═════════════════════════════════════════════════════════════════════════════════════════
//
//  Tempest today is one assembly that thinks in Blazor: states call `Rerender()`, which is
//  `InvokeAsync(StateHasChanged)` wearing a hat. But nothing about the ideas is Blazor's —
//  the write ladder, latest-wins commands, partial hooks, the doorbell bus: all plain C#.
//  The only genuinely platform-specific question is:
//
//      "a state changed — how does that become pixels?"
//
//  Blazor's answer: re-render the owning component (coarse — the renderer diffs).
//  WinUI's answer:  raise PropertyChanged so {x:Bind} updates (fine — per property).
//
//  So the core emits ONE change signal with BOTH granularities, and each host consumes the
//  one it understands. Everything else — lifecycle, cancellation, hooks, equality checks —
//  lives here, written once, tested once, headless.
//
//  The split:
//
//      Tempest            ← this file's world. netstandard2.0. Zero UI references.
//                           States, host contract, bus, attributes.
//      Tempest.Blazor     ← StatefulComponent/StatefulLayoutComponent implement the host.
//                           The razor-parsing half of the generator lives on this side.
//      Tempest.WinUI      ← TempestObservable implements the host over DispatcherQueue.
//                           Generator targets partial ViewModel classes (MVVM-Toolkit-shaped).
//      Tempest.Testing    ← a headless host. States become unit-testable with no UI at all.
//
//  The DSL — [Command] / [Reactive] / [Event], the {Name}State twin, the On{Name}Changed
//  hook — is identical on every platform. A ViewModel written against the core moves
//  between hosts without edits; only the markup dialect differs.
//
// ═════════════════════════════════════════════════════════════════════════════════════════

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;             // BCL, not WPF — ICommand ships in netstandard2.0.

namespace Tempest;

// ─────────────────────────────────────────────────────────────────────────────────────────
//  1. The host boundary — the ENTIRE platform surface is these two methods
// ─────────────────────────────────────────────────────────────────────────────────────────
//
//  Today's `ITempestComponent` says `Rerender()` — a Blazor verb. The neutral verb is
//  "this state changed"; whether that means a render, a binding update, or nothing is the
//  host's business. `DispatchReaction` generalizes to `Dispatch`: "run this on my thread,
//  and if it throws, surface it the way this platform surfaces event-handler exceptions."
//
//  THE THREADING INVARIANT — the whole concept rests on it:
//
//      The core is thread-free. It never marshals, never locks, never touches a
//      SynchronizationContext. Every state mutation happens on the host's thread because
//      every entry point is either (a) a UI gesture, already on that thread, or (b) an
//      async continuation or bus delivery that arrives through Dispatch. The core runs
//      where the host puts it — full stop.
//
//  Consequences per host:
//    Blazor  — Dispatch = InvokeAsync(...), errors → DispatchExceptionAsync (unchanged
//              from today). StateChanged = InvokeAsync(StateHasChanged); the renderer
//              already coalesces queued renders, so notify-per-property costs nothing.
//    WinUI   — Dispatch = DispatcherQueue.TryEnqueue(...), errors rethrown on the UI
//              thread so they hit the app's UnhandledException like any event handler.
//              StateChanged = no-op: PropertyChanged (below) already reached {x:Bind},
//              and it fired on the right thread because of the invariant.
//    Testing — Dispatch = run inline (or capture for stepped replay), errors recorded
//              for assertion. StateChanged = increment a counter you can assert on.

/// <summary>The platform boundary. Implemented by StatefulComponent (Blazor),
/// TempestObservable (WinUI) and TestHost (headless); the core calls it, never
/// the other way around.</summary>
public interface ITempestHost
{
    /// <summary>A state twin mutated. Blazor schedules a render; WinUI ignores it
    /// (PropertyChanged already did the fine-grained work); tests count it.</summary>
    void StateChanged(StateBase state);

    /// <summary>Run work on the host's thread. An exception surfaces the way this
    /// platform surfaces a throwing event handler — never an unobserved task.</summary>
    void Dispatch(Func<Task> work);
}

// ─────────────────────────────────────────────────────────────────────────────────────────
//  2. StateBase — one mutation, two signals
// ─────────────────────────────────────────────────────────────────────────────────────────
//
//  Every state twin (reactive and command alike) derives from StateBase. A mutation calls
//  Set(...), which emits both granularities of the same fact:
//
//      PropertyChanged("IsLoading")   → fine-grained. XAML {x:Bind Mode=OneWay} consumes
//                                       it natively. Blazor ignores it. Free to raise —
//                                       INotifyPropertyChanged is System.ComponentModel,
//                                       not a UI reference.
//      host.StateChanged(this)        → coarse-grained. Blazor consumes it (re-render and
//                                       diff). WinUI ignores it.
//
//  This is the load-bearing trick of the whole file: the core doesn't pick a granularity,
//  it emits both and lets the host subscribe to its native one. No adapter layer, no
//  translation, no "Blazor mode" flag.

/// <summary>Common signaling for all state twins. Mutations go through
/// <see cref="Set{T}"/>, which raises <see cref="PropertyChanged"/> (for XAML bindings)
/// and notifies the host (for render-based UIs) — the same fact at both granularities.</summary>
public abstract class StateBase(ITempestHost host) : INotifyPropertyChanged
{
    private readonly ITempestHost _host = host;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Change-check → assign → signal both consumers. Returns false on a
    /// same-value write so callers can skip dependent work (hooks, CanExecute).</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string property = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Notify(property);
        return true;
    }

    /// <summary>Signal a change that didn't flow through <see cref="Set{T}"/>
    /// (e.g. a computed property whose inputs moved).</summary>
    protected void Notify(string property)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        _host.StateChanged(this);
    }

    /// <summary>Run a hook or continuation on the host's thread; a throw surfaces
    /// like a throwing event handler.</summary>
    protected void Dispatch(Func<Task> work) => _host.Dispatch(work);
}

// ─────────────────────────────────────────────────────────────────────────────────────────
//  3. ReactiveState<T> — the write ladder, unchanged, one level down
// ─────────────────────────────────────────────────────────────────────────────────────────
//
//  Byte-for-byte the semantics of Concept-State.md — only the signaling seams moved into
//  StateBase. The ladder survives intact because none of its rungs ever was Blazor:
//
//      SearchTextState.Value = x          renders/notifies + rings the hook
//      SearchTextState.SetSilently(x)     renders/notifies, no hook
//      _searchText = x                    nothing; a later signal picks it up
//
//  In WinUI the ladder maps onto binding modes with no ceremony:
//      {x:Bind Vm.SearchTextState.Value, Mode=TwoWay}    ← the top rung IS TwoWay binding
//  and the hook stays the orchestration point exactly as in Blazor — a TextBox edit rings
//  On{Name}Changed on the ViewModel, which fans out to sibling states in plain C#.

/// <summary>Generated per [Reactive] field: the field's public twin. Identical on every
/// platform; only who binds to <see cref="Value"/> differs.</summary>
public sealed class ReactiveState<T>(
    ITempestHost host, Func<T> getter, Action<T> setter, T initial, Func<T, Task>? hook = null)
    : StateBase(host)
{
    private readonly Func<T> _getter = getter;      // the states WRAP the user's field —
    private readonly Action<T> _setter = setter;    // reads always see it, the component
    private readonly Func<T, Task>? _hook = hook;   // can still touch it (bottom rung).

    /// <summary>The field's value when this state was created (its initializer, in practice).</summary>
    public T Initial { get; } = initial;

    /// <summary>True when <see cref="Value"/> differs from <see cref="Initial"/>.</summary>
    public bool IsDirty => !EqualityComparer<T>.Default.Equals(_getter(), Initial);

    /// <summary>Change-check → assign → signal → ring the hook. The top rung.</summary>
    public T Value
    {
        get => _getter();
        set
        {
            if (EqualityComparer<T>.Default.Equals(_getter(), value))
                return;                                   // no-op writes ring nothing —
            _setter(value);                               // this is what makes hook
            Notify(nameof(Value));                        // cycles terminate.
            Notify(nameof(IsDirty));
            if (_hook is { } hook)
                Dispatch(() => hook(value));
        }
    }

    /// <summary>Change-check → assign → signal — the hook is not called. The middle rung:
    /// how reactions adjust their siblings without cascading.</summary>
    public void SetSilently(T value)
    {
        if (EqualityComparer<T>.Default.Equals(_getter(), value))
            return;
        _setter(value);
        Notify(nameof(Value));
        Notify(nameof(IsDirty));
    }

    /// <summary>Back to <see cref="Initial"/>, silently.</summary>
    public void Reset() => SetSilently(Initial);
}

// ─────────────────────────────────────────────────────────────────────────────────────────
//  4. Commands — same lifecycle, plus ICommand for free
// ─────────────────────────────────────────────────────────────────────────────────────────
//
//  CommandStateBase's RunAsync — versioned latest-wins, superseded runs discarded on all
//  three exits — moves here UNCHANGED except that `IsLoading`/`Error` become Set(...)
//  properties on StateBase. That logic was never Blazor's; it was always plain async C#.
//
//  The XAML bonus: ICommand lives in the BCL (System.Windows.Input, netstandard2.0), so
//  the CORE can implement it — no WinUI reference needed, and Blazor simply never asks.
//
//      <Button Command="{x:Bind Vm.SearchUsersState}" />                        ← WinUI
//      <button @onclick="SearchUsersState.TryExecute">                          ← Blazor
//
//  ICommand maps onto surface we already have:
//      Execute(_)          → TryExecute()     (a click can't observe a throw anyway —
//                                              same reasoning as bus-triggered commands)
//      CanExecute(_)       → !IsLoading       (buttons auto-disable while running)
//      CanExecuteChanged   → raised whenever IsLoading flips
//
//  Sketch — full lifecycle omitted, it is today's CommandState.cs verbatim:

/// <summary>Surface shared by all command states. Implements ICommand from the BCL so
/// XAML buttons bind the state directly; Blazor binds TryExecute as before.</summary>
public abstract class CommandStateBase(ITempestHost host) : StateBase(host), ICommand
{
    private bool _isLoading;
    private Exception? _error;

    /// <summary>True while the command runs; signaled on both edges.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private protected set
        {
            if (Set(ref _isLoading, value))
            {
                Notify(nameof(IsError));                 // computed neighbors move too
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsError => _error is not null;

    /// <summary>The exception caught by the last TryExecute, cleared on the next run.</summary>
    public Exception? Error
    {
        get => _error;
        private protected set
        {
            if (Set(ref _error, value))
                Notify(nameof(IsError));
        }
    }

    public void ClearError() => Error = null;

    // RunAsync<TResult>(invoke, propagate, commit) — the versioned latest-wins lifecycle —
    // is today's implementation, verbatim. Its Rerender() calls simply disappear: the
    // IsLoading/Error property setters above now carry the signaling. One refinement the
    // move makes natural: the host disposes its states on teardown, cancelling any
    // in-flight run (see §5) — the fix for commands outliving their component.

    public event EventHandler? CanExecuteChanged;
    bool ICommand.CanExecute(object? parameter) => !IsLoading;
    void ICommand.Execute(object? parameter) { /* → TryExecute(); typed subclasses route the parameter. */ }
}

// CommandState / CommandState<TResult> / EventCommandState<TEvent> keep their exact public
// surface (Execute, TryExecute, Result, HasResult) — nothing about them was platform-bound.
// `Result` commits through Set(...), so a WinUI ItemsControl bound to
// {x:Bind Vm.LoadTodosState.Result} refreshes on commit with zero extra code.

// ─────────────────────────────────────────────────────────────────────────────────────────
//  5. The hosts — what each platform actually writes
// ─────────────────────────────────────────────────────────────────────────────────────────
//
//  Blazor (Tempest.Blazor) — StatefulComponent, reduced to an adapter:
//
//      public abstract class StatefulComponent : ComponentBase, ITempestHost, IDisposable
//      {
//          [Inject] protected IEventBus Bus { get; set; } = default!;
//
//          void ITempestHost.StateChanged(StateBase _) => _ = InvokeAsync(StateHasChanged);
//          void ITempestHost.Dispatch(Func<Task> work)
//              => _ = InvokeAsync(async () =>
//              {
//                  try { await work(); StateHasChanged(); }
//                  catch (Exception ex) { await DispatchExceptionAsync(ex); }
//              });
//
//          // OnInitialized → RegisterTempestHandlers(Bus); Dispose → unsubscribe the bus
//          // AND dispose owned states, cancelling in-flight commands. (Unchanged shape.)
//      }
//
//  WinUI (Tempest.WinUI) — the host is a ViewModel base, not a control:
//
//      public abstract class TempestObservable : ITempestHost, IDisposable
//      {
//          private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
//
//          void ITempestHost.StateChanged(StateBase _) { }        // PropertyChanged did the work
//          void ITempestHost.Dispatch(Func<Task> work)
//              => _dispatcher.TryEnqueue(async () =>
//              {
//                  try { await work(); }
//                  catch (Exception ex) { /* rethrow on UI thread → App.UnhandledException */ }
//              });
//      }
//
//      public partial class TodosViewModel : TempestObservable        // the DSL, verbatim
//      {
//          [Reactive] private string _search = "";
//          private partial Task OnSearchChanged(string value) => LoadTodosState.TryExecute();
//
//          [Command] private Task<TodoPage> LoadTodos(CancellationToken ct)
//              => Api.SearchAsync(_search, ct);
//      }
//
//      <TextBox Text="{x:Bind Vm.SearchState.Value, Mode=TwoWay}" />
//      <ProgressRing IsActive="{x:Bind Vm.LoadTodosState.IsLoading, Mode=OneWay}" />
//      <Button Content="Refresh" Command="{x:Bind Vm.LoadTodosState}" />
//
//  Testing (Tempest.Testing) — the payoff nobody asked for but everybody needs:
//
//      var host = new TestHost();                      // Dispatch runs inline, errors recorded
//      var vm = new TodosViewModel(host);
//      vm.SearchState.Value = "milk";                  // rings the hook, runs the command
//      await host.Idle();                              // drain dispatched work
//      Assert.False(vm.LoadTodosState.IsLoading);
//      Assert.Equal(1, host.Changes(vm.LoadTodosState, nameof(CommandStateBase.IsLoading)));
//
//  The entire reactivity model — ladder, hooks, latest-wins, supersession — becomes
//  assertable without a renderer, a dispatcher, or a browser. The states were never
//  UI objects; now the type system says so.
//
// ─────────────────────────────────────────────────────────────────────────────────────────
//  6. The generator split
// ─────────────────────────────────────────────────────────────────────────────────────────
//
//  One generator core (model extraction, naming, diagnostics TEM001–TEM008) — it already
//  works on symbols and text, not on Blazor. Per-platform, only the edges differ:
//
//      discovery   Blazor: attributed members in .cs + the razor-text path (@code blocks).
//                  WinUI:  attributed members in partial classes. The razor parser — the
//                          hairiest code in the project — is a Blazor-only concern and
//                          moves behind that boundary.
//      base check  TEM002 accepts any ITempestHost implementer, not a hardcoded pair —
//                  StatefulComponent, TempestObservable, or a user's own host.
//      emission    Identical: {Name}State twins, partial hook declarations, the
//                  RegisterTempestHandlers override. It emits against ITempestHost and
//                  core state types only.
//
//  The bus needs nothing. EventBus never referenced Blazor; [Event] handlers reach the
//  UI thread through Dispatch like everything else. Publish<T>() from a WinUI ViewModel
//  and a Blazor component interoperate today, by accident of good layering — this split
//  just makes the accident load-bearing.
//
// ─────────────────────────────────────────────────────────────────────────────────────────
//  7. Costs, honestly
// ─────────────────────────────────────────────────────────────────────────────────────────
//
//  - Blazor pays for PropertyChanged events nobody consumes. An allocation-free raise to
//    an empty handler list — real but negligible next to a render. If it ever shows up in
//    a profile, hosts could opt out via a capability flag; don't build that until then.
//  - Fine-grained notification invites computed-property drift: every property depending
//    on a mutated one must be Notify'd by hand (IsError, IsDirty above). Blazor's re-render
//    hid this class of bug; XAML will find it. The discipline: any computed property on a
//    state twin gets a Notify in the same setter, and the (future) test host asserts it.
//  - ICommand on the base class widens the visible API on Blazor, where it's dead weight.
//    Priced in for XAML binding without an adapter type; explicit interface implementation
//    keeps it out of IntelliSense.
//  - `ITempestComponent` → `ITempestHost` plus constructor-shape changes on the states is
//    a breaking change for anyone who newed a state by hand. Beta is the time.
//  - Two host adapters and a shared core to keep honest — the "mirror" comment on
//    StatefulLayoutComponent already documents this tax at N=2 inside one file; the split
//    at least makes each mirror five lines against one interface.
//  - WinUI's generator story needs partial-property-era polish (MVVM Toolkit overlap is
//    real: [ObservableProperty] users will ask which attribute wins). The answer — Tempest
//    owns members that carry lifecycle (commands, hooks, dirty-tracking); the Toolkit owns
//    plain notification — deserves its own concept file when Tempest.WinUI is real.
//
//  What is deliberately NOT in the core, on any platform: schedulers, debouncing, deep
//  observation, dependency graphs, cascade tracking. A value changed, so your reaction
//  ran, on the host's thread. That was the whole idea in Blazor; it is the whole idea
//  everywhere. State lives in the component. Nobody reads it but you.
