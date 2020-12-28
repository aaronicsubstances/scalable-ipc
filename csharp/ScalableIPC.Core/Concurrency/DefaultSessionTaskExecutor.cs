using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScalableIPC.Core.Concurrency
{
    public class DefaultSessionTaskExecutor : ISessionTaskExecutor
    {
        private readonly LimitedConcurrencyLevelTaskScheduler _throttledTaskScheduler;

        // limit parallelism to one to eliminate thread inteference errors.
        public DefaultSessionTaskExecutor():
            this(1)
        { }

        // for subclasses, to avoid creation of task scheduler.
        protected DefaultSessionTaskExecutor(int maxDegreeOfParallelism)
        {
            if (maxDegreeOfParallelism > 0)
            {
                _throttledTaskScheduler = new LimitedConcurrencyLevelTaskScheduler(maxDegreeOfParallelism);
            }
            else
            {
                _throttledTaskScheduler = null;
            }
        }

        public virtual void PostCallback(Action cb)
        {
            Task.Factory.StartNew(() => {
                try
                {
                    // Although parallelism is limited to 1 thread, more than 1 pool thread
                    // can still take turns to run task.
                    // So use lock to cater for memory consistency.
                    lock (this)
                    {
                        cb();
                    }
                }
                catch (Exception ex)
                {
                    CustomLoggerFacade.Log(() => new CustomLogEvent("5394ab18-fb91-4ea3-b07a-e9a1aa150dd6",
                        "Error occured on event loop", ex));
                }
            }, CancellationToken.None, TaskCreationOptions.None, _throttledTaskScheduler);
        }

        public virtual object ScheduleTimeout(int secs, Action cb)
        {
            var cts = new CancellationTokenSource();
            Task.Delay(TimeSpan.FromSeconds(secs), cts.Token).ContinueWith(t =>
            {
                Task.Factory.StartNew(() => {
                    try
                    {
                        // use lock to get equivalent of single threaded behaviour in terms of
                        // memory consistency.
                        lock (this)
                        {
                            cb();
                        }
                    }
                    catch (Exception ex)
                    {
                        CustomLoggerFacade.Log(() => new CustomLogEvent("6357ee7d-eb9c-461a-a4d6-7285bae06823",
                            "Error occured on event loop during timeout processing", ex));
                    }
                }, cts.Token, TaskCreationOptions.None, _throttledTaskScheduler);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            return cts;
        }

        public virtual void CancelTimeout(object id)
        {
            ((CancellationTokenSource)id).Cancel();
        }

        public virtual void RunTask(Action task)
        {
            Task.Run(task);
        }

        // the remaining methods make use of the above ones. Thus it is sufficient to modify the above methods
        // to modify the behaviour of the following ones.

        public void PostTask(Action cb)
        {
            PostCallback(() => RunTask(cb));
        }

        // Contract here is that both Complete* methods should behave like notifications, and
        // hence these should be called from outside event loop if possible, but after current
        // event in event loop has been processed.
        public void CompletePromiseCallbackSuccessfully<T>(PromiseCompletionSource<T> promiseCb, T value)
        {
            PostTask(() => ((DefaultPromiseCompletionSource<T>)promiseCb).WrappedSource.TrySetResult(value));
        }
        public void CompletePromiseCallbackExceptionally<T>(PromiseCompletionSource<T> promiseCb, Exception error)
        {
            PostTask(() => ((DefaultPromiseCompletionSource<T>) promiseCb).WrappedSource.TrySetException(error));
        }
    }
}
