using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public class SendOpenHandler : ISessionStateHandler
    {
        private readonly IStandardSessionHandler _sessionHandler;
        private readonly IRetrySendHandlerAssistant _sendWindowHandler;
        private PromiseCompletionSource<VoidType> _pendingPromiseCallback;

        public SendOpenHandler(DefaultSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;

            _sendWindowHandler = _sessionHandler.CreateRetrySendHandlerAssistant();
            _sendWindowHandler.SuccessCallback = OnSendSuccess;
            _sendWindowHandler.ErrorCallback = OnSendError;

            var openDatagram = new ProtocolDatagram
            {
                SessionId = _sessionHandler.SessionId,
                OpCode = ProtocolDatagram.OpCodeOpen,
                Options = new ProtocolDatagramOptions
                {
                    IdleTimeout = _sessionHandler.IdleTimeout
                }
            };
            _sendWindowHandler.CurrentWindow = new List<ProtocolDatagram> { openDatagram };
        }

        public bool SendInProgress { get; set; }

        public void PrepareForDispose(ProtocolOperationException cause)
        {
            Dispose(cause);
        }

        public void Dispose(ProtocolOperationException cause)
        {
            _sendWindowHandler.Cancel();
            SendInProgress = false;
            _pendingPromiseCallback?.CompleteExceptionally(cause);
            _pendingPromiseCallback = null;
        }

        public bool ProcessReceive(ProtocolDatagram datagram)
        {
            if (datagram.OpCode != ProtocolDatagram.OpCodeOpenAck)
            {
                return false;
            }

            // to prevent clashes with other handlers performing sends, 
            // check that specific send in progress is on.
            if (!SendInProgress)
            {
                return false;
            }

            _sendWindowHandler.OnAckReceived(datagram);
            return true;
        }

        public void ProcessOpen(PromiseCompletionSource<VoidType> promiseCb)
        {
            if (_sessionHandler.NextWindowIdToSend != 0)
            {
                promiseCb.CompleteExceptionally(new Exception("No longer in the state of opening"));
                return;
            }

            _pendingPromiseCallback = promiseCb;
            _sendWindowHandler.Start();

            SendInProgress = true;
        }

        private void OnSendSuccess()
        {
            // send open succeeded.
            SendInProgress = false;

            // force receives to start expecting from 1.
            _sessionHandler.LastWindowIdReceived = 0;

            // complete pending promise.
            _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
            _pendingPromiseCallback = null;

            _sessionHandler.OnOpenSuccess();
        }

        private void OnSendError(ProtocolOperationException error)
        {
            _sessionHandler.InitiateDispose(error, null);
        }
    }
}
