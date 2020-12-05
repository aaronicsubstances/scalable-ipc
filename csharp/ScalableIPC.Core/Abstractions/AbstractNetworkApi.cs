using ScalableIPC.Core.Session;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    /// <summary>
    /// Abstracting underlying network allows us to separately target different networks such as
    /// 1. fake socket for testing
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
        AbstractPromise<ISessionHandler> OpenSessionAsync(GenericNetworkIdentifier remoteEndpoint, string sessionId,
            ISessionHandler sessionHandler);
        AbstractPromise<VoidType> HandleSendAsync(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram message);
        AbstractPromise<VoidType> InitiateSessionDisposeAsync(GenericNetworkIdentifier remoteEndpoint, string sessionId);
    }
}
