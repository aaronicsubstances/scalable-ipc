using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface ScalableIpcProtocolListener
    {
        void OnMessageReceived(GenericNetworkIdentifier remoteEndpoint,
            ProtocolMessage msg);
        void OnProcessingError(string message, Exception ex);
    }
}
