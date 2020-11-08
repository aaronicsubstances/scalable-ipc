using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface ISessionHandlerFactory
    {
        ISessionHandler Create(string sessionId, bool configureForInitialSend, IEndpointHandler endpointHandler,
            IPEndPoint remoteEndpoint);
    }
}
