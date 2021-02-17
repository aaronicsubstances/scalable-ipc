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
        ISendWindowAssistant CreateSendWindowAssistant();
        ISendHandlerAssistant CreateSendHandlerAssistant();

        int State { get; set; }

        long NextWindowIdToSend { get; set; }
        long LastWindowIdReceived { get; set; }
        ProtocolDatagram LastAck { get; set; }
        bool OpeningByReceiving { get; set; }
        bool OpeningBySending { get; set; }
        void IncrementNextWindowIdToSend();
        bool IsSendInProgress();
        void EnsureSendNotInProgress();

        int? RemoteIdleTimeout { get; set; }
        int? RemoteMaxWindowSize { get; set; } // non-positive means ignore it.

        void CancelOpenTimeout();
        void ResetIdleTimeout();
        void ScheduleEnquireLinkEvent(bool reset);
        void ResetAckTimeout(int timeout, Action cb);
        void CancelAckTimeout();
        void InitiateDisposeGracefully(ProtocolOperationException cause, PromiseCompletionSource<VoidType> promiseCb);
        void InitiateDispose(ProtocolOperationException cause);

        // event loop method for use by session state handlers
        void PostEventLoopCallback(Action cb, PromiseCompletionSource<VoidType> promisesCb);

        // application layer interface.
        void OnDatagramDiscarded(ProtocolDatagram datagram);
        void OnOpenSuccess(bool onReceive);
        void OnMessageReceived(ReceivedProtocolMessage message);
        void OnSessionDisposing(ProtocolOperationException cause);
        void OnSessionDisposed(ProtocolOperationException cause);
        void OnSendError(ProtocolOperationException error);
        void OnReceiveError(ProtocolOperationException error);
        void OnEnquireLinkTimerFired();
        void OnEnquireLinkSuccess(ProtocolDatagram datagram);
    }
}
