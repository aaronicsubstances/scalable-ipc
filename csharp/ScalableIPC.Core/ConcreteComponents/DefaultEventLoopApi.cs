using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScalableIPC.Core.ConcreteComponents
{
    public class DefaultEventLoopApi : AbstractEventLoopApi
    {
        public DefaultEventLoopApi()
        {
            SingleThreadTaskScheduler = new LimitedConcurrencyLevelTaskScheduler(1);
        }

        public LimitedConcurrencyLevelTaskScheduler SingleThreadTaskScheduler { get; }

        public void PostCallback(Action cb)
        {
            Task.Factory.StartNew(cb, CancellationToken.None, TaskCreationOptions.None, SingleThreadTaskScheduler);
        }

        public object ScheduleTimeout(int secs, Action cb)
        {
            var cts = new CancellationTokenSource();
            Task.Delay(TimeSpan.FromSeconds(secs), cts.Token).ContinueWith(t =>
            {
                Task.Factory.StartNew(cb, cts.Token, TaskCreationOptions.None, SingleThreadTaskScheduler);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            return cts;
        }

        public void CancelTimeout(object id)
        {
            ((CancellationTokenSource) id).Cancel();
        }
    }
}
