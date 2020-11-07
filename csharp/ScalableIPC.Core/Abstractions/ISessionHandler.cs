using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface ISessionHandler
    {
        // beginning of public API.
        IEndpointHandler EndpointHandler { get; set; }
        IPEndPoint RemoteEndpoint { get; set; }
        string SessionId { get; set; }
        List<ISessionStateHandler> StateHandlers { get; }
        void ProcessReceive(ProtocolDatagram message);
        AbstractPromise<VoidType> ProcessSend(ProtocolDatagram message);
        AbstractPromise<VoidType> ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options);
        AbstractPromise<VoidType> Shutdown(Exception error, bool timeout);

        // beginning of internal API with state handlers.
        int SessionState { get; set; }
        AbstractEventLoopApi EventLoop { get; set; }

        // sesion parameters.
        int MaxReceiveWindowSize { get; set; }
        int MaxSendWindowSize { get; set; }
        int MaximumTransferUnitSize { get; set; }
        int MaxRetryCount { get; set; }
        int AckTimeoutSecs { get; set; }
        int IdleTimeoutSecs { get; set; }

        // Rules for window id changes are:
        //  - Receiver usually accepts only next ids larger than last received window id.
        //  - The only exception is that receiver can receive a next of 0, provided 
        //    last received id is larger than 0.
        // By so doing receiver can be conservative, and sender can have 
        // freedom in varying trend of window ids.
        long NextWindowIdToSend { get; set; }
        long LastWindowIdReceived { get; set; }
        int LastMaxSeqReceived { get; set; }
        void IncrementNextWindowIdToSend();
        bool IsSendInProgress();
        void PostIfNotClosed(Action cb);

        // timeout api assumes only 1 timeout can be outstanding at any time.
        // setting a timeout clears previous timeout.
        int? SessionIdleTimeoutSecs { get; set; }

        void EnsureIdleTimeout();
        void ResetIdleTimeout();

        void ResetAckTimeout(int timeoutSecs, Action cb);
        void DiscardReceivedMessage(ProtocolDatagram message);
        void ProcessShutdown(Exception error, bool timeout);
        void Log(string logPosition, string message, params object[] args);
        void Log(string logPosition, ProtocolDatagram pdu, string message, params object[] args);

        // application layer interface. contract here is that these should be called from event loop.
        void OnOpenRequest(byte[] data, Dictionary<string, List<string>> options, bool isLastOpenRequest);
        void OnDataReceived(byte[] data, Dictionary<string, List<string>> options);
        void OnClose(Exception error, bool timeout);
    }
}
