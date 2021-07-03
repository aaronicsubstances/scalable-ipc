using System;

namespace ScalableIPC.Core.Abstractions
{
    /// <summary>
    /// The event loop abstraction is key to providing a library API which can be implemented without
    /// presuming the use of any concurrency or I/O programming model. In particular, it supports 3 models:
    /// 1. blocking I/O and multi-threaded
    /// 2. non-blocking I/O and single-threaded
    /// 3. non-blocking I/O and multi-threaded
    /// The required constraints on an event loop implementation in summary are that it should be execute tasks 
    /// asynchronously and provide async timeouts. In particular,
    ///  1. Caller submitting tasks should not be blocked during task execution.
    ///  2. Provide timeouts asynchronously without using dedicated timer thread.
    ///  3. Supply current time. This enables use of virtual times for testing.
    /// </summary>
    public interface EventLoopApi
    {
        long CurrentTimestamp { get; }
        void PostCallback(Action cb);
        object ScheduleTimeout(int millis, Action cb);
        void CancelTimeout(object id);
    }
}