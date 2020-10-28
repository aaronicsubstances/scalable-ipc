using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    /// <summary>
    /// The event loop abstraction is key to providing a library API which can be implemented without
    /// presuming the use of any concurrency or I/O programming model. In particular, it supports 3 models:
    /// 1. blocking I/O and multi-threaded
    /// 2. non-blocking I/O and single-threaded
    /// 3. non-blocking I/O and multi-threaded
    /// The only thing presumed however, is that the event loop should run in a single thread.
    /// For multi-threaded environments, an additional requirement is that the rest of the application 
    /// cannot share in using the event loop thread.
    /// </summary>
    public interface AbstractEventLoopApi
    {
        void PostCallback(ISessionHandler sessionHandler, Action cb);
        void PostCallbackSerially(ISessionHandler sessionHandler, Action cb);
        object ScheduleTimeoutSerially(ISessionHandler sessionHandler, long millis, Action cb);
        void CancelTimeout(object id);
    }
}
