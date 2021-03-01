using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session.Abstractions
{
    public interface ISendHandlerAssistant
    {
        List<ProtocolDatagram> ProspectiveWindowToSend { get; set; }
        Action SuccessCallback { get; set; }
        Action TimeoutCallback { get; set; }
        Action<ProtocolOperationException> ErrorCallback { get; set; }
        List<ProtocolDatagram> CurrentWindow { get; }
        List<int> ActualSentWindowRanges { get; }
        int TotalSentCount { get; }
        int RetryCount { get; }
        bool IsStarted { get; }
        bool IsComplete { get; }
        void Start();
        void OnAckReceived(ProtocolDatagram datagram);
        void Cancel();
    }
}
