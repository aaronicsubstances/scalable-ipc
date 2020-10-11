using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace PortableIPC.Core.Abstractions
{
    // Using this abstraction of a datagram socket allows for testing with fake sockets;
    // and also allows for implementing an encryption layer beneath PortableIPC protocol.
    public interface AbstractNetworkApi
    {
        AbstractPromise<VoidType> HandleSend(IPEndPoint endpoint, byte[] data, int offset, int length);
    }
}
