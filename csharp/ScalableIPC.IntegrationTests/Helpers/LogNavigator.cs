using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.IntegrationTests.Helpers
{
    public class LogNavigator<T>
    {
        private readonly IList<T> _logs;

        public LogNavigator(IList<T> logs)
        {
            _logs = logs;
        }

        public int NextIndex { get; private set; } = 0;

        public bool HasNext()
        {
            return NextIndex < _logs.Count;
        }

        public T Next()
        {
            return _logs[NextIndex++];
        }

        public T Next(Predicate<T> searchCondition)
        {
            return Next(searchCondition, null);
        }

        public T Next(Predicate<T> searchCondition, Predicate<T> stopCondition)
        {
            if (searchCondition == null)
            {
                throw new ArgumentNullException(nameof(searchCondition));
            }
            int stopIndex = _logs.Count;
            if (stopCondition != null)
            {
                for (int i = NextIndex; i < _logs.Count; i++)
                {
                    if (stopCondition.Invoke(_logs[i]))
                    {
                        stopIndex = i;
                        break;
                    }
                }
            }
            for (int i = NextIndex; i < stopIndex; i++)
            {
                T log = _logs[i];
                if (searchCondition.Invoke(log))
                {
                    NextIndex = i + 1;
                    return log;
                }
            }
            return default;
        }
    }
}
