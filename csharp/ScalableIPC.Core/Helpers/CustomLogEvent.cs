using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core.Helpers
{
    public class CustomLogEvent
    {
        public static readonly string LogDataKeyLogPositionId = "logPositionId";
        public static readonly string LogDataKeyCurrentLogicalThreadId = "currentLogicalThreadId";
        public static readonly string LogDataKeyEndingLogicalThreadId = "endingLogicalThreadId";
        public static readonly string LogDataKeyNewLogicalThreadId = "newLogicalThreadId";

        public CustomLogEvent()
        { }

        public CustomLogEvent(string message)
        {
            Message = message;
        }

        public CustomLogEvent(string message, Exception ex)
        {
            Message = message;
            Error = ex;
        }

        public string Message { get; set; }
        public Exception Error { get; set; }
        public IDictionary<string, object> Data { get; set; }

        public CustomLogEvent AddData(string key, object value)
        {
            if (Data == null)
            {
                Data = new Dictionary<string, object>();
            }
            Data.Add(key, value);
            return this;
        }
    }
}
