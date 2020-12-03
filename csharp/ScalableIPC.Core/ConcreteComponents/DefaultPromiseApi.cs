using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScalableIPC.Core.ConcreteComponents
{
    public class DefaultPromiseApi : AbstractPromiseApi
    {
        public PromiseCompletionSource<T> CreateCallback<T>(ISessionHandler sessionHandler)
        {
            return new DefaultPromiseCompletionSource<T>(sessionHandler);
        }

        public AbstractPromise<VoidType> Reject(Exception reason)
        {
            return new DefaultPromise<VoidType>(Task.FromException<VoidType>(reason));
        }

        public AbstractPromise<T> Resolve<T>(T value)
        {
            return new DefaultPromise<T>(Task.FromResult(value));
        }

        public AbstractPromise<VoidType> Delay(int secs)
        {
            return new DefaultPromise<VoidType>(Task.Delay(secs * 100)
                .ContinueWith(_ => VoidType.Instance));
        }

        public AbstractPromise<T> Race<T>(params AbstractPromise<T>[] competitors)
        {
            var tasks = competitors.Select(c => ((DefaultPromise<T>)c).WrappedTask)
                .ToArray();
            int firstToFinish = Task.WaitAny(tasks);
            return new DefaultPromise<T>(tasks[firstToFinish]);
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
        private readonly AbstractEventLoopApi _eventLoop;
        public DefaultPromiseCompletionSource(ISessionHandler sessionHandler)
        {
            _eventLoop = sessionHandler.EventLoop;
            WrappedSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            ExtractedPromise = new DefaultPromise<T>(WrappedSource.Task);
        }

        public TaskCompletionSource<T> WrappedSource { get; }
        public DefaultPromise<T> ExtractedPromise { get; }

        public AbstractPromise<T> Extract()
        {
            return ExtractedPromise;
        }

        // Contract here is that both Complete* methods should behave like notifications, and
        // hence these should be called from event loop.
        public void CompleteSuccessfully(T value)
        {
            _eventLoop.PostCallback(() => WrappedSource.TrySetResult(value));
        }

        public void CompleteExceptionally(Exception error)
        {
            _eventLoop.PostCallback(() => WrappedSource.TrySetException(error));
        }
    }
}
