using System.Collections;

namespace Tempest.Pipeline;

/// <summary>An immutable array with sequence value-equality, so records holding lists of
/// models still compare by value — which incremental caching and test assertions rely on.
/// A default instance behaves as empty. Implemented without Span or HashCode: the
/// pipeline compiles against netstandard2.0, which has neither.</summary>
public readonly struct EquatableArray<T>(T[] items) : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    public static EquatableArray<T> Empty { get; } = new([]);

    private readonly T[]? _items = items;

    public int Count => _items?.Length ?? 0;

    public T this[int index] => (_items ?? [])[index];

    public bool Equals(EquatableArray<T> other)
    {
        var left = _items ?? [];
        var right = other._items ?? [];
        if (left.Length != right.Length)
            return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in _items ?? [])
            hash = unchecked(hash * 31 + (item?.GetHashCode() ?? 0));
        return hash;
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
