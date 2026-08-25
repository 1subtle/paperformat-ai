using System.Collections;
using System.Globalization;

namespace PaperFormat.Domain;

/// <summary>
/// An immutable, structurally comparable sequence used by domain models.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly T[] _items;

    /// <summary>
    /// Creates an immutable snapshot of <paramref name="items"/>.
    /// </summary>
    public ValueList(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
    }

    /// <inheritdoc />
    public int Count => _items.Length;

    /// <inheritdoc />
    public T this[int index] => _items[index];

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() =>
        ((IEnumerable<T>)_items).GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    public bool Equals(ValueList<T>? other) =>
        ReferenceEquals(this, other)
        || (other is not null && _items.SequenceEqual(other._items));

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is ValueList<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var item in _items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Count = {_items.Length}");
}
