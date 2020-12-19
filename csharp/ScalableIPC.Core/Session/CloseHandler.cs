using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class CloseHandler : ISessionStateHandler
    {
        private readonly IReferenceSessionHandler _sessionHandler;
        private readonly List<PromiseCompletionSource<VoidType>> _pendingPromiseCallbacks;

        public CloseHandler(IReferenceSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
            _pendingPromiseCallbacks = new List<PromiseCompletionSource<VoidType>>();
        }

        public bool SendInProgress { get; set; }

        public void PrepareForDispose(SessionDisposedException cause)
        {
            // nothing to do
        }

        public void Dispose(SessionDisposedException cause)
        {
            foreach (var cb in _pendingPromiseCallbacks)
            {
                _sessionHandler.TaskExecutor.CompletePromiseCallbackSuccessfully(cb, VoidType.Instance);
            }
            _pendingPromiseCallbacks.Clear();
        }

        public void QueueCallback(PromiseCompletionSource<VoidType> promiseCb)
        {
            _pendingPromiseCallbacks.Add(promiseCb);
        }

        public bool ProcessReceive(ProtocolDatagram datagram)
        {
            if (datagram.OpCode != ProtocolDatagram.OpCodeClose)
            {
                return false;
            }

            _sessionHandler.Log("7b79fdc3-e704-4dba-8e92-1621b78c4e18", datagram,
                "Datagram received for processing in close handler");
            ProcessReceiveClose(datagram);
            return true;
        }

        public bool ProcessSend(ProtocolMessage message, PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        private void ProcessReceiveClose(ProtocolDatagram datagram)
        {
            if (datagram.Options?.AbortCode == null || datagram.Options?.AbortCode == ProtocolDatagram.AbortCodeNormalClose)
            {
                // validate window id for normal close.
                if (datagram.SequenceNumber != 0 || !ProtocolDatagram.IsReceivedWindowIdValid(datagram.WindowId,
                    _sessionHandler.LastWindowIdReceived))
                {
                    _sessionHandler.Log("2cf44189-3193-4c45-a433-0aa1b077a484", datagram,
                        "Rejecting close datagram with invalid window id or sequence number");
                    _sessionHandler.DiscardReceivedDatagram(datagram);
                    return;
                }
            }

            var error = new SessionDisposedException(true, datagram.Options?.AbortCode ?? ProtocolDatagram.AbortCodeNormalClose);
            _sessionHandler.ContinueDispose(error);
        }

        public void ProcessSendClose(SessionDisposedException cause, PromiseCompletionSource<VoidType> promiseCb)
        {            
            _sessionHandler.Log("89d4c052-a99a-4e49-9116-9c80553ec594", "Send Close request accepted for processing in close handler",
                "cause", cause);

            // promiseCb may be null if timeout triggered.
            if (promiseCb != null)
            {
                _pendingPromiseCallbacks.Add(promiseCb);
            }

            // send but ignore errors.
            var closeDatagram = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeClose,
                SessionId = _sessionHandler.SessionId,
                WindowId = _sessionHandler.NextWindowIdToSend
            };
            if (cause.AbortCode != ProtocolDatagram.AbortCodeNormalClose)
            {
                closeDatagram.Options = new ProtocolDatagramOptions
                {
                    AbortCode = cause.AbortCode
                };
            }
            _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint, closeDatagram,
                _ => HandleSendSuccessOrError(cause));
            SendInProgress = true;
        }

        private void HandleSendSuccessOrError(SessionDisposedException cause)
        {
            _sessionHandler.TaskExecutor.PostCallback(() =>
            {
                SendInProgress = false;

                _sessionHandler.Log("63a2eff5-d376-44c9-8d98-fd752f4a0c7b", 
                    "Continuing after sending closing datagram");

                _sessionHandler.ContinueDispose(cause);
            });
        }
    }
}
