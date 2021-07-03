using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScalableIPC.Core.Concurrency
{
    /// <summary>
    /// Provides single thread implementation of event loop, and is the event loop implementation to use
    /// in production for the single thread event-driven framework employed by the standard transport processor.
    /// The required constraints on this event loop implementation in summary are that it should be equivalent to
    /// single-threaded program execution of tasks in a queue. In particular,
    ///  1. Tasks should be run serially, ie one at a time. Even if there are multiple threads, there must be only ONE
    ///     degree of parallelism. If a running task schedules another task, that task must be guaranteed to execute
    ///     after the current one is done running.
    ///  2. Side-effects of executed tasks must be visible to tasks which will be run later.
    /// </summary>
    public class DefaultEventLoopApi : EventLoopApi
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

        public long CurrentTimestamp => DateTimeUtils.UnixTimeMillis;

        public void PostCallback(Action cb)
        {
            PostCallback(cb, CancellationToken.None);
        }

        private void PostCallback(Action cb, CancellationToken cancellationToken)
        {
            Task.Factory.StartNew(() => {
                lock (this)
                {
                    cb();
                }
            }, cancellationToken, TaskCreationOptions.None, _throttledTaskScheduler);
        }

        public object ScheduleTimeout(int millis, Action cb)
        {
            var cts = new CancellationTokenSource();
            Task.Delay(millis, cts.Token).ContinueWith(t =>
            {
                PostCallback(cb, cts.Token);
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
