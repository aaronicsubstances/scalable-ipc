using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface INetworkTransportInterface
    {
        AbstractPromiseApi PromiseApi { get; set; }
        AbstractEventLoopApi EventLoop { get; set; }
        IPEndPoint LocalEndpoint { get; set; }
        int IdleTimeoutSecs { get; set; }
        int AckTimeoutSecs { get; set; }
        int MaxSendWindowSize { get; set; }
        int MaxReceiveWindowSize { get; set; }
        int MaxRetryCount { get; set; }
        int MaximumTransferUnitSize { get; set; }
        ISessionHandlerFactory SessionHandlerFactory { get; set; }
        AbstractPromise<VoidType> HandleReceive(IPEndPoint remoteEndpoint,
             byte[] rawBytes, int offset, int length);
        AbstractPromise<VoidType> HandleSend(IPEndPoint remoteEndpoint, ProtocolDatagram message);
        AbstractPromise<ISessionHandler> OpenSession(IPEndPoint remoteEndpoint, string sessionId = null,
            ISessionHandler sessionHandler = null);
        void OnCloseSession(IPEndPoint remoteEndpoint, string sessionId, Exception error, bool timeout);
        AbstractPromise<VoidType> CloseSession(IPEndPoint remoteEndpoint, string sessionId,
            Exception error, bool timeout);
        AbstractPromise<VoidType> CloseSessions(IPEndPoint remoteEndpoint);
        AbstractPromise<VoidType> Shutdown();
    }
}
