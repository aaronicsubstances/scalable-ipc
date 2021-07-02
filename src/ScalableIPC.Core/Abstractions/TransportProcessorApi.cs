using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface TransportProcessorApi
    {
        void BeginReceive(GenericNetworkIdentifier remoteEndpoint,
            byte[] data, int offset, int length, Action<ProtocolOperationException> cb);
    }
}
