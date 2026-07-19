// ═════════════════════════════════════════════════════════════════════════════════════════
//  Concept: the reactivity core — worked example
//  Companion to Concept-Reactivity.cs. One ViewModel, written once in the DSL; every
//  platform difference lives in the container and the markup, never in the members.
// ═════════════════════════════════════════════════════════════════════════════════════════
//
//  The scenario is the demo's todo list, upgraded to exercise the whole surface:
//
//      search box     → [Reactive] + hook + latest-wins reload  (type fast, never stale)
//      page index     → [Reactive] + hook; search resets it SILENTLY (no double reload)
//      new-todo title → [Reactive] with no hook (binding + IsDirty/Reset, nothing else)
//      load           → [Command] returning Task<TodoPage>; Result IS the data source
//      add            → [Command] returning Task; chains Reset + reload in plain C#
//      import         → [Event, Command] doorbell with payload, rung from anywhere
//      invalidated    → [Event] doorbell: another screen saved a todo, this list refreshes
//
// ═════════════════════════════════════════════════════════════════════════════════════════

namespace Tempest.Example;

// ─────────────────────────────────────────────────────────────────────────────────────────
//  0. The app's own types — nothing Tempest about them
// ─────────────────────────────────────────────────────────────────────────────────────────

public record TodoItem(int Id, string Title, bool Done);

public record TodoPage(IReadOnlyList<TodoItem> Items, int OpenCount);

public interface ITodoApi
{
    Task<TodoPage> SearchAsync(string search, int page, CancellationToken ct);
    Task CreateAsync(string title, CancellationToken ct);
    Task ImportAsync(string url, CancellationToken ct);
}

// ─────────────────────────────────────────────────────────────────────────────────────────
//  1. What you write — the DSL, once
// ─────────────────────────────────────────────────────────────────────────────────────────
//
//  The container is the only platform decision in the file:
//
//      WinUI   → a ViewModel on TempestObservable (Tempest.WinUI), shown here.
//      Blazor  → the SAME member text inside a StatefulComponent @code block (§4).
//
//  One deliberate difference from the Blazor habit: members here are `internal`, not
//  `private`. Generated twins match the accessibility of the member they wrap (beta.3
//  rule), and a XAML view is a *different class* that needs to see them — while {x:Bind}
//  compiles into the same assembly, so `internal` is exactly enough. In a Blazor @code
//  block the markup compiles into the same class, so `private` suffices there. That one
//  keyword is the entire porting cost between the two containers.

public partial class TodosViewModel(ITodoApi api) : TempestObservable
{
    // ── Reactive values: you write the field, the generator emits its {PascalCase}State twin. ──

    [Reactive] string _search = "";
    [Reactive] int _page = 1;
    [Reactive] string _newTitle = "";

    public record TodosInvalidated();
    public record ImportTodos(string Url);

    private partial Task OnSearchChanged(string value)
    {
        PageState.SetSilently(1);
        return LoadTodosState.TryExecute();
    }

    private partial Task OnPageChanged(int value) => LoadTodosState.TryExecute();

    // _newTitle has no hook — legal by design. Its twin still binds, notifies, and
    // tracks IsDirty; implementing the partial later is purely additive.

    // ── Commands: the method is the work; the twin is the lifecycle. ──

    [Command]                                // → CommandState<TodoPage> LoadTodosState.
    internal Task<TodoPage> LoadTodos(CancellationToken ct)
        => api.SearchAsync(_search, _page, ct);
    // Trailing ct = latest-wins: typing "re" cancels the in-flight "r" search, and a
    // stale response — success OR failure — can never overwrite the newer one.
    // LoadTodosState.Result is the page the UI renders; no copy into a field.

    [Command]                                // → CommandState AddTodoState.
    internal async Task AddTodo(CancellationToken ct)
    {
        await api.CreateAsync(_newTitle.Trim(), ct);
        NewTitleState.Reset();               // back to Initial (""), silently
        await LoadTodosState.TryExecute();   // sequencing is just... await
    }

    // ── Events: what happens when someone rings the doorbell. ──

    [Event]                                  // Bus.Publish<TodosViewModel.TodosInvalidated>()
    private void OnTodosInvalidated(TodosInvalidated _)
        => _ = LoadTodosState.TryExecute();  // fire-and-safe: errors land in .Error, not the publisher

    [Event, Command]                         // → EventCommandState<ImportTodos> ImportTodosState:
    private async Task OnImportTodos(ImportTodos e, CancellationToken ct)
    {                                        // a doorbell WITH a lifecycle — IsLoading/Error
        await api.ImportAsync(e.Url, ct);    // are visible here even though the trigger came
        await LoadTodosState.TryExecute();   // from anywhere on the bus.
    }

    // ── Computed state: plain C# over the twins — no attribute, no registration. ──
    // Blazor reads it fresh every render. XAML binds its inputs instead
    // (x:Bind AddTodoState.IsLoading / NewTitleState.Value) — see the drift note in
    // Concept-Reactivity.cs §7: the core does not track derived-value dependencies.

    internal bool CanAdd => !AddTodoState.IsLoading && _newTitle.Trim().Length > 0;
}

// ─────────────────────────────────────────────────────────────────────────────────────────
//  2. What the generator emits — you never write this half
// ─────────────────────────────────────────────────────────────────────────────────────────
//
//  Shown in full once so the whole mechanism is on the table: lazily-allocated twins
//  wrapping your members, the declaring halves of your hooks (this is what makes a typo'd
//  or orphaned hook a compile error), and the bus wiring. Note what is ABSENT: no Blazor,
//  no WinUI, no dispatcher — everything platform-shaped reaches the states through the
//  ITempestHost the container implements.

partial class TodosViewModel
{
    // Declaring halves — the compiler now enforces the pairing with §1's implementations.
    private partial Task OnSearchChanged(string value);
    private partial Task OnPageChanged(int value);

    private ReactiveState<string>? __searchState;
    internal ReactiveState<string> SearchState => __searchState ??= new(
        this, () => _search, __v => _search = __v, "", __v => OnSearchChanged(__v));

    private ReactiveState<int>? __pageState;
    internal ReactiveState<int> PageState => __pageState ??= new(
        this, () => _page, __v => _page = __v, 1, __v => OnPageChanged(__v));

    private ReactiveState<string>? __newTitleState;
    internal ReactiveState<string> NewTitleState => __newTitleState ??= new(
        this, () => _newTitle, __v => _newTitle = __v, "", null);       // no hook: null

    private CommandState<TodoPage>? __loadTodosState;
    internal CommandState<TodoPage> LoadTodosState => __loadTodosState ??= new(this, LoadTodos);

    private CommandState? __addTodoState;
    internal CommandState AddTodoState => __addTodoState ??= new(this, AddTodo);

    private EventCommandState<ImportTodos>? __importTodosState;
    private EventCommandState<ImportTodos> ImportTodosState => __importTodosState ??= new(this, OnImportTodos);

    protected override void RegisterTempestHandlers(IEventBus bus)
    {
        // Bus deliveries arrive through Dispatch — the host's thread, the host's error
        // channel. [Event, Command] routes through TryExecute so a publish can't blow up
        // in the publisher; the failure is visible on ImportTodosState.Error instead.
        SubscribeEvent<TodosInvalidated>(e => Dispatch(() => { OnTodosInvalidated(e); return Task.CompletedTask; }));
        SubscribeEvent<ImportTodos>(e => Dispatch(() => ImportTodosState.TryExecute(e)));

        // Touch each reactive twin so Initial captures the field's initializer value.
        _ = SearchState; _ = PageState; _ = NewTitleState;
    }
}

// ─────────────────────────────────────────────────────────────────────────────────────────
//  3. The markup dialects — the ONLY thing each platform writes differently
// ─────────────────────────────────────────────────────────────────────────────────────────
//
//  WinUI (XAML). {x:Bind} consumes the PropertyChanged half of the signal; the Button
//  binds the command state ITSELF (CommandStateBase implements BCL ICommand:
//  auto-disables while IsLoading, click routes through TryExecute):
//
//      <TextBox Text="{x:Bind Vm.SearchState.Value, Mode=TwoWay,
//                             UpdateSourceTrigger=PropertyChanged}" />
//
//      <ProgressRing IsActive="{x:Bind Vm.LoadTodosState.IsLoading, Mode=OneWay}" />
//      <InfoBar Severity="Error"
//               IsOpen="{x:Bind Vm.LoadTodosState.IsError, Mode=OneWay}"
//               Message="{x:Bind Vm.LoadTodosState.Error.Message, Mode=OneWay}" />
//
//      <ItemsRepeater ItemsSource="{x:Bind Vm.LoadTodosState.Result.Items, Mode=OneWay}" />
//
//      <TextBox Text="{x:Bind Vm.NewTitleState.Value, Mode=TwoWay}" />
//      <Button Content="Add" Command="{x:Bind Vm.AddTodoState}" />
//
//  Blazor (razor). The renderer consumes the StateChanged half; markup reads the same
//  twins as plain properties and re-renders on every signal:
//
//      <input @bind="SearchState.Value" @bind:event="oninput" />
//
//      @if (LoadTodosState.IsLoading && !LoadTodosState.HasResult) { <Spinner /> }
//      else if (LoadTodosState.Error is { } err)
//      {
//          <ErrorBox Message="@err.Message" OnRetry="LoadTodosState.TryExecute" />
//      }
//      else
//      {
//          <ul class="@(LoadTodosState.IsLoading ? "opacity-50" : "")">
//              @foreach (var todo in LoadTodosState.Result?.Items ?? []) { <li>@todo.Title</li> }
//          </ul>
//      }
//
//      <input @bind="NewTitleState.Value" @bind:event="oninput" />
//      <button disabled="@(!CanAdd)" @onclick="AddTodoState.TryExecute">Add</button>
//
//  Same twins, same names, same semantics. The stale-search guarantee, the doorbells,
//  the write ladder — none of it re-implemented per platform.
//
// ─────────────────────────────────────────────────────────────────────────────────────────
//  4. The same members in the Blazor container — for the record
// ─────────────────────────────────────────────────────────────────────────────────────────
//
//      @page "/todos"
//      @inherits StatefulComponent
//      @inject ITodoApi Api
//
//      @* …the razor markup from §3… *@
//
//      @code {
//          [Reactive] private string _search = "";
//          [Reactive] private int _page = 1;
//          [Reactive] private string _newTitle = "";
//
//          public record TodosInvalidated();
//          public record ImportTodos(string Url);
//
//          private partial Task OnSearchChanged(string value)
//          {
//              PageState.SetSilently(1);
//              return LoadTodosState.TryExecute();
//          }
//          private partial Task OnPageChanged(int value) => LoadTodosState.TryExecute();
//
//          [Command] private Task<TodoPage> LoadTodos(CancellationToken ct) => Api.SearchAsync(_search, _page, ct);
//          [Command] private async Task AddTodo(CancellationToken ct) { /* §1, verbatim */ }
//
//          [Event] private void OnTodosInvalidated(TodosInvalidated _) => _ = LoadTodosState.TryExecute();
//          [Event, Command] private async Task OnImportTodos(ImportTodos e, CancellationToken ct) { /* §1, verbatim */ }
//
//          protected override Task OnInitializedAsync() => LoadTodosState.TryExecute();
//      }
//
//  Diff against §1: `internal` → `private`, `api` → injected `Api`, plus the component
//  lifecycle line. The doorbells interoperate across platforms because the bus never knew
//  about either: Bus.Publish(new TodosViewModel.ImportTodos(url)) from a WinUI settings
//  pane or a Blazor admin page rings the same handler.
//
// ─────────────────────────────────────────────────────────────────────────────────────────
//  5. The headless test — the payoff of the host boundary
// ─────────────────────────────────────────────────────────────────────────────────────────
//
//  TestHost (Tempest.Testing) runs Dispatch inline and records signals. No renderer, no
//  dispatcher, no browser — the reactivity model itself under assertion:
//
//      [Fact]
//      public async Task Typing_resets_page_and_reloads_once()
//      {
//          var host = new TestHost();
//          var api = new FakeTodoApi();
//          var vm = new TodosViewModel(api) { Host = host };
//
//          vm.PageState.Value = 3;                       // user had paged ahead
//          api.ResetCallLog();
//
//          vm.SearchState.Value = "milk";                // rings OnSearchChanged
//          await host.Idle();                            // drain dispatched work
//
//          Assert.Equal(1, vm.PageState.Value);          // silently reset…
//          Assert.Single(api.Searches);                  // …so exactly ONE reload ran
//          Assert.Equal(("milk", 1), api.Searches[0]);   // with the new search on page 1
//      }
//
//      [Fact]
//      public async Task Stale_search_never_overwrites_newer_one()
//      {
//          var host = new TestHost();
//          var api = new FakeTodoApi { Hold = true };    // responses await manual release
//          var vm = new TodosViewModel(api) { Host = host };
//
//          vm.SearchState.Value = "r";                   // run 1 in flight
//          vm.SearchState.Value = "re";                  // run 2 supersedes it
//          api.Release("re", pageWith("milk, rye"));     // NEWER answer lands first
//          api.Release("r",  pageWith("rice"));          // stale answer arrives late
//          await host.Idle();
//
//          Assert.Equal("milk, rye", vm.LoadTodosState.Result!.Items[0].Title);
//          Assert.False(vm.LoadTodosState.IsLoading);    // the stale run touched nothing
//      }
//
//  (`{ Host = host }` marks an open decision, honestly: how a host is attached — ctor
//  parameter, init property, or ambient from the container base — is the disposal/
//  ownership question flagged at the end of Concept-Reactivity.cs §5, not yet specced.)
//
// ─────────────────────────────────────────────────────────────────────────────────────────
//  What this example is meant to prove
// ─────────────────────────────────────────────────────────────────────────────────────────
//
//  - §1 contains every idea in the library and ZERO platform code. That is the DSL.
//  - §2 contains every generated mechanism and ZERO platform code. That is the core.
//  - §3 is disjoint dialects over identical twins. That is the host boundary working.
//  - §5 asserts the hard guarantees (single reload, stale-loss) without any UI at all.
//  - The full §1↔§4 porting diff is one accessibility keyword and how the API arrives.
