using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public class CustomLogEvent
    {
        public const string PduOptionTraceId = "sx_traceId";

        public CustomLogEvent(string logPosition, ProtocolDatagram pdu, string message, params object[] args)
        {
            LogPosition = logPosition;
            Message = message;
            FillData(pdu?.SessionId);
            FillData(pdu);
            FillData(args);
        }

        public CustomLogEvent(string logPosition, string sessionId, string message, params object[] args)
        {
            LogPosition = logPosition;
            Message = message;
            FillData(sessionId);
            FillData(args);
        }

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

        private void FillData(string sessionId)
        {
            if (sessionId == null)
            {
                return;
            }

            if (Data == null)
            {
                Data = new Dictionary<string, object>();
            }
            Data.Add("sessionId", sessionId);
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

        internal void FillData(ProtocolDatagram pdu)
        {
            if (pdu == null)
            {
                return;
            }
            if (Data == null)
            {
                Data = new Dictionary<string, object>();
            }
            Data.Add("pdu.windowId", pdu.WindowId);
            Data.Add("pdu.seqNr", pdu.SequenceNumber);
            Data.Add("pdu.opCode", pdu.OpCode);
            Data.Add("pdu.idleTimeout", pdu.IdleTimeoutSecs);
            Data.Add("pdu.lastInWindow", pdu.IsLastInWindow);
            Data.Add("pdu.lastOpenRequest", pdu.IsLastOpenRequest);
            Data.Add("pdu.windowFull", pdu.IsWindowFull);
            string traceId = null;
            if (pdu.Options != null && pdu.Options.ContainsKey(PduOptionTraceId))
            {
                traceId = pdu.Options[PduOptionTraceId].FirstOrDefault();
            }
            Data.Add("pdu.traceId", traceId);
        }
    }
}
