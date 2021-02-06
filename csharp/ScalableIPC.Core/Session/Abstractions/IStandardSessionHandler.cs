using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session.Abstractions
{
    /// <summary>
    /// Meant to house methods and properties constituting implementation internals of standard
    /// session handler.
    /// </summary>
    public interface IStandardSessionHandler: ISessionHandler
    {
        // Intended to enable testing. Maps to similarly-named classes (w/o 'I' prefix) in production usage.
        AbstractEventLoopApi CreateEventLoop();
        ISendHandlerAssistant CreateSendHandlerAssistant();
        IRetrySendHandlerAssistant CreateRetrySendHandlerAssistant();
        IReceiveHandlerAssistant CreateReceiveHandlerAssistant();

        int SessionState { get; set; }

        // Rules for window id changes are:
        //  - Receiver usually accepts only next ids larger than last received window id.
        //  - The only exception is that after 9E18, receiver must receive a next starting from 0
        //  - In any case increments must be less than one thousand (1000).
        // By so doing receiver can be conservative, and sender can have 
        // freedom in varying trend of window ids.
        long NextWindowIdToSend { get; set; }
        long LastWindowIdReceived { get; set; }
        ProtocolDatagram LastAck { get; set; }
        void IncrementNextWindowIdToSend();
        bool IsSendInProgress();
        void EnsureSendNotInProgress();

        int? RemoteIdleTimeout { get; set; }
        int? RemoteMaxWindowSize { get; set; } // non-positive means ignore it.

        void ResetIdleTimeout();
        void ScheduleEnquireLinkEvent(bool reset);
        void ResetAckTimeout(int timeout, Action cb);
        void CancelAckTimeout();
        void InitiateDispose(ProtocolOperationException cause, PromiseCompletionSource<VoidType> promiseCb);
        void InitiateDisposeBypassingSendClose(ProtocolOperationException cause);

        // event loop method for use by session state handlers
        void PostEventLoopCallback(Action cb, PromiseCompletionSource<VoidType> promisesCb);

        // application layer interface.
        void OnDatagramDiscarded(ProtocolDatagram datagram);
        void OnOpenSuccess();
        void OnMessageReceived(ProtocolMessage message);
        void OnSessionDisposing(ProtocolOperationException cause);
        void OnSessionDisposed(ProtocolOperationException cause);
        void OnSendError(ProtocolOperationException error);
        void OnReceiveError(ProtocolOperationException error);
        void OnEnquireLinkTimerFired();
        void OnEnquireLinkSuccess(ProtocolDatagram datagram);
    }
}
