﻿using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface TransportApiCallbacks
    {
        void BeginReceive(GenericNetworkIdentifier remoteEndpoint,
            byte[] data, int offset, int length);
        void BeginReceive(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram pdu);
    }
}
