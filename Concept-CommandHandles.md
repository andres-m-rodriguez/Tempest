# Proposal: command handles

One generated property per command instead of three flat members. Everything about a command lives behind one name:

```
LoadCart.IsLoading    LoadCart.Error    LoadCart.Command()    LoadCart.Try()
```

## The naming rule (and why it exists)

C# forbids a property and a method with the same name in one class — so the method itself
can't be called `LoadCart` if the handle is called `LoadCart`. The fix is the convention the
event handlers already use: **the method takes an `On` prefix, the handle gets the clean name.**

```csharp
[Command]
private async Task OnLoadCart()      // you write OnLoadCart…
    => _items = await Api.GetCartAsync();

// …the generator emits the handle:
// public CommandHandle LoadCart { get; }
```

`[Command]` methods without the `On` prefix become a compile error (`TEM004`), same spirit as
the existing event-shape rule. One convention across commands and events:

| You write | Generator emits |
|---|---|
| `[Command] Task OnLoadCart()` | `LoadCart` handle |
| `[Event] void OnClearCart(ClearCart e)` | subscription for `ClearCart` |
| `[Event, Command] Task OnApplyCoupon(ApplyCoupon e)` | `ApplyCoupon` handle + subscription |

## The whole component, rewritten

```razor
@* CartComponent.razor *@
@inherits StatefulComponent

@if (LoadCart.IsLoading)
{
    <Spinner />
}
else if (LoadCart.Error is { } err)
{
    <ErrorBox Message="@err.Message" OnRetry="LoadCart.Try" />
}
else
{
    <ul>
        @foreach (var item in _items)
        {
            <li>@item.Name — @item.Price</li>
        }
    </ul>
    <button @onclick="LoadCart.Try">Refresh</button>
}

@code {
    // ── Internal state. Private fields, mutated freely, invisible outside. ──
    private List<CartItem> _items = [];

    // ── Contract. Plain nested records — the component's public doorbell panel. ──
    public record ClearCart();
    public record RemoveItem(string Sku);
    public record ApplyCoupon(string Code);

    // ── Commands: async work this component does. ──
    [Command]
    private async Task OnLoadCart()
    {
        _items = await Api.GetCartAsync();
    }

    // ── Events: what happens when someone rings the doorbell. ──
    [Event]
    private void OnClearCart(ClearCart _) => _items.Clear();

    [Event]
    private void OnRemoveItem(RemoveItem e) => _items.RemoveAll(i => i.Sku == e.Sku);

    // Commands with an argument get a typed handle: ApplyCoupon.Try(e)
    [Event, Command]
    private async Task OnApplyCoupon(ApplyCoupon e)
    {
        _items = await Api.ApplyCouponAsync(e.Code);
    }
}
```

## What a handle is

Two small classes in the library — the generator only news them up:

```csharp
public sealed class CommandHandle
{
    public bool IsLoading { get; }
    public Exception? Error { get; }

    public Task Command();   // runs the lifecycle, exceptions propagate
    public Task Try();       // never throws — the exception lands in Error
    public void ClearError();
}

public sealed class CommandHandle<TArg>   // for [Event, Command]
{
    public bool IsLoading { get; }
    public Exception? Error { get; }

    public Task Command(TArg arg);
    public Task Try(TArg arg);
    public void ClearError();
}
```

Same lifecycle as today: `IsLoading` flips and re-renders on both edges, `Error` clears on the
next run, bus-triggered `[Event, Command]` handlers still run through `Try` so a publish can
never blow up in the publisher.

## What it looks like generated

```csharp
partial class CartComponent
{
    private CommandHandle? _loadCart;
    public CommandHandle LoadCart => _loadCart ??= new(this, OnLoadCart);

    private CommandHandle<ApplyCoupon>? _applyCoupon;
    public CommandHandle<ApplyCoupon> ApplyCoupon => _applyCoupon ??= new(this, OnApplyCoupon);

    protected override void RegisterTempestHandlers(IEventBus bus)
    {
        SubscribeEvent<ClearCart>(e => DispatchEvent(() => OnClearCart(e)));
        SubscribeEvent<RemoveItem>(e => DispatchEvent(() => OnRemoveItem(e)));
        SubscribeEvent<ApplyCoupon>(e => InvokeAsync(() => this.ApplyCoupon.Try(e)));
    }
}
```

## Why this is better than the flat members

- **One name, not a family of three.** `IsLoadingLoadCart` / `LoadCartError` /
  `TryLoadCartCommand` becomes `LoadCart.` and IntelliSense shows you everything the command has.
- **Handles are values you can pass.** A shared button component becomes possible:

  ```razor
  <CommandButton Handle="LoadCart" Label="Refresh" />
  @* renders spinner from Handle.IsLoading, error from Handle.Error, clicks Handle.Try *@
  ```
- **No generated-name guessing.** Today you have to remember the `IsLoading{Name}` spelling;
  here the only generated name is the command itself.

## Costs, honestly

- The `On` prefix is mandatory for `[Command]` methods — a breaking change to today's shape
  (`LoadCart()` → `OnLoadCart()`), enforced by `TEM004`.
- Markup churn for existing users: `IsLoadingLoadCart` → `LoadCart.IsLoading`, etc.
- One lazily-allocated handle object per command per component instance (negligible).

## Migration at a glance

| Today | With handles |
|---|---|
| `[Command] Task LoadCart()` | `[Command] Task OnLoadCart()` |
| `IsLoadingLoadCart` | `LoadCart.IsLoading` |
| `LoadCartError` | `LoadCart.Error` |
| `LoadCartCommand()` | `LoadCart.Command()` |
| `TryLoadCartCommand()` | `LoadCart.Try()` |
| `TryApplyCouponCommand(e)` | `ApplyCoupon.Try(e)` |

Bus publishing is untouched: `Bus.Publish<CartComponent.ClearCart>()` works exactly as before.
