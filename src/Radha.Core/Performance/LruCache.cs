namespace Radha.Core.Performance;

/// <summary>
/// Bounded least-recently-used cache. O(1) reads/writes; used to memoise wallpaper →
/// palette extraction so re-opening the wallpaper picker never re-runs k-means.
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly record struct Entry(TValue Value, LinkedListNode<TKey> Node);

    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Dictionary<TKey, Entry> _map;
    private readonly LinkedList<TKey> _order = new();
    private long _hits;
    private long _misses;

    public LruCache(int capacity = 32)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _map = new Dictionary<TKey, Entry>(capacity);
    }

    public int Count { get { lock (_gate) return _map.Count; } }
    public int Capacity => _capacity;
    public long Hits { get { lock (_gate) return _hits; } }
    public long Misses { get { lock (_gate) return _misses; } }
    public double HitRatio
    {
        get
        {
            lock (_gate)
            {
                var total = _hits + _misses;
                return total == 0 ? 0 : (double)_hits / total;
            }
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var entry))
            {
                _order.Remove(entry.Node);
                _order.AddFirst(entry.Node);
                _hits++;
                value = entry.Value;
                return true;
            }

            _misses++;
            value = default!;
            return false;
        }
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        if (TryGet(key, out var existing)) return existing;
        var created = factory(key);
        Put(key, created);
        return created;
    }

    public void Put(TKey key, TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing.Node);
                var node = _order.AddFirst(key);
                _map[key] = new Entry(value, node);
                return;
            }

            var created = _order.AddFirst(key);
            _map[key] = new Entry(value, created);

            while (_map.Count > _capacity && _order.Last is { } last)
            {
                _order.RemoveLast();
                _map.Remove(last.Value);
            }
        }
    }

    public bool TryRemove(TKey key)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var entry)) return false;
            _order.Remove(entry.Node);
            return _map.Remove(key);
        }
    }

    /// <summary>Snapshot of current keys, most-recent first (used for prefix invalidation).</summary>
    public IReadOnlyList<TKey> KeysSnapshot()
    {
        lock (_gate) return _order.ToList();
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _order.Clear();
            _hits = 0;
            _misses = 0;
        }
    }
}
