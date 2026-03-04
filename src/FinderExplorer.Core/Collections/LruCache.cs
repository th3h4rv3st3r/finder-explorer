// Copyright (c) Finder Explorer. All rights reserved.

using System.Collections.Generic;

namespace FinderExplorer.Core.Collections;

/// <summary>
/// Thread-safe, fixed-capacity Least-Recently-Used cache.
/// Evicts the oldest entry when capacity is exceeded.
/// </summary>
public sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly object _lock = new();
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
    private readonly LinkedList<(TKey Key, TValue Value)> _list = new();

    public LruCache(int capacity)
    {
        _capacity = capacity;
        _map      = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
    }

    /// <summary>Returns <see langword="true"/> and sets <paramref name="value"/> if found (promotes to MRU).</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // Promote to front (most-recently-used)
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }
    }

    /// <summary>Inserts or updates the entry; evicts LRU entry when over capacity.</summary>
    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _list.Remove(existing);
                _map.Remove(key);
            }

            if (_map.Count >= _capacity)
            {
                // Evict least-recently-used (tail)
                var lru = _list.Last!;
                _map.Remove(lru.Value.Key);
                _list.RemoveLast();
            }

            var node = _list.AddFirst((key, value));
            _map[key] = node;
        }
    }

    /// <summary>Removes an entry if present.</summary>
    public void Remove(TKey key)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _map.Remove(key);
            }
        }
    }

    /// <summary>Clears all entries.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _list.Clear();
        }
    }

    public int Count { get { lock (_lock) return _map.Count; } }
}
