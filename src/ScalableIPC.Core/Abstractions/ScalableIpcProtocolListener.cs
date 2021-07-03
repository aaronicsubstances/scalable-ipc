using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface ScalableIpcProtocolListener
    {
        void OnMessageReceived(GenericNetworkIdentifier remoteEndpoint,
            string messageId,  byte[] data, int offset, int length);
    }
}
