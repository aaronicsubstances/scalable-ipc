using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session.Abstractions
{
    public interface ISessionStateHandler
    {
        bool SendInProgress { get; }
        void PrepareForDispose(SessionDisposedException cause);
        void Dispose(SessionDisposedException cause);
        bool ProcessReceive(ProtocolDatagram datagram);
        bool ProcessSend(ProtocolMessage message, PromiseCompletionSource<VoidType> promiseCb);
        bool ProcessSendWithoutAck(ProtocolMessage message, PromiseCompletionSource<bool> promiseCb);
    }
}