using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core.Abstractions
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

        internal void FillData(ProtocolDatagram datagram)
        {
            if (datagram == null)
            {
                return;
            }
            if (Data == null)
            {
                Data = new Dictionary<string, object>();
            }
            Data.Add("datagram.windowId", datagram.WindowId);
            Data.Add("datagram.seqNr", datagram.SequenceNumber);
            Data.Add("datagram.opCode", datagram.OpCode);
            Data.Add("datagram.idleTimeout", datagram.Options?.IdleTimeoutSecs);
            Data.Add("datagram.lastInWindow", datagram.Options?.IsLastInWindow);
            Data.Add("datagram.windowFull", datagram.Options?.IsWindowFull);
            Data.Add("datagram.traceId", datagram.Options?.TraceId);
        }
    }
}
