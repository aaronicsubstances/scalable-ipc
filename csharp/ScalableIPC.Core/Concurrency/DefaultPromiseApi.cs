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

        public PromiseCompletionSource<T> CreateCallback<T>(ISessionTaskExecutor sessionTaskExecutor)
        {
            return new DefaultPromiseCompletionSource<T>(sessionTaskExecutor);
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
            return ThenCompose(result =>
            {
                var retResult = onFulfilled(result);
                return new DefaultPromise<U>(Task.FromResult(retResult));
            });
        }

        public AbstractPromise<T> Catch(Action<AggregateException> onRejected)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                if (task.Status != TaskStatus.RanToCompletion)
                {
                    onRejected.Invoke(DetermineTaskException(task));
                }
                return task;
            });
            return new DefaultPromise<T>(continuationTask.Unwrap());
        }

        private static AggregateException DetermineTaskException(Task task)
        {
            if (task.Exception != null)
            {
                return task.Exception;
            }
            else
            {
                return new AggregateException(new TaskCanceledException(task));
            }
        }

        public AbstractPromise<T> Finally(Action onFinally)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                onFinally();
                // return original task in its completed or faulted state.
                return task;
            });
            return new DefaultPromise<T>(continuationTask.Unwrap());
        }

        public AbstractPromise<U> ThenCompose<U>(Func<T, AbstractPromise<U>> onFulfilled)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                if (task.Status != TaskStatus.RanToCompletion)
                {
                    // Notes on previous attempts:

                    // 1. tried to use TaskContinuationOptions to only continue
                    //    when antecedent task is successful. problem however was
                    //    that any error occuring in onFulfilled handler isn't
                    //    forwarded; rather the forwarded task is marked as cancelled,
                    //    with null task.exception result.

                    // 2. tried to forward fault by returning task as is, and removing generics from entire implementation.
                    // problem however was that the eventual continuation task now gets a status of RanToCompletion.

                    // so rather create an equivalent faulty task.
                    var errorTask = new TaskCompletionSource<U>();
                    if (task.Exception != null)
                    {
                        errorTask.SetException(TrimOffAggregateException(task.Exception));
                    }
                    else
                    {
                        errorTask.SetCanceled();
                    }
                    return errorTask.Task;
                }
                else
                {
                    AbstractPromise<U> continuationPromise = onFulfilled(task.Result);
                    return ((DefaultPromise<U>)continuationPromise).WrappedTask;
                }
            });
            return new DefaultPromise<U>(continuationTask.Unwrap());
        }

        /// <summary>
        /// Used to prevent aggregate exception tree of nested exceptions from growing
        /// long in an unbounded manner with each forwarding of faulted tasks.
        /// </summary>
        /// <param name="ex"></param>
        /// <returns></returns>
        private static Exception TrimOffAggregateException(AggregateException ex)
        {
            if (ex.InnerExceptions.Count == 1)
            {
                return ex.InnerExceptions[0];
            }
            return ex;
        }

        public AbstractPromise<T> CatchCompose(Func<AggregateException, AbstractPromise<T>> onRejected)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                // Note on previous implementation attempt:
                // 1. This method was the first place where TaskContinuationOptions was observed to
                // be doing something different from what is required for its operation:
                // Using OnlyOnFaulted resulted in success results not being forwarded.

                // so rather get task and use its status to determine next steps.
                // in the end TaskContinuationOptions was abandoned entirely in this class,
                // and if else is rather used.

                if (task.Status == TaskStatus.RanToCompletion)
                {
                    return task;
                }
                else
                {
                    AbstractPromise<T> continuationPromise = onRejected(DetermineTaskException(task));
                    return ((DefaultPromise<T>)continuationPromise).WrappedTask;
                }
            });
            return new DefaultPromise<T>(continuationTask.Unwrap());
        }

        public AbstractPromise<U> ThenOrCatchCompose<U>(Func<T, AbstractPromise<U>> onFulfilled,
            Func<AggregateException, AbstractPromise<U>> onRejected)
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
                    continuationPromise = onRejected(DetermineTaskException(task));
                }
                return ((DefaultPromise<U>)continuationPromise).WrappedTask;
            });
            return new DefaultPromise<U>(continuationTask.Unwrap());
        }
    }

    public class DefaultPromiseCompletionSource<T> : PromiseCompletionSource<T>
    {
        private readonly ISessionTaskExecutor _sessionTaskExecutor;

        public DefaultPromiseCompletionSource(ISessionTaskExecutor sessionTaskExecutor)
        {
            _sessionTaskExecutor = sessionTaskExecutor;
            WrappedSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            RelatedPromise = new DefaultPromise<T>(WrappedSource.Task);
        }

        public TaskCompletionSource<T> WrappedSource { get; }
        public AbstractPromise<T> RelatedPromise { get; }

        // Contract here is that both Complete* methods should behave like notifications, and
        // hence aftermath of these calls should execute outside event loop if possible, but after current
        // event in event loop has been processed. 
        // NB: Hence use of RunContinuationsAsynchronously in constructor.
        public void CompleteSuccessfully(T value)
        {
            _sessionTaskExecutor.PostCallback(() => WrappedSource.TrySetResult(value));
        }

        public void CompleteExceptionally(Exception error)
        {
            _sessionTaskExecutor.PostCallback(() => WrappedSource.TrySetException(error));
        }
    }
}
