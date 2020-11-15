using System;

namespace ScalableIPC.Core.Abstractions
{
    public interface INetworkTransportInterface
    {
        AbstractPromiseApi PromiseApi { get; set; }
        AbstractEventLoopApi EventLoop { get; set; }
        GenericNetworkIdentifier LocalEndpoint { get; set; }
        int IdleTimeoutSecs { get; set; }
        int AckTimeoutSecs { get; set; }
        int MaxSendWindowSize { get; set; }
        int MaxReceiveWindowSize { get; set; }
        int MaxRetryCount { get; set; }
        int MaximumTransferUnitSize { get; set; }
        ISessionHandlerFactory SessionHandlerFactory { get; set; }
        AbstractPromise<VoidType> HandleReceive(GenericNetworkIdentifier remoteEndpoint,
             byte[] rawBytes, int offset, int length);
        AbstractPromise<VoidType> HandleSend(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram message);
        AbstractPromise<ISessionHandler> OpenSession(GenericNetworkIdentifier remoteEndpoint, string sessionId = null,
            ISessionHandler sessionHandler = null);
        void OnCloseSession(GenericNetworkIdentifier remoteEndpoint, string sessionId, Exception error, bool timeout);
        AbstractPromise<VoidType> CloseSession(GenericNetworkIdentifier remoteEndpoint, string sessionId,
            Exception error, bool timeout);
        AbstractPromise<VoidType> CloseSessions(GenericNetworkIdentifier remoteEndpoint);
        AbstractPromise<VoidType> Shutdown();
    }
}
