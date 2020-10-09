using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace PortableIPC.Core.Abstractions
{
    public interface IEndpointHandler
    {
        AbstractNetworkApi NetworkSocket { get; }
        AbstractEventLoopApi EventLoop { get; }
        AbstractPromiseApi PromiseApi { get; }
        EndpointConfig EndpointConfig { get; }
        ProtocolDatagram ParseRawDatagram(byte[] rawBytes, int offset, int length);
        AbstractPromise<VoidType> OpenSession(IPEndPoint endpoint, ISessionHandler sessionHandler,
            ProtocolDatagram message);
        AbstractPromise<VoidType> HandleReceive(IPEndPoint endpoint, byte[] rawBytes, int offset, int length);
        AbstractPromise<VoidType> Shutdown();

        // internal api
        void RemoveSessionHandler(IPEndPoint endpoint, string sessionId);
        AbstractPromise<VoidType> HandleSend(IPEndPoint endpoint, ProtocolDatagram message);
        AbstractPromise<VoidType> HandleException(AbstractPromise<VoidType> promise);

        AbstractPromise<VoidType> SwallowException(AbstractPromise<VoidType> promise);
        AbstractPromise<VoidType> HandleReceiveProtocolControlMessage(IPEndPoint endpoint, ProtocolDatagram message);
    }
}
