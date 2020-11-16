using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScalableIPC.Core.Pooling
{
    public abstract class AbstractPool<T, U> : IPool<T, U>
    {
        private readonly BackingStore<T, U> _store;

        public AbstractPool()
        {
            _store = new BackingStore<T, U>();
        }

        public int MinimumSizeHint
        {
            get
            {
                return _store.MinimumItemCountHint;
            }

            set
            {
                _store.MinimumItemCountHint = value;
            }
        }

        public int MaximumSizeHint { get; set; }
        public int CurrentSize
        {
            get
            {
                return _store.ItemCount;
            }
        }

        public async Task<T> Acquire(U key, bool? mustExist)
        {
            T item;
            if (mustExist != false)
            {
                item = _store.FindItem(key);
                if (mustExist == true || !Equals(item, default(T)))
                {
                    return item;
                }
            }

            if (CurrentSize < MaximumSizeHint)
            {
                item = await CreatePoolItem(key);
                _store.Add(key, item);
            }
            else
            {
                // get next item to send out.
                item = _store.Add(key, default(T));
            }
            return item;
        }

        public async Task Release(T item)
        {
            _store.RemoveItem(item);
            await DisposePoolItem(item);
        }

        public async Task ReleaseKey(U key)
        {
            T item = _store.RemoveKey(key);
            if (!Equals(item, default(T)))
            {
                await DisposePoolItem(item);
            }
        }

        public async void Dispose()
        {
            var itemList = _store.ListItems(true);
            foreach (var item in itemList)
            {
                try
                {
                    _store.RemoveItem(item);
                    await DisposePoolItem(item);
                }
                catch (Exception)
                {
                    // ignore
                }
            }
        }

        public virtual Task Start()
        {
            return Task.CompletedTask;
        }

        public abstract Task<T> CreatePoolItem(U key);
        public abstract Task DisposePoolItem(T item);
    }
}
