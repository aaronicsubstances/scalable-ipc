using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Concurrency
{
    public class TestEventLoopApi: AbstractEventLoopApi
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

        public bool RunImmediateCallbacksWithoutAdvance { get; set; } = false;

        public void AdvanceTimeBy(long delay)
        {
            if (delay < 0)
            {
                throw new ArgumentException("cannot be negative", nameof(delay));
            }
            CurrentTimestamp += delay;
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
            ScheduleTimeout(0, cb);
        }

        protected internal static void StableSort(List<TaskDescriptor> list)
        {
            list.Sort((x, y) => x.ScheduledAt.CompareTo(y.ScheduledAt));
        }

        public object ScheduleTimeout(int millis, Action cb)
        {
            if (millis < 0)
            {
                throw new ArgumentException("cannot be negative", nameof(millis));
            }
            // run immediately if so configured.
            if (millis == 0 && RunImmediateCallbacksWithoutAdvance)
            {
                cb.Invoke();
                return null;
            }
            var taskDescriptor = new TaskDescriptor(cb, CurrentTimestamp + millis);
            _taskQueue.Add(taskDescriptor);
            StableSort(_taskQueue);
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
