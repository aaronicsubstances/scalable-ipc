using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session.Abstractions
{
    public interface IRetrySendHandlerAssistant
    {
        List<ProtocolDatagram> CurrentWindow { get; set; }
        Action SuccessCallback { get; set; }
        Action<SessionDisposedException> DisposeCallback { get; set; }
        int TotalSentCount { get; set; }
        int RetryCount { get; }
        void Start();
        void OnAckReceived(ProtocolDatagram datagram);
        void Cancel();
    }
}
