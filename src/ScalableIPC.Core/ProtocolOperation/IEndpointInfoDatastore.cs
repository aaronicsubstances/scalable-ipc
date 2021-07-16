using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.ProtocolOperation
{
    public interface IEndpointInfoDatastore
    {
        void Clear();
        void Update(GenericNetworkIdentifier remoteEndpoint, string endpointOwnerId);
        string Get(GenericNetworkIdentifier remoteEndpoint);
    }
}
