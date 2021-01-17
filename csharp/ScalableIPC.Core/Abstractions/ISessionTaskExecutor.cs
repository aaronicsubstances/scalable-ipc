using System;

namespace ScalableIPC.Core.Abstractions
{
    /// <summary>
    /// The event loop abstraction is key to providing a library API which can be implemented without
    /// presuming the use of any concurrency or I/O programming model. In particular, it supports 3 models:
    /// 1. blocking I/O and multi-threaded
    /// 2. non-blocking I/O and single-threaded
    /// 3. non-blocking I/O and multi-threaded
    /// The required constraints on an event loop implementation in summary are that it should be equivalent to
    /// single-threaded program execution of tasks in a queue, and provide async timeouts. In particular,
    ///  1. Tasks should be run serially, ie one at a time. Even if there are multiple threads, there must be only ONE
    ///     degree of parallelism. If a running task schedules another task, that task must be guaranteed to execute
    ///     after the current one is done running.
    ///  2. Side-effects of executed tasks must be visible to tasks which will be run later.
    ///  3. Provide timeouts asynchronously without using dedicated timer thread.
    /// </summary>
    public interface ISessionTaskExecutor
    {
        string SessionId { get;  }

        // these are the event loop operations.
        void PostCallback(Action cb);
        object ScheduleTimeout(int millis, Action cb);
        void CancelTimeout(object id);
    }

    public interface ISessionTaskExecutorGroup
    {
        bool ConfirmAddWorker(Action notificationListener);
        void OnWorkerFinished();
    }
}