using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface TransportApiCallbacks
    {
        void BeginReceive(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram pdu);
    }
}
