using System.Collections.Concurrent;

namespace ConsoleApp1_vdp_sheetbuilder.Services
{
    /// <summary>
    /// Thread-safe LRU (Least Recently Used) cache implementation for memory-efficient XObject caching
    /// Prevents unbounded memory growth during large PDF processing
    /// </summary>
    /// <typeparam name="TKey">Cache key type</typeparam>
    /// <typeparam name="TValue">Cache value type that implements IDisposable</typeparam>
    public class LRUCache<TKey, TValue> : IDisposable
        where TKey : notnull
        where TValue : class
    {
        private readonly int _maxCapacity;
        private readonly ConcurrentDictionary<TKey, CacheNode> _cache;
        private readonly object _lock = new object();

        // Doubly linked list for LRU tracking
        private CacheNode? _head;
        private CacheNode? _tail;
        private int _currentSize;

        public LRUCache(int maxCapacity = 1000)
        {
            _maxCapacity = maxCapacity;
            _cache = new ConcurrentDictionary<TKey, CacheNode>();
            _currentSize = 0;
        }

        public bool TryGetValue(TKey key, out TValue? value)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    // Move to front (most recently used)
                    MoveToFront(node);
                    value = node.Value;
                    return true;
                }

                value = null;
                return false;
            }
        }

        public void Set(TKey key, TValue value)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var existingNode))
                {
                    // Update existing node and move to front
                    if (existingNode.Value is IDisposable disposable)
                        disposable.Dispose();
                    existingNode.Value = value;
                    MoveToFront(existingNode);
                    return;
                }

                // Create new node
                var newNode = new CacheNode(key, value);

                // Add to cache
                _cache[key] = newNode;
                AddToFront(newNode);
                _currentSize++;

                // Evict if necessary
                if (_currentSize > _maxCapacity)
                {
                    EvictLeastRecentlyUsed();
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                // Dispose all values that implement IDisposable
                foreach (var kvp in _cache)
                {
                    if (kvp.Value.Value is IDisposable disposable)
                        disposable.Dispose();
                }

                _cache.Clear();
                _head = null;
                _tail = null;
                _currentSize = 0;
            }
        }

        public int Count => _currentSize;
        public int MaxCapacity => _maxCapacity;

        private void MoveToFront(CacheNode node)
        {
            if (node == _head) return;

            // Remove from current position
            RemoveNode(node);

            // Add to front
            AddToFront(node);
        }

        private void AddToFront(CacheNode node)
        {
            node.Next = _head;
            node.Previous = null;

            if (_head != null)
            {
                _head.Previous = node;
            }

            _head = node;

            if (_tail == null)
            {
                _tail = node;
            }
        }

        private void RemoveNode(CacheNode node)
        {
            if (node.Previous != null)
            {
                node.Previous.Next = node.Next;
            }
            else
            {
                _head = node.Next;
            }

            if (node.Next != null)
            {
                node.Next.Previous = node.Previous;
            }
            else
            {
                _tail = node.Previous;
            }
        }

        private void EvictLeastRecentlyUsed()
        {
            if (_tail == null) return;

            var lruNode = _tail;

            // Remove from cache
            _cache.TryRemove(lruNode.Key, out _);

            // Remove from linked list
            RemoveNode(lruNode);

            // Dispose the value if it implements IDisposable
            if (lruNode.Value is IDisposable disposable)
                disposable.Dispose();

            _currentSize--;
        }

        public void Dispose()
        {
            Clear();
        }

        private class CacheNode
        {
            public TKey Key { get; }
            public TValue? Value { get; set; }
            public CacheNode? Next { get; set; }
            public CacheNode? Previous { get; set; }

            public CacheNode(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
        }
    }
}