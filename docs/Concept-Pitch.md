# Tempest — component-owned state for Blazor (and XAML next)

You write a normal component: plain fields, plain methods. Four attributes tell
Tempest what they *mean* — and a source generator builds all the state machinery
around them at compile time.

It's CQRS-ish: **commands** are the work your component does, **reactive fields**
are the state it owns, **events** are how the outside world reaches it. What you
add to the component decides which side it plays on — internal state, external
state, or both.

## One component, the whole idea

`ShoppingCart.razor`

```razor
@inherits StatefulComponent

<input @bind="QueryState.Value" placeholder="Search products…" />

@if (SearchState.IsLoading)
{
    <Spinner />
}
else if (SearchState.Error is { } error)
{
    <ErrorBanner Message="@error.Message" Retry="SearchState.Execute" />
}
else
{
    <ProductGrid Items="@SearchState.Result" />
}

<button @onclick="CheckoutState.Execute" disabled="@CheckoutState.IsLoading">
    Checkout (@ItemsState.Value.Count)
</button>

@code {
    // ── Internal state ─────────────────────────────────────────────
    // A [Reactive] field gets a generated {Name}State twin:
    // Value, IsDirty, Reset… writing Value re-renders. No StateHasChanged, ever.
    [Reactive] private string _query = "";
    [Reactive] private List<Product> _items = [];

    // Rings on every _query change. Chain work, debounce, validate.
    [OnChanged]
    private Task OnQueryChanged(string value) => SearchState.Execute();

    // ── Commands: the work (the "C" in CQRS-ish) ───────────────────
    // A [Command] gets a {Name}State twin: IsLoading, Error, Result,
    // Execute — plus latest-wins cancellation via the trailing token.
    // No loading flags, no try/catch, no torn UI states.
    [Command]
    private Task<List<Product>> Search(CancellationToken ct)
        => Api.SearchAsync(_query, ct);

    [Command]
    private async Task Checkout(CancellationToken ct)
    {
        await Api.CheckoutAsync(_items, ct);
        Bus.Publish(new OrderPlaced(_items.Count));   // tell the world
    }

    // ── External state: events ─────────────────────────────────────
    // The nested record IS the contract. Any component, anywhere in the
    // tree, publishes it — no cascading parameters, no parameter drilling.
    public sealed record ItemAdded(Product Product);

    [Event]
    private void OnItemAdded(ItemAdded e) => ItemsState.Value = [.. _items, e.Product];
}
```

And somewhere completely unrelated in the tree:

```razor
<button @onclick="() => Bus.Publish(new ShoppingCart.ItemAdded(Product))">
    Add to cart
</button>
```

That's the whole wiring. Subscription, unsubscription, marshalling back to the
renderer, re-render — all generated, all disposed with the component.

## What you never write again

- `bool _isLoading` + `try/catch` + `finally { StateHasChanged(); }` around every API call
- Debounce/cancellation plumbing — a new `Execute` cancels the in-flight one
- Event subscription lifecycles — `[Event]` + a record replaces the whole pub/sub ceremony
- Dirty tracking for forms — `QueryState.IsDirty`, `QueryState.Reset()` come free

## The twist: the same component works in XAML

The generated code never touches Blazor. It targets a tiny host contract, and
`StatefulComponent` is just the *Blazor* implementation of it. A WinUI/MAUI host
implements the same contract with `DispatcherQueue` instead of `InvokeAsync` —
and then:

- `SearchState` **is** an `ICommand` → `Command="{x:Bind SearchState}"`
- `QueryState` raises `INotifyPropertyChanged` → `Text="{x:Bind QueryState.Value, Mode=TwoWay}"`
- the same `record` + `[Event]` bus spans both worlds

One mental model — state the component owns, work the component does, events the
component listens to — and the UI framework becomes an implementation detail.
