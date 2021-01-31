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
        [ThreadStatic]
        private static Guid? _currentLogicalThreadId;

        public static DefaultPromiseApi Instance { get; }  = new DefaultPromiseApi();

        private DefaultPromiseApi()
        { }

        public PromiseCompletionSource<T> CreateCallback<T>(AbstractEventLoopApi completionEventLoop)
        {
            return new DefaultPromiseCompletionSource<T>(this, completionEventLoop);
        }

        public AbstractPromise<T> Resolve<T>(T value)
        {
            return new DefaultPromise<T>(this, Task.FromResult(value));
        }

        public AbstractPromise<T> Reject<T>(Exception reason)
        {
            return new DefaultPromise<T>(this, Task.FromException<T>(reason));
        }

        public AbstractPromise<VoidType> CompletedPromise()
        {
            return Resolve(VoidType.Instance);
        }

        public AbstractPromise<VoidType> Delay(int millis)
        {
            return new DefaultPromise<VoidType>(this, Task.Delay(millis)
                .ContinueWith(_ => VoidType.Instance));
        }

        public AbstractPromise<List<PromiseResult<T>>> WhenAll<T>(params AbstractPromise<T>[] promises)
        {
            var nativePromises = ToNativePromises(promises);
            var raceOutcomeTask = Task.WhenAll(nativePromises)
                .ContinueWith(_ =>
                {
                    return nativePromises.Select(t =>
                    {
                        if (t.Status == TaskStatus.RanToCompletion)
                        {
                            return PromiseResult<T>.CreateSuccess(t.Result);
                        }
                        else if (t.Exception != null)
                        {
                            return PromiseResult<T>.CreateFailure(t.Exception);
                        }
                        else
                        {
                            return PromiseResult<T>.CreateCancellation(t);
                        }
                    }).ToList();
                });
            return new DefaultPromise<List<PromiseResult<T>>>(this, raceOutcomeTask);
        }

        public AbstractPromise<int> WhenAny<T>(params AbstractPromise<T>[] promises)
        {
            var nativePromises = ToNativePromises(promises);
            var raceOutcomeTask = Task.WhenAny(nativePromises)
                .ContinueWith(t => nativePromises.IndexOf(t.Result));
            return new DefaultPromise<int>(this, raceOutcomeTask);
        }

        public AbstractPromise<int> WhenAnySucceed<T>(params AbstractPromise<T>[] promises)
        {
            var nativePromises = ToNativePromises(promises);
            var raceOutcomeTask = WhenAnySucceedImpl(nativePromises, nativePromises,
                new AggregateException[promises.Length]);
            return new DefaultPromise<int>(this, raceOutcomeTask);
        }

        private Task<int> WhenAnySucceedImpl<T>(List<Task<T>> nativePromises, List<Task<T>> uncompleted,
            AggregateException[] tempExceptions)
        {
            return Task.WhenAny(uncompleted)
                .ContinueWith(t =>
                {
                    int tIndex = nativePromises.IndexOf(t.Result);
                    if (nativePromises[tIndex].Status == TaskStatus.RanToCompletion)
                    {
                        return Task.FromResult(tIndex);
                    }
                    else
                    {
                        // for thread safety, leverage immutability and create new
                        var newExceptions = new AggregateException[tempExceptions.Length];
                        for (int i = 0; i < newExceptions.Length; i++)
                        {
                            newExceptions[i] = tempExceptions[i];
                        }
                        newExceptions[tIndex] = t.Result.Exception ?? new AggregateException(
                            new TaskCanceledException(t.Result));
                        var newUncompleted = new List<Task<T>>();
                        foreach (var entry in uncompleted)
                        {
                            if (entry != t.Result)
                            {
                                newUncompleted.Add(entry);
                            }
                        }
                        if (newUncompleted.Count == 0)
                        {
                            return Task.FromException<int>(new AggregateException(newExceptions));
                        }
                        else
                        {
                            return WhenAnySucceedImpl(nativePromises, newUncompleted, newExceptions);
                        }
                    }
                }).Unwrap();
        }

        public AbstractPromise<List<T>> WhenAllSucceed<T>(params AbstractPromise<T>[] promises)
        {
            var nativePromises = ToNativePromises(promises);
            var raceOutcomeTask = WhenAllSucceedImpl(nativePromises, nativePromises,
                new T[promises.Length]);
            return new DefaultPromise<T[]>(this, raceOutcomeTask).Then(r => r.ToList());
        }

        private Task<T[]> WhenAllSucceedImpl<T>(List<Task<T>> nativePromises,
            List<Task<T>> uncompleted, T[] tempResults)
        {
            // WhenAllSucceed is interpreted as NOT WhenAnyFail. 
            // And so implementation is similar to WhenAnySucceed
            return Task.WhenAny(uncompleted)
                .ContinueWith(t =>
                {
                    int tIndex = nativePromises.IndexOf(t.Result);
                    if (nativePromises[tIndex].Status != TaskStatus.RanToCompletion)
                    {
                        return Task.FromException<T[]>(t.Result.Exception ??
                            new AggregateException(new TaskCanceledException(t.Result)));
                    }
                    else
                    {
                        // for thread safety, leverage immutability and create new
                        var newResults = new T[tempResults.Length];
                        for (int i = 0; i < newResults.Length; i++)
                        {
                            newResults[i] = tempResults[i];
                        }
                        newResults[tIndex] = t.Result.Result;

                        var newUncompleted = new List<Task<T>>();
                        foreach (var entry in uncompleted)
                        {
                            if (entry != t.Result)
                            {
                                newUncompleted.Add(entry);
                            }
                        }
                        if (newUncompleted.Count == 0)
                        {
                            return Task.FromResult(newResults);
                        }
                        else
                        {
                            return WhenAllSucceedImpl(nativePromises, newUncompleted, newResults);
                        }
                    }
                }).Unwrap();
        }

        private List<Task<T>> ToNativePromises<T>(AbstractPromise<T>[] promises)
        {
            return promises.Select(p => ((DefaultPromise<T>)p).WrappedTask).ToList();
        }

        public AbstractPromise<T> Poll<T>(Func<PollCallbackArg<T>, PollCallbackRet<T>> cb,
            int intervalMillis, long totalDurationMillis, T initialValue)
        {
            var tcs = new TaskCompletionSource<T>();
            Task.Run(() => StartPolling(DateTime.UtcNow, initialValue, cb, 
                intervalMillis, totalDurationMillis, tcs));
            var promise = new DefaultPromise<T>(this, tcs.Task);
            return promise;
        }

        private Task StartPolling<T>(DateTime startTime, T prevValue, 
            Func<PollCallbackArg<T>, PollCallbackRet<T>> cb,
            int intervalMillis, long totalDurationMillis,
            TaskCompletionSource<T> tcs)
        {
            try
            {
                // NB: current implementation invokes callback at least once.
                var cbArg = new PollCallbackArg<T>
                {
                    Value = prevValue,
                    UptimeMillis = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
                };

                // for predictability in knowing which call of the callback is the last one,
                // determine upfront rather than after callback is executed.
                var onLastCall = (totalDurationMillis - cbArg.UptimeMillis) < intervalMillis;
                cbArg.LastCall = onLastCall;

                // Now execute callback.
                var cbRes = cb.Invoke(cbArg);
                    
                if (cbRes != null && cbRes.Stop)
                {
                    tcs.SetResult(cbRes.NextValue);
                }
                else
                {
                    T nextValue = default;
                    if (cbRes != null)
                    {
                        nextValue = cbRes.NextValue;
                    }

                    // check if time is up.
                    // just in case callback modified cbArg, don't depend on cbArg.LastCall.
                    if (onLastCall)
                    {
                        tcs.SetResult(nextValue);
                    }
                    else
                    {
                        return Task.Delay(intervalMillis).ContinueWith(_ =>
                            StartPolling(startTime, nextValue, cb, intervalMillis,
                                totalDurationMillis, tcs));
                    }
                }
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            return Task.CompletedTask;
        }

        // begin implementing logical threading methods

        public Guid? CurrentLogicalThreadId
        {
            get
            {
                return _currentLogicalThreadId;
            }
        }

        public Guid? _CurrentLogicalThreadId
        {
            set
            {
                _currentLogicalThreadId = value;
            }
        }
    }

    public class DefaultPromise<T>: AbstractPromise<T>
    {
        public DefaultPromise(AbstractPromiseApi promiseApi, Task<T> task)
            : this(promiseApi, task, promiseApi.CurrentLogicalThreadId)
        { }

        private DefaultPromise(AbstractPromiseApi promiseApi, Task<T> task, 
            Guid? logicalThreadId)
        {
            _PromiseApi = promiseApi;
            WrappedTask = task;
            LogicalThreadId = logicalThreadId;
        }

        private DefaultPromise(_ILogicalThreadMember antecedent, Task<T> task) :
            this(antecedent._PromiseApi, task, antecedent.LogicalThreadId)
        { }

        public AbstractPromiseApi _PromiseApi { get; }

        public Task<T> WrappedTask { get; }

        public Guid? LogicalThreadId { get; }

        private void InsertCurrentLogicalThreadId()
        {
            _PromiseApi._CurrentLogicalThreadId = LogicalThreadId;
        }

        private void ClearCurrentLogicalThreadId()
        {
            _PromiseApi._CurrentLogicalThreadId = null;
        }

        public AbstractPromise<U> Then<U>(Func<T, U> onFulfilled)
        {
            return ThenCompose(result =>
            {
                var retResult = onFulfilled(result);
                return new DefaultPromise<U>(this, Task.FromResult(retResult));
            });
        }

        public AbstractPromise<T> Catch(Action<AggregateException> onRejected)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                InsertCurrentLogicalThreadId();
                try
                {
                    if (task.Status != TaskStatus.RanToCompletion)
                    {
                        onRejected.Invoke(DetermineTaskException(task));
                    }
                    return task;
                }
                finally
                {
                    ClearCurrentLogicalThreadId();
                }
            });
            return new DefaultPromise<T>(this, continuationTask.Unwrap());
        }

        public AbstractPromise<T> Finally(Action onFinally)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                InsertCurrentLogicalThreadId();
                try
                {
                    onFinally();
                    // return original task in its completed or faulted state.
                    return task;
                }
                finally
                {
                    ClearCurrentLogicalThreadId();
                }
            });
            return new DefaultPromise<T>(this, continuationTask.Unwrap());
        }

        public AbstractPromise<U> ThenCompose<U>(Func<T, AbstractPromise<U>> onFulfilled)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                InsertCurrentLogicalThreadId();
                try
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
                }
                finally
                {
                    ClearCurrentLogicalThreadId();
                }
            });
            return new DefaultPromise<U>(this, continuationTask.Unwrap());
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

                InsertCurrentLogicalThreadId();
                try
                {
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        return task;
                    }
                    else
                    {
                        AbstractPromise<T> continuationPromise = onRejected(DetermineTaskException(task));
                        return ((DefaultPromise<T>)continuationPromise).WrappedTask;
                    }
                }
                finally
                {
                    ClearCurrentLogicalThreadId();
                }
            });
            return new DefaultPromise<T>(this, continuationTask.Unwrap());
        }

        public AbstractPromise<U> ThenOrCatchCompose<U>(Func<T, AbstractPromise<U>> onFulfilled,
            Func<AggregateException, AbstractPromise<U>> onRejected)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                InsertCurrentLogicalThreadId();
                try
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
                }
                finally
                {
                    ClearCurrentLogicalThreadId();
                }
            });
            return new DefaultPromise<U>(this, continuationTask.Unwrap());
        }

        public AbstractPromise<T> StartLogicalThread(Guid newLogicalThreadId)
        {
            return new DefaultPromise<T>(_PromiseApi, WrappedTask, newLogicalThreadId);
        }

        public AbstractPromise<T> EndLogicalThread()
        {
            return new DefaultPromise<T>(_PromiseApi, WrappedTask, null);
        }

        public AbstractPromise<T> EndLogicalThread(Action onFinally)
        {
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                InsertCurrentLogicalThreadId();
                try
                {
                    onFinally();
                    // return original task in its completed or faulted state.
                    return task;
                }
                finally
                {
                    ClearCurrentLogicalThreadId();
                }
            });
            return new DefaultPromise<T>(_PromiseApi, continuationTask.Unwrap(), null);
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
    }

    public class DefaultPromiseCompletionSource<T> : PromiseCompletionSource<T>
    {
        private readonly AbstractEventLoopApi _completionEventLoop;

        public DefaultPromiseCompletionSource(AbstractPromiseApi promiseApi,
            AbstractEventLoopApi completionEventLoop)
        {
            // Event loop is optional so that promise completion sources
            // can be created independently of event loops.
            _completionEventLoop = completionEventLoop;

            WrappedSource = new TaskCompletionSource<T>();
            RelatedPromise = new DefaultPromise<T>(promiseApi, WrappedSource.Task);
        }

        public TaskCompletionSource<T> WrappedSource { get; }
        public AbstractPromise<T> RelatedPromise { get; }

        // Contract here is that both Complete* methods should execute after current
        // event in event loop has been processed.
        public void CompleteSuccessfully(T value)
        {
            if (_completionEventLoop != null)
            {
                _completionEventLoop.PostCallback(() => WrappedSource.TrySetResult(value));
            }
            else
            {
                WrappedSource.TrySetResult(value);
            }
        }

        public void CompleteExceptionally(Exception error)
        {
            if (_completionEventLoop != null)
            {
                _completionEventLoop.PostCallback(() => WrappedSource.TrySetException(error));
            }
            else
            {
                WrappedSource.TrySetException(error);
            }
        }
    }
}
