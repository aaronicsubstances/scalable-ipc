using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace PortableIPC.Core.Abstractions
{
    public interface ISessionHandler
    {
        // beginning of public API.
        IEndpointHandler EndpointHandler { get; set; }
        IPEndPoint ConnectedEndpoint { get; set; }
        string SessionId { get; set; }
        List<ISessionStateHandler> StateHandlers { get; }
        void ProcessReceive(ProtocolDatagram message);
        AbstractPromise<VoidType> ProcessSend(ProtocolDatagram message);
        AbstractPromise<VoidType> ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options);
        AbstractPromise<VoidType> Shutdown(Exception error, bool timeout);

        // beginning of internal API with state handlers.
        int MaxPduSize { get; set; }
        int MaxRetryCount { get; set; }
        int DataWindowSize { get; set; }
        int IdleTimeoutSecs { get; set; }
        int AckTimeoutSecs { get; set; }
        SessionState SessionState { get; set; }
        int LastMinSeqReceived { get; set; }
        int LastMaxSeqReceived { get; set; }
        int NextSendSeqStart { get; set; }
        void PostSerially(Action cb);
        void PostSeriallyIfNotClosed(Action cb);

        void EnsureIdleTimeout();
        void ResetIdleTimeout();

        void ResetAckTimeout(int timeoutSecs, Action cb);
        void DiscardReceivedMessage(ProtocolDatagram message);
        void ProcessShutdown(Exception error, bool timeout);

        // application layer interface. contract here is that these should be called from event loop.
        void OnOpenRequest(byte[] data, Dictionary<string, List<string>> options);
        void OnDataReceived(byte[] data, Dictionary<string, List<string>> options);
        void OnClose(Exception error, bool timeout);
    }
}
