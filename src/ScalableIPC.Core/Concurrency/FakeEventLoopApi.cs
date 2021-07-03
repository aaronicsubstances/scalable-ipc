using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Concurrency
{
    /// <summary>
    /// Event loop implementation which doesn't use real time.
    /// Useful for testing main components of protocol implementation.
    /// </summary>
    public class FakeEventLoopApi: EventLoopApi
    {
        public class TaskDescriptor
        {
            public TaskDescriptor(Action cb, long time) :
                this(Guid.NewGuid(), cb, time)
            { }

            protected internal TaskDescriptor(Guid id, Action cb, long time)
            {
                Id = id;
                Callback = cb;
                ScheduledAt = time;
            }

            public Guid Id { get; }

            public Action Callback { get; }

            public long ScheduledAt { get; }

            public override bool Equals(object obj)
            {
                return obj is TaskDescriptor descriptor &&
                       Id == descriptor.Id &&
                       EqualityComparer<Action>.Default.Equals(Callback, descriptor.Callback) &&
                       ScheduledAt == descriptor.ScheduledAt;
            }

            public override int GetHashCode()
            {
                int hashCode = 547303379;
                hashCode = hashCode * -1521134295 + Id.GetHashCode();
                hashCode = hashCode * -1521134295 + EqualityComparer<Action>.Default.GetHashCode(Callback);
                hashCode = hashCode * -1521134295 + ScheduledAt.GetHashCode();
                return hashCode;
            }
        }

        private readonly List<TaskDescriptor> _taskQueue = new List<TaskDescriptor>();

        public void AdvanceTimeBy(long delay)
        {
            if (delay < 0)
            {
                throw new ArgumentException("cannot be negative", nameof(delay));
            }
            AdvanceTimeTo(CurrentTimestamp + delay);
        }

        public void AdvanceTimeTo(long newTimestamp)
        {
            if (newTimestamp < 0)
            {
                throw new ArgumentException("cannot be negative", nameof(newTimestamp));
            }
            TriggerActions(newTimestamp);
            CurrentTimestamp = newTimestamp;
        }

        public void AdvanceTimeIndefinitely()
        {
            TriggerActions(-1);
        }

        public long CurrentTimestamp { get; private set; }

        private void TriggerActions(long stoppageTimestamp)
        {
            // invoke task queue actions starting with head of queue
            // and stop if item's time is in the future.
            while (_taskQueue.Count > 0)
            {
                var firstTask = _taskQueue[0];
                if (stoppageTimestamp >= 0 && firstTask.ScheduledAt > stoppageTimestamp)
                {
                    break;
                }
                _taskQueue.RemoveAt(0);
                CurrentTimestamp = firstTask.ScheduledAt;
                firstTask.Callback.Invoke();
            }
        }

        public void PostCallback(Action cb)
        {
            ScheduleTimeout(0, cb);
        }

        public object ScheduleTimeout(int millis, Action cb)
        {
            if (millis < 0)
            {
                throw new ArgumentException("cannot be negative", nameof(millis));
            }
            var taskDescriptor = new TaskDescriptor(cb, CurrentTimestamp + millis);
            _taskQueue.Add(taskDescriptor);
            
            // stable sort
            _taskQueue.Sort((x, y) => x.ScheduledAt.CompareTo(y.ScheduledAt));

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
    }
}
