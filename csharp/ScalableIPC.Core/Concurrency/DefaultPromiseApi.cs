using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScalableIPC.Core.Concurrency
{
    public class DefaultPromiseApi : AbstractPromiseApi
    {
        // helper field for getting a void async return anywhere needed, without having to fetch an 
        // AbstractPromiseApi instance.
        // Is part of reference implementation API
        public static readonly AbstractPromise<VoidType> CompletedPromise = new DefaultPromise<VoidType>(
            Task.FromResult(VoidType.Instance));

        public PromiseCompletionSource<T> CreateCallback<T>()
        {
            return new DefaultPromiseCompletionSource<T>();
        }

        public AbstractPromise<T> Reject<T>(Exception reason)
        {
            return new DefaultPromise<T>(Task.FromException<T>(reason));
        }

        public AbstractPromise<T> Resolve<T>(T value)
        {
            return new DefaultPromise<T>(Task.FromResult(value));
        }

        public AbstractPromise<VoidType> Delay(int millis)
        {
            return new DefaultPromise<VoidType>(Task.Delay(millis)
                .ContinueWith(_ => VoidType.Instance));
        }
    }

    public class DefaultPromise<T>: AbstractPromise<T>
    {
        public DefaultPromise(Task<T> task)
        {
            WrappedTask = task;
        }

        public Task<T> WrappedTask { get; }

        public AbstractPromise<U> Then<U>(Func<T, U> onFulfilled)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                return onFulfilled(task.Result);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            return new DefaultPromise<U>(continuationTask);
        }

        public AbstractPromise<T> Catch(Action<Exception> onRejected)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                if (task.Status != TaskStatus.RanToCompletion)
                {
                    onRejected.Invoke(task.Exception);
                }
                return task;
            });
            return new DefaultPromise<T>(continuationTask.Unwrap());
        }

        public AbstractPromise<U> ThenCompose<U>(Func<T, AbstractPromise<U>> onFulfilled)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                AbstractPromise<U> continuationPromise = onFulfilled(task.Result);
                return ((DefaultPromise<U>) continuationPromise).WrappedTask;
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            return new DefaultPromise<U>(continuationTask.Unwrap());
        }

        /// <summary>
        /// Bugs in initial implementation of CatchCompose forced us to abandon use
        /// of TaskContinuationOptions, and use if else conditional instead.
        /// Bugs were:
        ///  1. CatchCompose wasn't forwarding success results to subsequent Then handlers.
        /// </summary>
        /// <param name="onRejected"></param>
        /// <returns></returns>
        public AbstractPromise<T> CatchCompose(Func<Exception, AbstractPromise<T>> onRejected)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    return task;
                }
                else
                {
                    AbstractPromise<T> continuationPromise = onRejected(task.Exception);
                    return ((DefaultPromise<T>)continuationPromise).WrappedTask;
                }
            });
            return new DefaultPromise<T>(continuationTask.Unwrap());
        }

        public AbstractPromise<U> ThenOrCatchCompose<U>(Func<T, AbstractPromise<U>> onFulfilled,
            Func<Exception, AbstractPromise<U>> onRejected)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                AbstractPromise<U> continuationPromise;
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    continuationPromise = onFulfilled(task.Result);
                }
                else
                {
                    continuationPromise = onRejected(task.Exception);
                }
                return ((DefaultPromise<U>)continuationPromise).WrappedTask;
            });
            return new DefaultPromise<U>(continuationTask.Unwrap());
        }
    }

    public class DefaultPromiseCompletionSource<T> : PromiseCompletionSource<T>
    {
        public DefaultPromiseCompletionSource()
        {
            WrappedSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            ExtractedPromise = new DefaultPromise<T>(WrappedSource.Task);
        }

        public TaskCompletionSource<T> WrappedSource { get; }
        public DefaultPromise<T> ExtractedPromise { get; }

        public AbstractPromise<T> Extract()
        {
            return ExtractedPromise;
        }
    }
}
