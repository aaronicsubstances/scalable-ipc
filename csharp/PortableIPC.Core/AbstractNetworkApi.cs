using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace PortableIPC.Core
{
    public interface AbstractNetworkApi
    {
        AbstractPromise<VoidType> HandleSend(IPEndPoint endpoint, byte[] data, int offset, int length);
    }
}
