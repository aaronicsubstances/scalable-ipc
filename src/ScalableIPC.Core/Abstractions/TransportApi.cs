using ScalableIPC.Core.ErrorHandling;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    /// <summary>
    /// Abstracting underlying transport allows us to separately target different transports such as
    /// 1. TCP/TLS. A key feature is automatic retries: when an error occurs on an existing connection,
    ///  an attempt is made after a short while to create a new one to replace it. This feature is the key to
    ///  alleviating programmers from the pains of using custom protocols over TCP.
    /// 2. UDP on localhost.
    /// 3. In-memory transport for testing and potentially for in-process communications.
    /// 4. Unix domain socket
    /// 5. Windows named pipe
    /// </summary>
    public interface TransportApi
    {
        GenericNetworkIdentifier LocalEndpoint { get; set; }
        TransportApiCallbacks Callbacks { get; set; }
        void BeginSend(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram pdu, Action<ProtocolException> cb);
    }
}
