using System.Collections;

namespace Tempest.Model;

/// <summary>An immutable array with sequence value-equality, so records holding lists of
/// models still compare by value — which incremental-caching and test assertions rely on.
/// A default instance behaves as empty.</summary>
public readonly struct EquatableArray<T>(T[] items) : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    public static EquatableArray<T> Empty { get; } = new([]);

    private readonly T[]? _items = items;

    public int Count => _items?.Length ?? 0;

    public T this[int index] => (_items ?? [])[index];

    public bool Equals(EquatableArray<T> other)
        => (_items ?? []).AsSpan().SequenceEqual((other._items ?? []).AsSpan());

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items ?? [])
            hash.Add(item);
        return hash.ToHashCode();
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
