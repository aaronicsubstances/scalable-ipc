using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace PortableIPC.Core.Abstractions
{
    // Abstracting underlying network allows us to separately target different networks such as
    // 1. datagram socket on localhost.
    // 2. datgram socket and DTLS on the Internet
    // 3. TCP/TLS tunnel
    // 4. fake socket for testing
    public interface AbstractNetworkApi
    {
        AbstractPromise<VoidType> HandleSend(IPEndPoint endpoint, byte[] data, int offset, int length);
    }
}
