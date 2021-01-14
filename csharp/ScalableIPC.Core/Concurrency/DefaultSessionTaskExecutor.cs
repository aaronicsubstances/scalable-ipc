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

        // Even when degree of parallelism is limited to 1, more than 1 pool thread
        // can still take turns to process callbacks.
        // So use lock to guarantee memory consistency (and also to definitely eliminate thread interference errors
        // in the case where degree of parallelism is more than 1).

        // locking also has another useful side-effect in combination with throttled task scheduler's task queue:
        // it guarantees that callbacks posted during processing of a given callback will only get executed after 
        // the current processing is finished, even when degree of parallelism is more than 1.
        private readonly bool _runCallbacksUnderMutex;

        // limit parallelism to one to guarantee that callbacks posted from same thread
        // are executed in order of submission.
        public DefaultSessionTaskExecutor():
            this(1, true)
        { }

        // for subclasses, to avoid creation of task scheduler if not needed, or use more degrees of
        // parallelism (e.g. for testing).
        protected internal DefaultSessionTaskExecutor(int maxDegreeOfParallelism, bool runCallbacksUnderMutex)
        {
            _runCallbacksUnderMutex = runCallbacksUnderMutex;
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
                    if (_runCallbacksUnderMutex)
                    {
                        lock (this)
                        {
                            cb();
                        }
                    }
                    else
                    {
                        cb();
                    }
                }
                catch (Exception ex)
                {
                    HandleCallbackException(ex);
                }
            }, CancellationToken.None, TaskCreationOptions.None, _throttledTaskScheduler);
        }

        public virtual void HandleCallbackException(Exception ex)
        {
            CustomLoggerFacade.Log(() => new CustomLogEvent(GetType(),
                    "Error occured during session task executor callback processing", ex)
                .AddProperty(CustomLogEvent.LogDataKeyLogPositionId, "5394ab18-fb91-4ea3-b07a-e9a1aa150dd6"));
        }

        public virtual object ScheduleTimeout(int millis, Action cb)
        {
            var cts = new CancellationTokenSource();
            Task.Delay(millis, cts.Token).ContinueWith(t =>
            {
                Task.Factory.StartNew(() => {
                    try
                    {
                        if (_runCallbacksUnderMutex)
                        {
                            lock (this)
                            {
                                cb();
                            }
                        }
                        else
                        {
                            cb();
                        }
                    }
                    catch (Exception ex)
                    {
                        HandleCallbackException(ex);
                    }
                }, cts.Token, TaskCreationOptions.None, _throttledTaskScheduler);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            return cts;
        }

        public virtual void CancelTimeout(object id)
        {
            if (id is CancellationTokenSource source)
            {
                source.Cancel();
            }
        }
    }
}
