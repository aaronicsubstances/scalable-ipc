using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public interface ISessionStateHandler
    {
        bool SendInProgress { get; }
        void PrepareForDispose(SessionDisposedException cause);
        void Dispose(SessionDisposedException cause);
        bool ProcessReceive(ProtocolDatagram message);
        bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb);
    }
}