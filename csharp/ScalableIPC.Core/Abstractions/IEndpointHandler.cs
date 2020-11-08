using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface IEndpointHandler
    {
        AbstractNetworkApi NetworkSocket { get; set; }
        EndpointConfig EndpointConfig { get; set; }
        AbstractPromiseApi PromiseApi { get; }
        AbstractEventLoopApi EventLoop { get; }

        ProtocolDatagram ParseRawDatagram(byte[] rawBytes, int offset, int length);
        byte[] GenerateRawDatagram(ProtocolDatagram message);
        string GenerateSessionId();
        AbstractPromise<VoidType> OpenSession(IPEndPoint remoteEndpoint, string sessionId, ISessionHandler sessionHandler);
        void HandleReceive(IPEndPoint remoteEndpoint, byte[] rawBytes, int offset, int length);
        AbstractPromise<VoidType> Shutdown();

        // internal api
        void RemoveSessionHandler(IPEndPoint remoteEndpoint, string sessionId);
        AbstractPromise<VoidType> HandleSend(IPEndPoint remoteEndpoint, ProtocolDatagram message);
        AbstractPromise<VoidType> HandleException(AbstractPromise<VoidType> promise);

        AbstractPromise<VoidType> SwallowException(AbstractPromise<VoidType> promise);
        bool HandleReceiveProtocolControlMessage(IPEndPoint remoteEndpoint, ProtocolDatagram message);
    }
}
