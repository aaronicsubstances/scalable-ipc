using ScalableIPC.Core.Session;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Abstractions
{
    public interface ISessionHandler
    {
        // beginning of public API.
        void CompleteInit(string sessionId, bool configureForInitialSend,
            INetworkTransportInterface networkInterface, GenericNetworkIdentifier remoteEndpoint);
        INetworkTransportInterface NetworkInterface { get; }
        GenericNetworkIdentifier RemoteEndpoint { get; }
        AbstractEventLoopApi EventLoop { get; }
        string SessionId { get; }
        List<ISessionStateHandler> StateHandlers { get; }
        AbstractPromise<VoidType> ProcessReceiveAsync(ProtocolDatagram message);
        AbstractPromise<VoidType> ProcessSendAsync(ProtocolDatagram message);
        AbstractPromise<VoidType> CloseAsync();
        AbstractPromise<VoidType> CloseAsync(bool skipSendClose);
        AbstractPromise<VoidType> ShutdownInputAsync();
        AbstractPromise<bool> IsInputShutdownAsync();
        AbstractPromise<VoidType> ShutdownOutputAsync();
        AbstractPromise<bool> IsOutputShutdownAsync();
        AbstractPromise<int> GetSessionStateAsync();

        // beginning of internal API with state handlers.
        int SessionState { get; set; }
        bool IsInputShutdown();

        // session parameters.
        int MaxReceiveWindowSize { get; set; }
        int MaxSendWindowSize { get; set; }
        int MaximumTransferUnitSize { get; set; }
        int MaxRetryCount { get; set; }
        int AckTimeoutSecs { get; set; }
        int IdleTimeoutSecs { get; set; }
        int MinRemoteIdleTimeoutSecs { get; set; }
        int MaxRemoteIdleTimeoutSecs { get; set; }

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

        int? RemoteIdleTimeoutSecs { get; set; }

        void ResetIdleTimeout();

        void ResetAckTimeout(int timeoutSecs, Action cb);
        void CancelAckTimeout();
        void DiscardReceivedMessage(ProtocolDatagram message);
        void InitiateClose(SessionCloseException cause);
        AbstractPromise<VoidType> FinaliseCloseAsync(SessionCloseException cause);
        void Log(string logPosition, string message, params object[] args);
        void Log(string logPosition, ProtocolDatagram pdu, string message, params object[] args);

        // application layer interface. contract here is that these should be called from event loop.
        event EventHandler<MessageReceivedEventArgs> MessageReceived;
        event EventHandler<SessionClosedEventArgs> SessionClosed;
        void OnMessageReceived(MessageReceivedEventArgs e);
        void OnSessionClosed(SessionClosedEventArgs e);
    }
}
