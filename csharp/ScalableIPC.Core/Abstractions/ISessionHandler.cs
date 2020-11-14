using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface ISessionHandler
    {
        // beginning of public API.
        void CompleteInit(string sessionId, bool configureForInitialSend,
            INetworkTransportInterface networkInterface, IPEndPoint remoteEndpoint);
        INetworkTransportInterface NetworkInterface { get; }
        IPEndPoint RemoteEndpoint { get; }
        AbstractEventLoopApi EventLoop { get; }
        string SessionId { get; }
        List<ISessionStateHandler> StateHandlers { get; }
        AbstractPromise<VoidType> ProcessReceive(ProtocolDatagram message);
        // Accepting single message instead of list due to possibility of message opcodes being
        // set up differently. Custom session state handler can handle that.
        AbstractPromise<VoidType> ProcessSend(ProtocolDatagram message);
        AbstractPromise<VoidType> ProcessSend(byte[] data, Dictionary<string, List<string>> options);
        AbstractPromise<VoidType> PartialShutdown();
        AbstractPromise<VoidType> Shutdown(Exception error, bool timeout);

        // beginning of internal API with state handlers.
        int SessionState { get; set; }

        // sesion parameters.
        int MaxReceiveWindowSize { get; set; }
        int MaxSendWindowSize { get; set; }
        int MaximumTransferUnitSize { get; set; }
        int MaxRetryCount { get; set; }
        int AckTimeoutSecs { get; set; }
        int IdleTimeoutSecs { get; set; }

        // Rules for window id changes are:
        //  - First window id must be 0. 
        //  - Receiver usually accepts only next ids larger than last received window id.
        //  - The only exception is that after 9E18, receiver must receive a next starting from 1
        //  - In any case increments must be less than one thousand (1000).
        // By so doing receiver can be conservative, and sender can have 
        // freedom in varying trend of window ids.
        long NextWindowIdToSend { get; set; }
        long LastWindowIdReceived { get; set; }
        int LastMaxSeqReceived { get; set; }
        void IncrementNextWindowIdToSend();
        bool IsSendInProgress();
        void PostIfNotClosed(Action cb);

        int? SessionIdleTimeoutSecs { get; set; }
        bool? SessionCloseReceiverOption { get; set; }

        void ResetIdleTimeout();

        void ResetAckTimeout(int timeoutSecs, Action cb);
        void CancelAckTimeout();
        void DiscardReceivedMessage(ProtocolDatagram message);
        void ProcessShutdown(Exception error, bool timeout);
        void Log(string logPosition, string message, params object[] args);
        void Log(string logPosition, ProtocolDatagram pdu, string message, params object[] args);

        // application layer interface. contract here is that these should be called from event loop.
        void OnDataReceived(byte[] data, Dictionary<string, List<string>> options);
        void OnClose(Exception error, bool timeout);
    }
}
