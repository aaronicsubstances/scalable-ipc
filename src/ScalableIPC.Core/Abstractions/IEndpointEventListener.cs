using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface IEndpointEventListener
    {
        void OnMessageReceived(GenericNetworkIdentifier remoteEndpoint, string messageId,  byte[] data);
    }
}
