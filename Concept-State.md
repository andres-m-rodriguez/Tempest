# Proposal: member state

One principle, applied twice: **you write a plain private member; the generator emits its
state class.** Commands get a `CommandState`. Reactive properties get a `ReactiveState<T>`.
Same suffix, same shape, same discoverability — type the member's name plus `State.` and
IntelliSense shows everything it can do.

| You write | Generator emits |
|---|---|
| `[Command] Task LoadUsers()` | `LoadUsersState` — `IsLoading` / `IsError` / `Execute()` / `TryExecute()` |
| `[Reactive] string _searchText` | `SearchTextState` — `Value` / `SetSilently()` / `IsDirty` / `Reset()` |
| *(optional)* `partial Task OnSearchTextChanged(string value)` | the change hook, wired into `SearchTextState.Value`'s setter |
| `[Event] void OnClearUsers(ClearUsers e)` | bus subscription (unchanged from today) |

Collisions are impossible by construction — the `State` suffix never collides with the method,
the `PascalCase` twin never collides with the `_camelCase` field — so there is no naming rule
to enforce and no breaking change to existing code. Both halves are additive.

---

# Part 1 — Commands: `{Name}State`

Today each `[Command]` sprays four flat members into the component (`IsLoadingLoadUsers`,
`LoadUsersError`, `LoadUsersCommand()`, `TryLoadUsersCommand()`). Instead: one generated
state property per command, holding everything.

```csharp
[Command]
public async Task LoadUsers()        // you write exactly what you write today…
    => _users = await Api.GetUsersAsync();

// …the generator emits the state property:
// public CommandState LoadUsersState { get; }
```

```
LoadUsersState.IsLoading    LoadUsersState.IsError    LoadUsersState.Execute()    LoadUsersState.TryExecute()
```

The `State` suffix is the collision fix: C# forbids a property and method sharing a name, and
`LoadUsersState` never collides with `LoadUsers`. Your method keeps its clean name and stays
a plain method call from C#.

## The command state classes

Three small classes in the library — the generator picks one from the method's return type:

| You return | Generator news up |
|---|---|
| `Task`, `ValueTask`, `void` | `CommandState` |
| `Task<T>`, `ValueTask<T>`, plain `T` | `CommandState<T>` — the value lands in `Result` |
| anything, on `[Event, Command]` | `EventCommandState<TEvent>` (results dropped — the publisher can't receive one) |

```csharp
public sealed class CommandState
{
    public bool IsLoading { get; }
    public bool IsError => Error is not null;
    public Exception? Error { get; }

    public Task Execute();      // runs the lifecycle, exceptions propagate
    public Task TryExecute();   // never throws — the exception lands in Error
    public void ClearError();
}

public sealed class CommandState<TResult>   // for commands that return a value
{
    // …same lifecycle surface, plus:
    public TResult? Result { get; }     // last successful value; stays visible while reloading
    public bool HasResult { get; }      // disambiguates a default-valued Result

    public Task<TResult?> Execute();    // returns the value too (default if superseded)
    public Task<TResult?> TryExecute();
}

public sealed class EventCommandState<TEvent>   // for [Event, Command]
{
    // …same lifecycle surface, with the event as the argument:
    public Task Execute(TEvent arg);
    public Task TryExecute(TEvent arg);
}
```

`Result` turns commands into the data source: a component can render
`LoadTodosState.Result` directly instead of copying into a field. A superseded run
(latest-wins) never commits its result; sync and `ValueTask` methods are adapted by the
generator (`Task.FromResult` / `.AsTask()`), so every return type participates in the
lifecycle — nothing runs unobserved.

Lifecycle: `IsLoading` flips and re-renders on both edges, `Error` clears on the next run,
bus-triggered `[Event, Command]` handlers run through `TryExecute` so a publish can never
blow up in the publisher.

## Latest-wins cancellation

Built into `CommandState`, because Part 2 makes it mandatory (rapid input changes mean
overlapping executes) and it's useful alone (double-clicked refresh buttons):

- A `[Command]` method may declare a trailing `CancellationToken` parameter; the state owns
  the token source.
- `Execute`/`TryExecute` while `IsLoading` **cancels the previous run and starts a new one**.
  The superseded run's result — success or exception — is discarded: it never touches
  `IsLoading`, `Error`, or triggers a render. The answer for `"an"` can never overwrite the
  answer for `"ann"`.
- `OperationCanceledException` from a superseded run is swallowed, not surfaced as `Error`.

---

# Part 2 — Reactive properties: `{Name}State` again

Commands cover *work the component does*. This half covers *state that changes and must
cause something* — a search box that re-queries, a page index that refetches. Tempest handles
**reactivity only**: a value changed, so your reaction runs. *When* values change —
debouncing, throttling, on-input vs on-blur — is the input layer's job; put a debounced input
component in front if you want fewer changes.

```csharp
[Reactive]
private string _searchText = "";     // you write the field…

// …the generator emits, in the other half of the partial class:
//   public ReactiveState<string> SearchTextState { get; }    (wraps the field)
//   private partial Task OnSearchTextChanged(string value);          (hook — only if you implement it)

private partial Task OnSearchTextChanged(string value)               // …and you implement the hook.
{
    ...
}
```

## The reactive state class

One small class in the library, symmetric with `CommandState`:

```csharp
public sealed class ReactiveState<T>
{
    public T Value { get; set; }        // set: change-check → re-render → On{Name}Changed hook
    public void SetSilently(T value);   // set: change-check → re-render — hook NOT called
    public T Initial { get; }           // the field's initializer value
    public bool IsDirty { get; }        // !Equals(Value, Initial)
    public void Reset();                // SetSilently(Initial)
}
```

The state wraps the field — reads always see the current value, and the component can still
touch `_searchText` directly (see the write ladder below). Markup binds `Value`:

```razor
<DebouncedInput @bind-Value="SearchTextState.Value" Delay="300" />
```

`Value`'s setter, in order:

1. **Change check** — `EqualityComparer<T>.Default`. Same value is a no-op: no render, no
   hook, no infinite loops when a hook writes a property it reacts to.
2. **Assign + re-render** — the field is updated and `StateHasChanged()` runs immediately.
   Reactivity never delays what the user sees; async hooks render again on their own
   (commands already re-render on both edges of `IsLoading`).
3. **Hook** — `On{Name}Changed(value)` is invoked through the component's sync context with the new
   value. A `Task` hook is dispatched fire-and-safe: an exception thrown by the hook body
   surfaces to Blazor's error handling exactly like a throwing event handler, never lost to
   an unobserved task.

## The write ladder

Making the state an object turns the old field-vs-property subtlety into explicit API —
three deliberate rungs instead of one invisible convention:

| Write | Renders? | Rings hook? | Use when |
|---|---|---|---|
| `SearchTextState.Value = x` | yes | yes | the normal case: bindings, user intent |
| `SearchTextState.SetSilently(x)` | yes | no | adjusting state from inside a reaction |
| `_searchText = x` | no | no | bookkeeping mid-flow; a later render will pick it up |

The call site now *says* which semantics it wants — `SetSilently` reads as loudly as the
thing it avoids.

## The hook lives on the component, not the class

`ReactiveState<T>` deliberately has no overridable `OnChanged` — a subclass's override can't
see the component, and a reaction's whole job is to touch its siblings:

```csharp
// the subclass version, honestly rendered:
private sealed class SearchTextReaction(UserSearchComponent c) : ReactiveState<string>
{
    protected override Task OnChanged(string value)
    {
        c.PageState.SetSilently(1);               // reaching back in, member by member
        return c.SearchUsersState.TryExecute();
    }
}
```

The partial method is that same "declared by the framework, overwritten by you" contract,
minus the plumbing — it lives inside the component, so `PageState` and `SearchUsersState`
are just there. (Precedent: MVVM Toolkit's `[ObservableProperty]` generates exactly this
`On{Name}Changed` partial-hook shape.) The generator adapts to what you write:

| You implement | `Value`'s setter does |
|---|---|
| *(nothing)* | re-renders on change, nothing else |
| `partial void OnSearchTextChanged(string value)` | calls it synchronously before the render settles |
| `partial Task OnSearchTextChanged(string value)` | dispatches it; exceptions surface like event handlers |

## Multiple reactions? That's just the method body.

No trigger metadata, no `Executes = ...` lists, no cascade-deduplication rules —
**orchestration is C#, not configuration:**

```csharp
private partial Task OnSearchTextChanged(string value)
{
    PageState.SetSilently(1);                    // new search starts at page 1 — no OnPageChanged
    return Task.WhenAll(                         // parallel? WhenAll.
        SearchUsersState.TryExecute(),
        LoadSuggestionsState.TryExecute());
}
```

Sequential? Two awaits. Conditional? An `if`. One thing? One call. The framework's job ended
when it called your method. `SetSilently` is what keeps fan-out clean: the page reset renders
but doesn't ring `OnPageChanged`, so the search executes once, not twice.

## The wiring is compiler-checked

The `partial` keyword makes the magic safe — the pairing is enforced in both directions by
the C# compiler, not by convention:

- **Rename the field, forget the hook** → your `OnSearchTextChanged` implementation has no defining
  declaration anymore → `CS8795`, the build breaks and points at the orphan.
- **Typo the hook name** → same error, at the typo.
- **Wrong signature** — parameter or return type not matching the field → the generator
  declares nothing, the compiler flags your implementation.

---

# The whole component

Everything together — events, commands, reactive properties:

```razor
@* UserSearchComponent.razor *@
@inherits StatefulComponent

@* Debounce (if wanted) lives here, in the input — Tempest never sees keystrokes, only value changes. *@
<DebouncedInput @bind-Value="SearchTextState.Value" Delay="300" Placeholder="Search users…" />

@if (_suggestions is { Count: > 0 })
{
    <SuggestionList Items="_suggestions" />
}

@if (SearchUsersState.IsLoading)
{
    <Spinner />
}
else if (SearchUsersState.IsError)
{
    <ErrorBox Message="@SearchUsersState.Error!.Message" OnRetry="SearchUsersState.TryExecute" />
}
else
{
    <ul>
        @foreach (var user in _users)
        {
            <li>@user.Name</li>
        }
    </ul>
    <Pager Page="PageState.Value"
           OnNext="() => PageState.Value++"
           OnPrev="() => PageState.Value--" />
}

@code {
    // ── Internal state. Private fields — [Reactive] ones get a state twin. ──
    private List<User> _users = [];
    private List<string> _suggestions = [];

    [Reactive]
    private string _searchText = "";

    [Reactive]
    private int _page = 1;

    // ── Contract. Plain nested records — the component's public doorbell panel. ──
    public record ClearUsers();
    public record BanUser(string Id);

    // ── Reactions. Declared by the generator, overwritten here. ──
    private partial Task OnSearchTextChanged(string value)
    {
        PageState.SetSilently(1);
        return Task.WhenAll(
            SearchUsersState.TryExecute(),
            LoadSuggestionsState.TryExecute());
    }

    private partial Task OnPageChanged(int value) => SearchUsersState.TryExecute();

    // ── Commands. Reactions and markup call them through their states. ──
    [Command]
    private async Task SearchUsers(CancellationToken ct)
    {
        _users = await Api.SearchUsersAsync(_searchText, _page, ct);
    }

    [Command]
    private async Task LoadSuggestions(CancellationToken ct)
    {
        _suggestions = await Api.GetSuggestionsAsync(_searchText, ct);
    }

    // ── Events: what happens when someone rings the doorbell. ──
    [Event]
    private void OnClearUsers(ClearUsers _) => _users.Clear();

    // Commands with an argument get a typed state: BanUserState.TryExecute(e)
    [Event, Command]
    private async Task OnBanUser(BanUser e)
    {
        _users = await Api.BanUserAsync(e.Id);
    }
}
```

The debounced input delivers one value change. `SearchTextState.Value`'s setter re-renders
and calls `OnSearchTextChanged`: page silently reset, both commands running concurrently, each with
its own `IsLoading`, `Error`, and cancellation. Pager clicks set `PageState.Value`, which
rings `OnPageChanged`. Bus publishing is untouched:
`Bus.Publish<UserSearchComponent.ClearUsers>()` works as before.

## What it looks like generated

```csharp
partial class UserSearchComponent
{
    // ── Part 1: command states ──
    private CommandState? _searchUsersState;
    public CommandState SearchUsersState => _searchUsersState ??= new(this, SearchUsers);

    private CommandState? _loadSuggestionsState;
    public CommandState LoadSuggestionsState => _loadSuggestionsState ??= new(this, LoadSuggestions);

    private EventCommandState<BanUser>? _banUserState;
    public EventCommandState<BanUser> BanUserState => _banUserState ??= new(this, OnBanUser);

    // ── Part 2: reactive states + hook declarations ──
    private partial Task OnSearchTextChanged(string value);
    private partial Task OnPageChanged(int value);

    private ReactiveState<string>? _searchTextState;
    public ReactiveState<string> SearchTextState => _searchTextState ??= new(
        this,
        getter: () => _searchText,
        setter: v => _searchText = v,
        initial: "",
        hook: OnSearchTextChanged);

    private ReactiveState<int>? _pageState;
    public ReactiveState<int> PageState => _pageState ??= new(
        this,
        getter: () => _page,
        setter: v => _page = v,
        initial: 1,
        hook: OnPageChanged);          // hook: null when no On{Name}Changed is implemented

    // ── Events, unchanged ──
    protected override void RegisterTempestHandlers(IEventBus bus)
    {
        SubscribeEvent<ClearUsers>(e => DispatchEvent(() => OnClearUsers(e)));
        SubscribeEvent<BanUser>(e => InvokeAsync(() => this.BanUserState.TryExecute(e)));
    }
}
```

Both state kinds are lazily allocated, wrap members you wrote, and dispatch through the
component's sync context. No timers, no scheduler, no cascade tracker anywhere in the library.

## States are values you can pass

The payoff of "everything is a state class": shared components take states as parameters and
work with any member of the right kind.

```razor
<CommandButton State="LoadSuggestionsState" Label="Refresh suggestions" />
@* renders spinner from State.IsLoading, error from State.Error, clicks State.TryExecute *@

<DebouncedInput State="SearchTextState" Delay="300" />
@* reads State.Value, writes State.Value after the debounce window *@

<ResetLink State="SearchTextState" />
@* visible when State.IsDirty, click calls State.Reset() *@
```

---

# Diagnostics

Most misuse is caught by the C# compiler for free (`CS8795` on orphaned hooks, signature
mismatches). The generator adds:

- A member already exists with a name the generator needs (`{Name}State`) → compile error
  naming the collision.
- `[Reactive]` on a non-private or static field → `TEM007`: the field is the internal half of
  the pair; the generated state is the public one.
- A non-partial method named `On{Property}Changed` matching a `[Reactive]` field → `TEM008` warning:
  "did you mean `partial`?" — the likeliest authoring mistake, caught at the site.

# Why this is better than doing it by hand

- **The manual version is the bug.** Hand-written today: `IsLoading` flags and try/catch
  boilerplate per command, a property wrapper that remembers `StateHasChanged()`, a
  `CancellationTokenSource` field, and an out-of-order-response bug you find in production.
  Here: two attributes, one partial method.
- **One suffix to know.** Anything interesting about any member is behind `{Name}State.` —
  commands and values alike. IntelliSense is the documentation.
- **States are passable.** `CommandButton`, `DebouncedInput`, `ResetLink` — shared components
  compose against `CommandState` and `ReactiveState<T>`, not against copy-pasted parameter
  triples.
- **Reactions are readable top to bottom.** The answer to "what happens when search changes?"
  is one method, in plain C#, and `partial` on it tells you a generator calls it. The write
  ladder (`Value` / `SetSilently` / field) says at every call site which semantics it wants.
- **The magic stops at the wiring — and is compiler-verified.** The generator emits state
  objects and hook declarations; every way the pairing can drift is a build error. Everything
  past the wiring is code you wrote and can step through in a debugger.
- **One family.** `[Command] LoadUsers` → `LoadUsersState`. `[Reactive] _searchText` →
  `SearchTextState` + `OnSearchTextChanged`. `[Event] OnClearUsers(ClearUsers e)` → subscription.
  *You write the plain member; the generator wires the lifecycle around its name.*

# Costs, honestly

- **`.Value` at every binding site.** `@bind-Value="SearchTextState.Value"` is a word longer
  than a plain property. The symmetry and passability pay for it; opinions may differ.
- **Markup churn for existing users** (commands): `IsLoadingLoadUsers` → `LoadUsersState.IsLoading`,
  etc. — confined to markup; no method renames. `[Reactive]` is purely additive.
- **The raw member still bypasses the lifecycle.** `LoadUsers()` direct skips
  `IsLoading`/`Error`; `_searchText = x` skips render and hook. The bottom rung of the write
  ladder is a feature, but a *wrong-rung* pick is a logic bug the compiler can't see; for
  commands an analyzer hint ("did you mean `LoadUsersState.Execute()`?") could cover it.
- **Change detection is equality, not deep observation.** Mutating a `[Reactive] List<T>` in
  place triggers nothing — reassign it, or don't make collections reactive.
- **Reaction cycles are possible.** `OnA` writes `B.Value`, `OnB` writes `A.Value` —
  terminates only because of the equality check; oscillating values would loop forever.
- **Every value change runs the hook.** With a plain `@bind:event="oninput"` input, that's
  one (cancelled) API call per keystroke. Latest-wins keeps it *correct*; keeping it *cheap*
  is the input component's job — by design, Tempest won't debounce for you.
- **Partial methods are unfamiliar to some.** "The generator declares it, I implement it" is
  a mental model users may meet here for the first time; `CS8795` at least points at the
  orphan half.
- One lazily-allocated state object per command and reactive property per component instance
  (negligible).

# Migration at a glance

| Today | With member state |
|---|---|
| `[Command] Task LoadUsers()` | unchanged |
| `IsLoadingLoadUsers` | `LoadUsersState.IsLoading` |
| `LoadUsersError` | `LoadUsersState.Error` (plus `LoadUsersState.IsError`) |
| `LoadUsersCommand()` | `LoadUsersState.Execute()` |
| `TryLoadUsersCommand()` | `LoadUsersState.TryExecute()` |
| `TryBanUserCommand(e)` | `BanUserState.TryExecute(e)` |
| property wrapper calling `StateHasChanged()` | `[Reactive] private string _searchText` |
| binding the hand-written property | `@bind-Value="SearchTextState.Value"` |
| orchestration sprinkled in the setter | `partial Task OnSearchTextChanged(string value)` body |
| `CancellationTokenSource` juggling | `CancellationToken ct` parameter on the command |
| debounce timer in the component | a debounced input component in the markup |

Bus publishing and `[Event]` are untouched throughout.
