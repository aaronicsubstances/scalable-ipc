using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Concurrency
{
    public class TestSessionTaskExecutor: ISessionTaskExecutor
    {
        public class TaskDescriptor
        {
            public static int _idCounter = 0;

            public TaskDescriptor(Action cb, long time)
            {
                Id = _idCounter++;
                Callback = cb;
                ScheduledAt = time;
            }

            public long Id { get; }

            public Action Callback { get; }

            public long ScheduledAt { get; }
        }

        private readonly List<TaskDescriptor> _taskQueue = new List<TaskDescriptor>();

        public TestSessionTaskExecutor(long initialTimestamp)
        {
            if (initialTimestamp < 0)
            {
                throw new ArgumentException("cannot be negative", nameof(initialTimestamp));
            }
            CurrentTimestamp = initialTimestamp;
        }

        public void AdvanceTimeBy(long delayTimeMillis)
        {
            if (delayTimeMillis < 0)
            {
                throw new ArgumentException("cannot be negative", nameof(delayTimeMillis));
            }
            CurrentTimestamp += delayTimeMillis;
            TriggerActions();
        }

        public long CurrentTimestamp { get; private set; }

        private void TriggerActions()
        {
            // invoke task queue actions starting with head of queue
            // and stop if item's time is in the future.
            while (_taskQueue.Count > 0)
            {
                var firstTask = _taskQueue[0];
                if (firstTask.ScheduledAt > CurrentTimestamp)
                {
                    break;
                }
                _taskQueue.RemoveAt(0);
                firstTask.Callback.Invoke();
            }
        }

        public void PostCallback(Action cb)
        {
            var taskDescriptor = new TaskDescriptor(cb, 0);
            _taskQueue.Add(taskDescriptor);
            _taskQueue.Sort((x, y) => x.Id.CompareTo(y.Id));
        }

        public object ScheduleTimeout(int secs, Action cb)
        {
            if (secs < 0)
            {
                throw new ArgumentException("cannot be negative", nameof(secs));
            }
            var taskDescriptor = new TaskDescriptor(cb, secs * 1000L);
            _taskQueue.Add(taskDescriptor);
            _taskQueue.Sort((x, y) => x.Id.CompareTo(y.Id));
            return taskDescriptor.Id;
        }

        public void CancelTimeout(object id)
        {
            int indexToRemove = -1;
            for (int i = 0; i < _taskQueue.Count; i++)
            {
                if (Equals(_taskQueue[i].Id, id))
                {
                    indexToRemove = i;
                    break;
                }
            }
            if (indexToRemove != -1)
            {
                _taskQueue.RemoveAt(indexToRemove);
            }
        }

        public void RunTask(Action task)
        {
            // run immediately.
            task.Invoke();
        }

        // the remaining methods make use of the above ones. Thus it is sufficient to modify the above methods
        // to modify the behaviour of the following ones.

        public void PostTask(Action cb)
        {
            PostCallback(() => RunTask(cb));
        }

        public void CompletePromiseCallbackSuccessfully<T>(PromiseCompletionSource<T> promiseCb, T value)
        {
            PostTask(() => ((DefaultPromiseCompletionSource<T>)promiseCb).WrappedSource.TrySetResult(value));
        }

        public void CompletePromiseCallbackExceptionally<T>(PromiseCompletionSource<T> promiseCb, Exception error)
        {
            PostTask(() => ((DefaultPromiseCompletionSource<T>)promiseCb).WrappedSource.TrySetException(error));
        }
    }
}
