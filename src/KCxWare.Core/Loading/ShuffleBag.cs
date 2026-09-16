namespace KCxWare.Core.Loading;

/// <summary>
/// Reusable "random order, but never miss one" sequencing: every item in the source collection is
/// emitted exactly once, in a random order, before any item repeats. A cycle is a full pass through
/// every item; a fresh cycle is only started once the previous one is completely exhausted, and the
/// bag tries to avoid immediately repeating the previous cycle's final item as the new cycle's first
/// item (a "boundary repeat"), so long as the source has more than one item.
/// </summary>
public sealed class ShuffleBag<T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly IRandomProvider _random;
    private readonly IEqualityComparer<T> _comparer;

    private Queue<T> _queue = new();
    private T? _lastEmitted;
    private bool _hasEmitted;

    public ShuffleBag(IReadOnlyList<T> items, IRandomProvider random, IEqualityComparer<T>? comparer = null)
    {
        if (items.Count == 0) throw new ArgumentException("ShuffleBag requires at least one item.", nameof(items));
        _items = items;
        _random = random;
        _comparer = comparer ?? EqualityComparer<T>.Default;
    }

    /// <summary>Returns the next item in the current shuffled cycle, refilling and reshuffling once exhausted.</summary>
    public T Next()
    {
        if (_queue.Count == 0) Refill();

        var item = _queue.Dequeue();
        _lastEmitted = item;
        _hasEmitted = true;
        return item;
    }

    private void Refill()
    {
        var shuffled = Shuffle(_items);

        // Avoid repeating the previous cycle's final item as this cycle's first item, when possible.
        if (_hasEmitted && shuffled.Count > 1 && _comparer.Equals(shuffled[0], _lastEmitted!))
        {
            var swapWith = _random.Next(1, shuffled.Count);
            (shuffled[0], shuffled[swapWith]) = (shuffled[swapWith], shuffled[0]);
        }

        _queue = new Queue<T>(shuffled);
    }

    private List<T> Shuffle(IReadOnlyList<T> source)
    {
        var list = new List<T>(source);
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _random.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }
}
