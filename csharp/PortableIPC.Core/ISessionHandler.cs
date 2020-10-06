using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace PortableIPC.Core
{
    public interface ISessionHandler
    {
        // beginning of public API.
        ProtocolEndpointHandler EndpointHandler { get; set; }
        IPEndPoint ConnectedEndpoint { get; set; }
        string SessionId { get; set; }
        AbstractPromise<VoidType> ProcessSend(ProtocolDatagram message);
        AbstractPromise<VoidType> ProcessSendData(byte[] rawData);
        AbstractPromise<VoidType> ProcessReceive(ProtocolDatagram message);
        AbstractPromise<VoidType> Close(Exception error, bool timeout);
        AbstractPromise<VoidType> ProcessErrorReceive();

        // beginning of internal API with state handlers.
        bool IsClosed { get; }
        int MaxPduSize { get; set; }
        int MaxRetryCount { get; set; }
        int WindowSize { get; set; }
        int IdleTimeoutSecs { get; set; }
        int AckTimeoutSecs { get; set; }
        U RunSerially<T, U>(T arg, Func<T, U> cb);
        void RunStateCallbackSerially<T>(IStoredCallback<T> cb);
        void ResetIdleTimeout();

        void ResetAckTimeout<T>(int timeoutSecs, IStoredCallback<T> cb);
        AbstractPromise<VoidType> HandleClosing(Exception error, bool timeout);
    }
}
