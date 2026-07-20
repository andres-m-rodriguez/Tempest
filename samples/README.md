# Samples: one component, two UI frameworks

The same Todo component twice — identical mental model, identical attributes,
identical generated shape. Only the host differs.

| | Blazor | XAML (WinUI/MAUI, planned) |
|---|---|---|
| Markup | `blazor/Todo.razor` (markup + `@code` in one file) | `xaml/DesktopTodo.xaml` (markup) |
| Members | in the `@code` block | `xaml/DesktopTodo.xaml.cs` (code-behind) |
| Generated twin | `blazor/Todo.Tempest.g.cs` | `xaml/DesktopTodo.Tempest.g.cs` |
| Host base | `StatefulComponent` (InvokeAsync/StateHasChanged) | `StatefulControl` (DispatcherQueue) |
| Binding | `@bind="NewTitleState.Value"` | `{x:Bind NewTitleState.Value, Mode=TwoWay}` |
| Commands | `@onclick="AddState.Execute"` | `Command="{x:Bind AddState}"` — CommandState is an ICommand |

**`blazor/Todo.Tempest.g.cs` is real output**, produced by running the actual
pipeline (RazorParser → TempestCompiler → Emitter) over `Todo.razor`.

**`xaml/DesktopTodo.Tempest.g.cs` is illustrative** until the C# symbol frontend
and the XAML host base exist. Its one honest difference from the Blazor twin:
symbol-sourced type names arrive fully qualified, so the generated file needs no
using directives (razor members are emitted as written, which is why that path
carries the file's `@using`s along).

The emitter never forks per framework: generated code references only
Tempest.Core types plus the four-member host contract (`RegisterTempestHandlers`,
`SubscribeEvent`, `DispatchEvent`, `InvokeAsync`). `TodoItem` and `Api` are
assumed application types — these samples document the DSL, they are not a
runnable project.
