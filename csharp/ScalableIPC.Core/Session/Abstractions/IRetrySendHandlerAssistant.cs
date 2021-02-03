using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session.Abstractions
{
    public interface IRetrySendHandlerAssistant
    {
        List<ProtocolDatagram> CurrentWindow { get; set; }
        Action SuccessCallback { get; set; }
        Action<ProtocolOperationException> ErrorCallback { get; set; }
        int TotalSentCount { get; }
        int RetryCount { get; }
        bool IsComplete { get; }
        void Start();
        void OnAckReceived(ProtocolDatagram datagram);
        void Cancel();
    }
}
