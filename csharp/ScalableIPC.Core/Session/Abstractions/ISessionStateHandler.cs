using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session.Abstractions
{
    public interface ISessionStateHandler
    {
        bool SendInProgress { get; }
        void Dispose(ProtocolOperationException cause);
        bool ProcessReceive(ProtocolDatagram datagram);
    }
}