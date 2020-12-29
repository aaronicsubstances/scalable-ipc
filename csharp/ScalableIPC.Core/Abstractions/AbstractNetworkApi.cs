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
        ISessionTaskExecutor SessionTaskExecutor { get; set; }
        int IdleTimeoutSecs { get; set; } // non-positive means disable idle timer 
        int MinRemoteIdleTimeoutSecs { get; set; }
        int MaxRemoteIdleTimeoutSecs { get; set; }
        int AckTimeoutSecs { get; set; } // non-positive means disable ack timer
        int MaxSendWindowSize { get; set; } // non-positive means use 1.
        int MaxReceiveWindowSize { get; set; } // non-positive means use 1.
        int MaxRetryCount { get; set; } // non-positive means disable retries.
        int MaximumTransferUnitSize { get; set; } // bounded between 512 and datagram max size.
        ISessionHandlerFactory SessionHandlerFactory { get; set; }
        AbstractPromise<VoidType> StartAsync();
        AbstractPromise<ISessionHandler> OpenSessionAsync(GenericNetworkIdentifier remoteEndpoint, string sessionId,
            ISessionHandler sessionHandler);
        void RequestSend(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram datagram, Action<Exception> cb);
        AbstractPromise<VoidType> HandleSendAsync(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram datagram);
        void RequestSessionDispose(GenericNetworkIdentifier remoteEndpoint, string sessionId, SessionDisposedException cause);
        AbstractPromise<VoidType> DisposeSessionAsync(GenericNetworkIdentifier remoteEndpoint, string sessionId,
            SessionDisposedException cause);
        AbstractPromise<VoidType> ShutdownAsync(int waitSecs);
        bool IsShuttingDown();
    }
}
