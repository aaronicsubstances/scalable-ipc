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
    /// Event loop implementation which employs multithreading and allows concurrent task execution.
    /// </summary>
    public class UnsynchronizedEventLoopApi : EventLoopApi
    {
        public long CurrentTimestamp => DateTimeUtils.UnixTimeMillis;

        public void PostCallback(Action cb)
        {
            Task.Run(cb);
        }

        public object ScheduleTimeout(int millis, Action cb)
        {
            var cts = new CancellationTokenSource();
            Task.Delay(millis, cts.Token).ContinueWith(t =>
            {
                cb();
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
