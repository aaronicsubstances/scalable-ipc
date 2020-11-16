using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core.Pooling
{
    public class BackingStore<T, U>
    {
        private readonly List<T> _items;
        private readonly Dictionary<U, T> _itemMap;
        private readonly Dictionary<T, Counter> _keyCounters;
        private int _nextIndex;

        public BackingStore()
        {
            _items = new List<T>();
            _itemMap = new Dictionary<U, T>();
            _keyCounters = new Dictionary<T, Counter>();
        }

        public int MinimumItemCountHint { get; set; }
        
        public int ItemCount
        {
            get
            {
                return _items.Count;
            }
        }

        public int KeyCount
        {
            get
            {
                return _itemMap.Count;
            }
        }

        public bool IsDisposing { get; private set; }

        public T FindItem(U key)
        {
            lock (this)
            {
                if (_itemMap.ContainsKey(key))
                {
                    return _itemMap[key];
                }
                return default(T);
            }
        }

        public T Add(U key, T item = default(T))
        {
            lock (this)
            {
                if (IsDisposing)
                    throw new InvalidOperationException("disposing started");

                Counter itemKeyCounts;
                // if item is not null, then treat as new.
                if (!Equals(item, default(T)))
                {
                    _items.Add(item);
                    itemKeyCounts = new Counter();
                    _keyCounters.Add(item, itemKeyCounts);
                }
                else
                {
                    // use an existing one.
                    if (_items.Count == 0)
                        throw new InvalidOperationException("There are no items");

                    // increment round robin index in a way that caters for changes to
                    // item counts.
                    _nextIndex %=_items.Count;
                    item = _items[_nextIndex++];
                    itemKeyCounts = _keyCounters[item];
                }

                // associate item with key.
                _itemMap.Add(key, item);
                itemKeyCounts.Count++;
                return item;
            }
        }

        public T RemoveKey(U key)
        {
            lock (this)
            {
                T item = default(T);
                if (_itemMap.ContainsKey(key))
                {
                    item = _itemMap[key];
                    _itemMap.Remove(key);
                    var itemKeyCount = _keyCounters[item];
                    itemKeyCount.Count--;
                    if (_items.Count > MinimumItemCountHint && itemKeyCount.Count <= 0)
                    {
                        _keyCounters.Remove(item);
                        _items.Remove(item);
                        // leave item as it is for return
                        // so it will be disposed.
                    }
                    else
                    {
                        // nullify item for return so it doesn't get disposed.
                        item = default(T);
                    }
                }
                return item;
            }
        }

        // Due to expected usage with connection pooling, the methods below have
        // lower priority for efficiency compared to the data structure methods above.

        public bool RemoveItem(T item)
        {
            lock (this)
            {
                // using dict.ContainsKey in preference to list.Contains due
                // to the former being faster.
                if (_keyCounters.ContainsKey(item))
                {
                    _keyCounters.Remove(item);
                    _items.Remove(item);

                    // Go through entire items and remove keys
                    // corresponding to this item.
                    var keys = _itemMap.Keys.ToList();
                    foreach (var k in keys)
                    {
                        if (Equals(item, _itemMap[k]))
                        {
                            _itemMap.Remove(k);
                        }
                    }
                    return true;
                }
                return false;
            }
        }

        public List<T> ListItems(bool disposing)
        {
            lock (this)
            {
                IsDisposing = disposing;
                // return new list every time.
                return _items.ToList();
            }
        }

        class Counter
        {
            public int Count { get; set; }
        }
    }
}
