﻿using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ScalableIPC.Core.ConcreteComponents
{
    public class DefaultPromiseApi : AbstractPromiseApi
    {
        public DefaultPromiseApi(AbstractEventLoopApi eventLoopApi)
        {
            EventLoopApi = eventLoopApi;
        }

        public AbstractEventLoopApi EventLoopApi { get; }

        public PromiseCompletionSource<T> CreateCallback<T>()
        {
            return new DefaultPromiseCompletionSource<T>(EventLoopApi);
        }

        public AbstractPromise<VoidType> Reject(Exception reason)
        {
            return new DefaultPromise<VoidType>(Task.FromException<VoidType>(reason));
        }

        public AbstractPromise<T> Resolve<T>(T value)
        {
            return new DefaultPromise<T>(Task.FromResult(value));
        }
    }

    public class DefaultPromise<T>: AbstractPromise<T>
    {
        public DefaultPromise(Task<T> task)
        {
            WrappedTask = task;
        }

        public Task<T> WrappedTask { get; }

        public AbstractPromise<U> Then<U>(Func<T, U> onFulfilled, Action<Exception> onRejected = null)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    return onFulfilled(task.Result);
                }
                else
                {
                    onRejected(task.Exception);
                    throw task.Exception;
                }
            });
            return new DefaultPromise<U>(continuationTask);
        }

        public AbstractPromise<U> ThenCompose<U>(Func<T, AbstractPromise<U>> onFulfilled,
            Func<Exception, AbstractPromise<U>> onRejected = null)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                AbstractPromise<U> continuationPromise;
                if (task.IsCompletedSuccessfully)
                {
                    continuationPromise = onFulfilled(task.Result);
                }
                else
                {
                    continuationPromise = onRejected(task.Exception);
                }
                return ((DefaultPromise<U>) continuationPromise).WrappedTask;
            });
            return new DefaultPromise<U>(continuationTask.Unwrap());
        }
    }

    public class DefaultPromiseCompletionSource<T> : PromiseCompletionSource<T>
    {
        public DefaultPromiseCompletionSource(AbstractEventLoopApi eventLoopApi)
        {
            EventLoopApi = eventLoopApi;
            WrappedSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            ExtractedPromise = new DefaultPromise<T>(WrappedSource.Task);
        }

        public TaskCompletionSource<T> WrappedSource { get; }
        public DefaultPromise<T> ExtractedPromise { get; }

        public AbstractEventLoopApi EventLoopApi { get; }

        public AbstractPromise<T> Extract()
        {
            return ExtractedPromise;
        }

        // Contract here is that both Complete* methods should behave like notifications, and
        // hence these should be called from event loop.
        public void CompleteSuccessfully(T value)
        {
            EventLoopApi.PostCallback(() => WrappedSource.TrySetResult(value));
        }

        public void CompleteExceptionally(Exception error)
        {
            EventLoopApi.PostCallback(() => WrappedSource.TrySetException(error));
        }
    }
}