using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session.Abstractions
{
    /// <summary>
    /// Implementation is equivalent to behaviour of selective repeat w/o NACKs on the side of
    /// receiver.
    /// </summary>
    public interface IReceiveHandlerAssistant
    {
        List<ProtocolDatagram> CurrentWindow { get; }
        List<ProtocolDatagram> CurrentWindowGroup { get; }

        Func<List<ProtocolDatagram>, bool> SuccessCallback { get; set; }
        Action<SessionDisposedException> DisposeCallback { get; set; }
        bool IsComplete { get; }
        void Cancel();
        void OnReceive(ProtocolDatagram datagram);
    }
}
