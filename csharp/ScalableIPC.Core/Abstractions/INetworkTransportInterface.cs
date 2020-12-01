using System;

namespace ScalableIPC.Core.Abstractions
{
    /// <summary>
    /// Abstracting underlying network allows us to separately target different networks such as
    /// 1. fake socket for testing
    /// 2. TCP/TLS, implemented as connection pooling.
    /// A key feature is automatic retry in some cases: when an error occurs on an existing connection,
    /// an attempt is immediately made to create a new one to replace it. This feature is the key to
    /// alleviating programmers from the pains of using custom protocols over TCP. 2 variants of this are:
    ///    A. this retry attempt is made for all pdus. Intended for use when there is no load balancing. In that
    ///       case few connections (e.g. 3) are enough.
    ///    B. this retry attempt is made only for the very first pdu of a session. Intended for load balancing in which number of
    ///       connections is about 3-5 times that of replica count.
    /// 3. Unix domain socket
    /// 4. Windows named pipe
    /// 5. datagram socket on localhost.
    /// 6. datagram socket and DTLS on the Internet. Futuristic and intended for gamers and others to implement.
    /// </summary>
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
