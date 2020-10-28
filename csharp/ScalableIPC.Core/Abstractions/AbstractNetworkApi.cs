using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    /// <summary>
    /// Abstracting underlying network allows us to separately target different networks such as
    /// 1. datagram socket on localhost.
    /// 2. TCP/TLS
    /// 3. fake socket for testing
    /// 4. datagram socket and DTLS on the Internet (futuristic)
    /// </summary>
    public interface AbstractNetworkApi
    {
        AbstractPromise<VoidType> HandleSend(IPEndPoint remoteEndpoint, byte[] data, int offset, int length);
    }
}
