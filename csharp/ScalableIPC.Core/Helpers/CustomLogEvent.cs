using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Helpers
{
    public class CustomLogEvent
    {
        public static readonly string LogDataKeyLogPositionId = "logPositionId";
        public static readonly string LogDataKeyCurrentLogicalThreadId = "currentLogicalThreadId";
        public static readonly string LogDataKeyEndingLogicalThreadId = "endingLogicalThreadId";
        public static readonly string LogDataKeyNewLogicalThreadId = "newLogicalThreadId";
        public static readonly string LogDataKeySessionId = "sessionId";
        public static readonly string LogDataKeySessionTaskExecutionId = "sessionTaskExecutionId";
        public static readonly string LogDataKeyEndingSessionTaskExecutionId = "endingSessionTaskExecutionId";
        public static readonly string LogDataKeyNewSessionTaskId = "newSessionTaskExecutionId";
        public static readonly string ThrottledTaskSchedulerId = "throttledTaskSchedulerId";
        public static readonly string ThrottledTaskSchedulerConcurrencyLevel = "throttledTaskSchedulerConcurrencyLevel";

        public CustomLogEvent(Type targetLogger)
            : this(targetLogger,null, null)
        { }

        public CustomLogEvent(Type targetLogger, string message)
            : this(targetLogger, message, null)
        { }

        public CustomLogEvent(Type targetLogger, string message, Exception error)
        {
            Message = message;
            Error = error;
            TargetLogger = targetLogger?.FullName;
        }

        public string Message { get; set; }
        public List<object> Arguments { get; set; }
        public Exception Error { get; set; }
        public object Data { get; set; }
        public string TargetLogger { get; set; }

        public CustomLogEvent AddProperty(string name, object value)
        {
            if (Data == null)
            {
                Data = new Dictionary<string, object>();
            }
            ((IDictionary<string, object>)Data).Add(name, value);
            return this;
        }
    }
}
