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
        ISessionTaskExecutorGroup SessionTaskExecutorGroup { get; set; }
        AbstractPromise<VoidType> StartAsync();
        AbstractPromise<ISessionHandler> OpenSessionAsync(GenericNetworkIdentifier remoteEndpoint, string sessionId,
            ISessionHandler sessionHandler);

        // this separation between RequestSend and HandleSendAsync is for the purpose of
        // launching HandleSendAsync in a separate thread of control.
        // HandleSendAsync returns ack timeout to cb arg of RequestSend.
        Guid RequestSend(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram datagram, Action<int, Exception> cb);
        AbstractPromise<int> _HandleSendAsync(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram datagram);
        
        // similar to send case, this separation between RequestSessionDispose and DisposeSessionAsync is
        // required so DisposeSessionAsync can be called in a separate thread of control, and then
        // RequestSessionDipose can return for session handlers to update their internal state prior to final
        // disposal.
        Guid RequestSessionDispose(GenericNetworkIdentifier remoteEndpoint, string sessionId, SessionDisposedException cause);
        AbstractPromise<VoidType> _DisposeSessionAsync(GenericNetworkIdentifier remoteEndpoint, string sessionId,
            SessionDisposedException cause);

        AbstractPromise<VoidType> ShutdownAsync(int gracefulWaitPeriodSecs);
        bool IsShuttingDown();
        void _StartNewThreadOfControl(Func<AbstractPromise<VoidType>> cb);
        int AckTimeout { get; set; }
    }
}
