using ScalableIPC.Core.Session;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    /// <summary>
    /// Abstracting underlying network allows us to separately target different networks such as
    /// 1. in-memory socket for testing and potentially for in-process communications.
    /// 2. multiplexed TCP/TLS. A key feature is automatic retry of one attempt: when an error occurs on an existing connection,
    ///  an attempt is immediately made to create a new one to replace it. This feature is the key to
    ///  alleviating programmers from the pains of using custom protocols over TCP.
    /// 3. Unix domain socket
    /// 4. Windows named pipe
    /// 5. datagram socket on localhost. Intended as fallback if domain socket or named pipe cannot be used.
    /// 6. datagram socket and DTLS on the Internet. Futuristic and intended for gamers and others to implement.
    /// </summary>
    public interface AbstractNetworkApi
    {
        GenericNetworkIdentifier LocalEndpoint { get; set; }
        AbstractPromiseApi PromiseApi { get; set; }
        ISessionHandlerFactory SessionHandlerFactory { get; set; }
        AbstractEventLoopGroupApi SessionTaskExecutorGroup { get; set; }
        int MaximumTransferUnitSize { get; set; } // bounded between 512 and datagram max size.

        // this method is used during sending to give network api implementations total
        // control in determininig ack timeouts.
        INetworkSendContext CreateSendContext();
        AbstractPromise<VoidType> StartAsync();

        AbstractPromise<ISessionHandler> OpenSessionAsync(GenericNetworkIdentifier remoteEndpoint, string sessionId,
            ISessionHandler sessionHandler);

        // Contract here is that Request* methods should launch actual operations in separate thread of
        // control.

        Guid RequestSend(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram datagram,
            INetworkSendContext sendContext, Action<Exception> cb);
        Guid RequestSendToSelf(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram datagram);
        Guid RequestSessionDispose(GenericNetworkIdentifier remoteEndpoint, string sessionId,
            ProtocolOperationException cause);
        
        AbstractPromise<VoidType> ShutdownAsync(int gracefulWaitPeriodSecs);
        bool IsShuttingDown();
        void _StartNewThreadOfControl(Func<AbstractPromise<VoidType>> cb);
    }

    public interface INetworkSendContext
    {
        int SessionState { get; set; }
        int RetryCount { get; set; }
        int DetermineAckTimeout();
        void Dispose();
    }
}
