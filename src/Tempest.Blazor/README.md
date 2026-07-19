# Tempest

**Component-owned state for Blazor.** Your component keeps its state. You write plain private
members; the generator emits one state twin per member — everything interesting lives behind
`{Name}State.`

No store. No reducers. No actions folder. Three attributes and a bus.

```csharp
// Program.cs
builder.Services.AddTempest();
```

```razor
@inherits StatefulComponent

@if (LoadCartState.IsLoading) { <p>Loading…</p> }
else
{
    <ul>@foreach (var item in _items) { <li>@item.Name</li> }</ul>
    <button @onclick="LoadCartState.TryExecute">Refresh</button>
}

@code {
    private List<CartItem> _items = [];

    public record ClearCart();          // the component's public doorbell panel

    [Command]                           // → LoadCartState : CommandState
    public async Task LoadCart()
        => _items = await Api.GetCartAsync();

    [Event]                             // rung from anywhere via the bus
    private void OnClearCart(ClearCart _) => _items.Clear();
}
```

Trigger from anywhere — no reference to the component needed:

```csharp
Bus.Publish<CartComponent.ClearCart>();
```

## What you get per `[Command]`

One generated property, `{Name}State`, holding the whole lifecycle:

| Member | Purpose |
|---|---|
| `IsLoading` | `true` while the method runs; re-renders on both edges |
| `IsError` / `Error` | last exception caught by `TryExecute()`, cleared on the next run |
| `Execute()` | runs the lifecycle, exceptions propagate |
| `TryExecute()` | never throws — the exception lands in `Error` |
| `ClearError()` | dismisses the error |
| `Result` / `HasResult` | on value-returning commands: the last successful value |

Every return type participates: `Task` / `ValueTask` / `void` generate a plain
`CommandState`; `Task<T>` / `ValueTask<T>` / plain `T` generate a `CommandState<T>` whose
`Result` holds the last successful value — render it directly instead of copying into a field:

```csharp
[Command]                                   // → LoadTodosState : CommandState<List<TodoItem>>
private Task<List<TodoItem>> LoadTodos(CancellationToken ct) => Api.GetTodosAsync(ct);

// markup: @foreach (var todo in LoadTodosState.Result ?? []) { ... }
```

Declare an optional trailing `CancellationToken` on the method for **latest-wins**
cancellation: re-executing while a run is in flight cancels it, and a stale result — success
or failure — is discarded instead of overwriting the newer one. Type into a search box faster
than the API answers and the response for "r" can never overwrite the one for "re".

```csharp
[Command]
private Task<TodoPage> LoadTodos(CancellationToken ct) => Api.SearchAsync(_search, ct);
```

`[Event, Command]` methods get an `EventCommandState<TEvent>` that takes the event record:
`ApplyCouponState.TryExecute(e)`. Bus-triggered runs always go through `TryExecute`, so a
publish can never blow up in the publisher.

## What you get per `[Reactive]`

Mark a private field and the generator emits its `{PascalCase}State` twin — a
`ReactiveState<T>` the markup binds and shared components can take as a parameter:

```razor
<input @bind="SearchTextState.Value" @bind:event="oninput" />
```

| Member | Purpose |
|---|---|
| `Value` | change-check → assign → re-render → ring the `On{Name}Changed` hook |
| `SetSilently(v)` | change-check → assign → re-render — no hook |
| `Initial` / `IsDirty` / `Reset()` | the field's starting value, drift check, and way back |

React to changes by implementing the partial hook — the generator declares it, you write the
body, and the C# compiler enforces the pairing (rename the field without the hook and the
build breaks):

```csharp
[Reactive]
private string _searchText = "";

private partial Task OnSearchTextChanged(string value)
{
    PageState.SetSilently(1);                    // adjust siblings without re-triggering
    return SearchUsersState.TryExecute();        // orchestration is C#, not configuration
}
```

The hook adapts to what you write: implement `partial void` for a synchronous reaction,
`partial Task` for async work — or implement nothing and the property just re-renders.

Tempest handles reactivity only — *when* values change (debouncing, on-input vs on-blur) is
the input layer's job. Put a debounced input in front; the state reacts to whatever arrives.

## Layouts too

Inherit `StatefulLayoutComponent` — the `LayoutComponentBase` flavor of `StatefulComponent`
with identical `[Command]`/`[Reactive]`/`[Event]` support. The classic
global-search-in-the-header becomes a `Task<T>` command with latest-wins cancellation for
free, instead of a hand-rolled stale-response guard.

## Guardrails

Misuse is caught at compile time, mostly by the C# compiler itself: an orphaned or misspelled
hook is a build error (`CS8795`), and the generator adds diagnostics for handler shape
(`TEM001`), missing `StatefulComponent` base (`TEM002`), command parameters (`TEM003`),
invalid `[Reactive]` fields (`TEM007`) and non-partial hooks (`TEM008`).

State lives in the component. Nobody reads it but you.
