namespace Tempest.Demo.Client;

public enum TodoPriority { Low, Normal, High }

/// <summary>Immutable DTO — callers never share mutable state with the store.</summary>
public record TodoItem(int Id, string Title, bool Done, TodoPriority Priority, DateTime CreatedAt);

/// <summary>A search response: the filtered items plus derived data the
/// backend computes (the open count spans the whole store, not the filter).</summary>
public record TodoPage(List<TodoItem> Items, int OpenCount);

/// <summary>In-memory backend with artificial latency so IsLoading and latest-wins
/// cancellation are visible. Everything goes through it — reads, writes, derived
/// counts, ordering. Expected problems are returned as values, never thrown.</summary>
public class TodoApi
{
    private int _nextId = 5;
    private readonly List<TodoItem> _store =
    [
        new(1, "Read the Tempest README", false, TodoPriority.Normal, DateTime.Now.AddDays(-3)),
        new(2, "Ship the state refactor", true, TodoPriority.High, DateTime.Now.AddDays(-2)),
        new(3, "Write a todo app", false, TodoPriority.High, DateTime.Now.AddHours(-20)),
        new(4, "Water the office plants", false, TodoPriority.Low, DateTime.Now.AddHours(-2)),
    ];

    public async Task<TodoPage> SearchAsync(string query, CancellationToken ct)
    {
        await Task.Delay(300, ct);
        query = query.Trim();
        var items = query.Length == 0
            ? _store.AsEnumerable()
            : _store.Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase));

        // Ordering is backend policy: open before done, urgent before relaxed, oldest first.
        var ordered = items
            .OrderBy(t => t.Done)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt);

        return new TodoPage([.. ordered], _store.Count(t => !t.Done));
    }

    public async Task<(TodoItem? Created, string? Problem)> CreateAsync(
        string title, TodoPriority priority, CancellationToken ct)
    {
        await Task.Delay(300, ct);
        if (_store.Any(t => string.Equals(t.Title, title, StringComparison.OrdinalIgnoreCase)))
            return (null, $"'{title}' is already on the list.");

        var item = new TodoItem(_nextId++, title, false, priority, DateTime.Now);
        _store.Add(item);
        return (item, null);
    }

    public async Task SetDoneAsync(int id, bool done, CancellationToken ct = default)
    {
        await Task.Delay(150, ct);
        var index = _store.FindIndex(t => t.Id == id);
        if (index >= 0)
            _store[index] = _store[index] with { Done = done };
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await Task.Delay(150, ct);
        _store.RemoveAll(t => t.Id == id);
    }
}
