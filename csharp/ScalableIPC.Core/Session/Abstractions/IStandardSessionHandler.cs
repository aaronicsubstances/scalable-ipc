using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session.Abstractions
{
    public interface IStandardSessionHandler: ISessionHandler
    {
        // Intended to enable testing. Maps to similarly-named classes (w/o 'I' prefix) in production usage.
        AbstractEventLoopApi CreateEventLoop();
        ISendHandlerAssistant CreateSendHandlerAssistant();
        IRetrySendHandlerAssistant CreateRetrySendHandlerAssistant();
        IReceiveHandlerAssistant CreateReceiveHandlerAssistant(); 
        IFireAndForgetSendHandlerAssistant CreateFireAndForgetSendHandlerAssistant();

        int SessionState { get; set; }

        // Rules for window id changes are:
        //  - First window id must be 0. 
        //  - Receiver usually accepts only next ids larger than last received window id.
        //  - The only exception is that after 9E18, receiver must receive a next starting from 1
        //  - In any case increments must be less than one thousand (1000).
        // By so doing receiver can be conservative, and sender can have 
        // freedom in varying trend of window ids.
        long NextWindowIdToSend { get; set; }
        long LastWindowIdReceived { get; set; }
        ProtocolDatagram LastAck { get; set; }
        void IncrementNextWindowIdToSend();
        bool IsSendInProgress();

        int? RemoteIdleTimeout { get; set; }
        int? RemoteMaxWindowSize { get; set; } // non-positive means ignore it.

        void ResetIdleTimeout();

        void ResetAckTimeout(int timeout, Action cb);
        void CancelAckTimeout();
        void ContinueDispose(ProtocolOperationException cause);

        // event loop method for use by session state handlers
        void PostEventLoopCallback(Action cb);

        // application layer interface. contract here is that these should be scheduled on event loop.
        Action<ISessionHandler, ProtocolDatagram> DatagramDiscardedHandler { get; set; }
        Action<ISessionHandler, ProtocolMessage> MessageReceivedHandler { get; set; }
        Action<ISessionHandler, ProtocolOperationException> SessionDisposingHandler { get; set; }
        Action<ISessionHandler, ProtocolOperationException> SessionDisposedHandler { get; set; }
        Action<ISessionHandler, ProtocolOperationException> ReceiveErrorHandler { get; set; }
        Action<ISessionHandler, ProtocolOperationException> SendErrorHandler { get; set; }
        Action<ISessionHandler> IdleTimeoutHandler { get; set; }
        void OnDatagramDiscarded(ProtocolDatagram datagram);
        void OnMessageReceived(ProtocolMessage message);
        void OnSessionDisposing(ProtocolOperationException cause);
        void OnSessionDisposed(ProtocolOperationException cause);
        void OnSendError(ProtocolOperationException error);
        void OnReceiveError(ProtocolOperationException error);
        void OnIdleTimeout();
    }
}
