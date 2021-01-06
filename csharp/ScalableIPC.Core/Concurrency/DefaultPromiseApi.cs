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
        private static Dictionary<DefaultPromiseApi, string> _currentLogicalThreadIds;

        private readonly Dictionary<string, List<ILogicalThreadMember>> _logicalThreads;

        public DefaultPromiseApi()
        {
            _logicalThreads = new Dictionary<string, List<ILogicalThreadMember>>();
        }

        public PromiseCompletionSource<T> CreateCallback<T>(ISessionTaskExecutor sessionTaskExecutor)
        {
            return new DefaultPromiseCompletionSource<T>(this, sessionTaskExecutor);
        }

        public AbstractPromise<T> Reject<T>(Exception reason)
        {
            return new DefaultPromise<T>(this, Task.FromException<T>(reason));
        }

        public AbstractPromise<T> Resolve<T>(T value)
        {
            return new DefaultPromise<T>(this, Task.FromResult(value));
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

        public AbstractPromise<string> StartNewLogicalThread()
        {
            var newLogicalThreadId = Guid.NewGuid().ToString("n");
            lock (this)
            {
                _logicalThreads.Add(newLogicalThreadId, new List<ILogicalThreadMember>());
            }
            var firstLogicalThreadMember = new DefaultPromise<string>(this,
                Task.FromResult(newLogicalThreadId), newLogicalThreadId, CurrentLogicalThreadId);
            return firstLogicalThreadMember;
        }

        public void AddToCurrentLogicalThread(ILogicalThreadMember newMember)
        {
            var currentLogicalThreadId = CurrentLogicalThreadId;
            if (currentLogicalThreadId != null)
            {
                lock (this)
                {
                    var logicalThread = _logicalThreads[currentLogicalThreadId];
                    var firstMember = logicalThread[0];
                    newMember.LogicalThreadId = firstMember.LogicalThreadId;
                    newMember.ParentLogicalThreadId = firstMember.ParentLogicalThreadId;
                    logicalThread.Add(newMember);
                }
            }
        }

        public void AddLogicalThreadMember(string logicalThreadId, ILogicalThreadMember newMember)
        {
            if (logicalThreadId != null)
            {
                lock (this)
                {
                    if (_logicalThreads.ContainsKey(logicalThreadId))
                    {
                        var logicalThread = _logicalThreads[logicalThreadId];
                        logicalThread.Add(newMember);
                    }
                    else
                    {
                        newMember.Terminated = true;
                    }
                }
            }
        }

        public string CurrentLogicalThreadId
        {
            get
            {
                if (_currentLogicalThreadIds != null && _currentLogicalThreadIds.ContainsKey(this))
                {
                    return _currentLogicalThreadIds[this];
                }
                else
                {
                    return null;
                }
            }
            set
            {
                if (_currentLogicalThreadIds == null)
                {
                    _currentLogicalThreadIds = new Dictionary<DefaultPromiseApi, string>();
                }
                if (_currentLogicalThreadIds.ContainsKey(this))
                {
                    _currentLogicalThreadIds[this] = value;
                }
                else
                {
                    _currentLogicalThreadIds.Add(this, value);
                }
            }
        }
        
        public void TerminateCurrentLogicalThread()
        {
            Terminate(CurrentLogicalThreadId);
        }

        public void Terminate(string logicalThreadId)
        {
            if (logicalThreadId == null)
            {
                return;
            }
            lock (this)
            {
                if (_logicalThreads.ContainsKey(logicalThreadId))
                {
                    var logicalThreadItems = _logicalThreads[logicalThreadId];
                    _logicalThreads.Remove(logicalThreadId);
                    foreach (var item in logicalThreadItems)
                    {
                        item.Terminated = true;
                    }
                }
            }
        }
    }

    public class DefaultPromise<T>: AbstractPromise<T>
    {
        private bool _terminated;
        private readonly object _terminatedLock = new object();

        public DefaultPromise(AbstractPromiseApi promiseApi, Task<T> task)
        {
            PromiseApi = promiseApi;
            WrappedTask = task;
            PromiseApi.AddToCurrentLogicalThread(this);
        }

        internal DefaultPromise(AbstractPromiseApi promiseApi, Task<T> task, 
            string logicalThreadId, string parentLogicalThreadId)
        {
            PromiseApi = promiseApi;
            WrappedTask = task;
            LogicalThreadId = logicalThreadId;
            ParentLogicalThreadId = parentLogicalThreadId;
            PromiseApi.AddLogicalThreadMember(logicalThreadId, this);
        }

        private DefaultPromise(ILogicalThreadMember antecedent, Task<T> task) :
            this(antecedent.PromiseApi, task, antecedent.LogicalThreadId, 
                antecedent.ParentLogicalThreadId)
        {
            Terminated = antecedent.Terminated;
        }

        public AbstractPromiseApi PromiseApi { get; }

        public Task<T> WrappedTask { get; }

        public string LogicalThreadId { get; set; }
        public string ParentLogicalThreadId { get; set; }

        public bool Terminated
        {
            get
            {
                lock (_terminatedLock)
                {
                    return _terminated;
                }
            }
            set
            {
                lock (_terminatedLock)
                {
                    _terminated = value;
                }
            }
        }

        public AbstractPromise<U> Then<U>(Func<T, U> onFulfilled)
        {
            if (Terminated)
            {
                return CreateTerminatedPromise<U>();
            }
            return ThenCompose(result =>
            {
                var retResult = onFulfilled(result);
                return new DefaultPromise<U>(this, Task.FromResult(retResult));
            });
        }

        public AbstractPromise<T> Catch(Action<AggregateException> onRejected)
        {
            if (Terminated)
            {
                return CreateTerminatedPromise<T>();
            }
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                PromiseApi.CurrentLogicalThreadId = LogicalThreadId;
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
                    PromiseApi.CurrentLogicalThreadId = null;
                }
            });
            return new DefaultPromise<T>(this, continuationTask.Unwrap());
        }

        public AbstractPromise<T> Finally(Action onFinally)
        {
            if (Terminated)
            {
                return CreateTerminatedPromise<T>();
            }
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                PromiseApi.CurrentLogicalThreadId = LogicalThreadId;
                try
                {
                    onFinally();
                    // return original task in its completed or faulted state.
                    return task;
                }
                finally
                {
                    PromiseApi.CurrentLogicalThreadId = null;
                }
            });
            return new DefaultPromise<T>(this, continuationTask.Unwrap());
        }

        public AbstractPromise<U> ThenCompose<U>(Func<T, AbstractPromise<U>> onFulfilled)
        {
            if (Terminated)
            {
                return CreateTerminatedPromise<U>();
            }
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                PromiseApi.CurrentLogicalThreadId = LogicalThreadId;
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
                    PromiseApi.CurrentLogicalThreadId = null;
                }
            });
            return new DefaultPromise<U>(this, continuationTask.Unwrap());
        }

        public AbstractPromise<T> CatchCompose(Func<AggregateException, AbstractPromise<T>> onRejected)
        {
            if (Terminated)
            {
                return CreateTerminatedPromise<T>();
            }
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                // Note on previous implementation attempt:
                // 1. This method was the first place where TaskContinuationOptions was observed to
                // be doing something different from what is required for its operation:
                // Using OnlyOnFaulted resulted in success results not being forwarded.

                // so rather get task and use its status to determine next steps.
                // in the end TaskContinuationOptions was abandoned entirely in this class,
                // and if else is rather used.

                PromiseApi.CurrentLogicalThreadId = LogicalThreadId;
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
                    PromiseApi.CurrentLogicalThreadId = null;
                }
            });
            return new DefaultPromise<T>(this, continuationTask.Unwrap());
        }

        public AbstractPromise<U> ThenOrCatchCompose<U>(Func<T, AbstractPromise<U>> onFulfilled,
            Func<AggregateException, AbstractPromise<U>> onRejected)
        {
            if (Terminated)
            {
                return CreateTerminatedPromise<U>();
            }
            var continuationTask = WrappedTask.ContinueWith(task =>
            {
                PromiseApi.CurrentLogicalThreadId = LogicalThreadId;
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
                    PromiseApi.CurrentLogicalThreadId = null;
                }
            });
            return new DefaultPromise<U>(this, continuationTask.Unwrap());
        }

        public void Terminate()
        {
            PromiseApi.Terminate(LogicalThreadId);
        }

        private AbstractPromise<U> CreateTerminatedPromise<U>()
        {
            var tcs = new TaskCompletionSource<U>();
            tcs.SetException(new LogicalThreadTerminatedException(tcs.Task));
            return new DefaultPromise<U>(this, tcs.Task);
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
        private readonly ISessionTaskExecutor _sessionTaskExecutor;

        public DefaultPromiseCompletionSource(AbstractPromiseApi promiseApi,
            ISessionTaskExecutor sessionTaskExecutor)
        {
            _sessionTaskExecutor = sessionTaskExecutor;
            WrappedSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            RelatedPromise = new DefaultPromise<T>(promiseApi, WrappedSource.Task);
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
