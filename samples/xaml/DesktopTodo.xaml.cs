namespace Tempest.Samples.Desktop;

// Same component as samples/blazor/Todo.razor, declared in XAML's native shape:
// the markup lives in DesktopTodo.xaml, the attributed members live here in the
// code-behind, and the generated twin lands in DesktopTodo.Tempest.g.cs.
// StatefulControl is the XAML host base: the same four-member contract
// StatefulComponent implements for Blazor, backed by DispatcherQueue instead of
// InvokeAsync/StateHasChanged.
public sealed partial class DesktopTodo : StatefulControl
{
    public DesktopTodo() => InitializeComponent();

    [Reactive] private string _newTitle = "";
    [Reactive] private List<TodoItem> _todos = [];

    [OnChanged]
    private void OnNewTitleChanged(string value) => Validate(value);

    [Command]
    private async Task<List<TodoItem>> Load(CancellationToken ct)
        => await Api.GetTodosAsync(ct);

    [Command]
    private async Task Add(CancellationToken ct)
    {
        var created = await Api.AddTodoAsync(_newTitle, ct);
        TodosState.Value = [.. _todos, created];
        NewTitleState.Reset();
    }

    public sealed record TodoCompleted(int Id);

    [Event]
    private async Task OnTodoCompleted(TodoCompleted e)
    {
        await Api.CompleteAsync(e.Id);
        TodosState.Value = [.. _todos.Where(t => t.Id != e.Id)];
    }

    private void CompleteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TodoItem todo })
            Bus.Publish(new TodoCompleted(todo.Id));
    }
}
