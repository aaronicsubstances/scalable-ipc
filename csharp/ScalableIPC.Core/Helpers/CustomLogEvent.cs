using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core.Helpers
{
    public class CustomLogEvent
    {
        public CustomLogEvent(string logPosition, string message, Exception ex)
        {
            LogPosition = logPosition;
            Message = message;
            Error = ex;
        }

        public string LogPosition { get; set; }
        public string Message { get; set; }
        public Exception Error { get; set; }
        public IDictionary<string, object> Data { get; set; }

        internal void FillData(string key, object value)
        {
            if (Data == null)
            {
                Data = new Dictionary<string, object>();
            }
            Data.Add(key, value);
        }

        internal void FillData(object[] args)
        {
            for (int i = 0; i < args.Length; i += 2)
            {
                var key = (string)args[i];
                var value = args[i + 1];
                if (Data == null)
                {
                    Data = new Dictionary<string, object>();
                }
                // pick last of duplicate keys.
                if (!Data.ContainsKey(key))
                {
                    Data.Add(key, value);
                }
                else
                {
                    Data[key] = value;
                }
            }
        }
    }
}
