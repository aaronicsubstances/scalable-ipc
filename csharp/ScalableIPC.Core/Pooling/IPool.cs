using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ScalableIPC.Core.Pooling
{
    public interface IPool<T, U> : IDisposable
    {
        Task Start();
        int MaximumSizeHint { get; }
        int MinimumSizeHint { get; }
        int CurrentSize { get; }
        Task<T> Acquire(U key, bool? mustExist);
        Task Release(T item);
        Task ReleaseKey(U key);
        Task<T> CreatePoolItem(U key);
        Task DisposePoolItem(T item);
    }
}
