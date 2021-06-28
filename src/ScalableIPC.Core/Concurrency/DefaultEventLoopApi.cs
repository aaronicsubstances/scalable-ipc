using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScalableIPC.Core.Concurrency
{
    public class DefaultEventLoopApi : AbstractEventLoopApi
    {
        private readonly LimitedConcurrencyLevelTaskScheduler _throttledTaskScheduler;

        // Even when degree of parallelism is limited to 1, more than 1 pool thread
        // can still take turns to process callbacks.
        // So use lock to guarantee memory consistency (and also to definitely eliminate thread interference errors
        // in the case where degree of parallelism is more than 1).

        // locking also has another interesting side-effect in combination with throttled task scheduler's task queue:
        // it guarantees that callbacks posted during processing of a given callback will only get executed after 
        // the current processing is finished, even when degree of parallelism is more than 1.
        // Unfortunately it is still not useful in production by itself without limit parallelism to 1 degree,
        // as posted callbacks aren't guaranteed to execute in order after current processing finishes.

        // Therefore limit parallelism to one to guarantee that callbacks posted from same thread
        // are executed within mutex lock in same order as that of original submission.
        public DefaultEventLoopApi()
        {
            _throttledTaskScheduler = new LimitedConcurrencyLevelTaskScheduler(1);
        }

        public void PostCallback(Action cb)
        {
            Task.Factory.StartNew(() => {
                lock (this)
                {
                    cb();
                }
            }, CancellationToken.None, TaskCreationOptions.None, _throttledTaskScheduler);
        }

        public object ScheduleTimeout(int millis, Action cb)
        {
            var cts = new CancellationTokenSource();
            Task.Delay(millis, cts.Token).ContinueWith(t =>
            {
                Task.Factory.StartNew(() => {
                    lock (this)
                    {
                        cb();
                    }
                }, cts.Token, TaskCreationOptions.None, _throttledTaskScheduler);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            return cts;
        }

        public void CancelTimeout(object id)
        {
            if (id is CancellationTokenSource source)
            {
                source.Cancel();
            }
        }
    }
}
