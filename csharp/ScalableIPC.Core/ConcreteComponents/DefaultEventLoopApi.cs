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
            // limit parallelism to one to eliminate thread inteference errors.
            SingleThreadTaskScheduler = new LimitedConcurrencyLevelTaskScheduler(1);
        }

        public LimitedConcurrencyLevelTaskScheduler SingleThreadTaskScheduler { get; }

        public void PostCallback(Action cb)
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
            }, CancellationToken.None, TaskCreationOptions.None, SingleThreadTaskScheduler);
        }

        public object ScheduleTimeout(int secs, Action cb)
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
                }, cts.Token, TaskCreationOptions.None, SingleThreadTaskScheduler);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            return cts;
        }

        public void CancelTimeout(object id)
        {
            ((CancellationTokenSource) id).Cancel();
        }
    }
}
