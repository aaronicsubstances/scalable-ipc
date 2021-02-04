using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class CloseHandler : ISessionStateHandler
    {
        private readonly IStandardSessionHandler _sessionHandler;
        private readonly List<PromiseCompletionSource<VoidType>> _pendingPromiseCallbacks;
        private IRetrySendHandlerAssistant _sendWindowHandler;

        public CloseHandler(IStandardSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
            _pendingPromiseCallbacks = new List<PromiseCompletionSource<VoidType>>();
        }

        public bool SendInProgress { get; set; }

        public void Dispose(ProtocolOperationException cause)
        {
            _sendWindowHandler?.Cancel();
            _sendWindowHandler = null;
            foreach (var cb in _pendingPromiseCallbacks)
            {
                cb.CompleteSuccessfully(VoidType.Instance);
            }
            _pendingPromiseCallbacks.Clear();
        }

        public void QueueCallback(PromiseCompletionSource<VoidType> promiseCb)
        {
            _pendingPromiseCallbacks.Add(promiseCb);
        }

        public bool ProcessReceive(ProtocolDatagram datagram)
        {
            if (datagram.OpCode == ProtocolDatagram.OpCodeCloseAck)
            {
                // to prevent clashes with other handlers performing sends, 
                // check that specific send in progress is on.
                if (SendInProgress)
                {
                    _sendWindowHandler.OnAckReceived(datagram);
                    return true;
                }
            }
            else if (datagram.OpCode == ProtocolDatagram.OpCodeClose)
            {
                ProcessReceiveClose(datagram);
                return true;
            }
            else if (datagram.OpCode == ProtocolDatagram.OpCodeEnquireLinkAck)
            {
                ProcessEnquireLink(datagram);
                return true;
            }
            return false;
        }

        private void ProcessEnquireLink(ProtocolDatagram datagram)
        {
            int enquireLinkErrorCode = ProtocolOperationException.FetchExpectedErrorCode(datagram);
            if (enquireLinkErrorCode == 0)
            {
                _sessionHandler.OnEnquireLinkSuccess();
            }
            else
            {
                _sessionHandler.InitiateDisposeBypassingSendClose(new ProtocolOperationException(enquireLinkErrorCode));
            }
        }

        private void ProcessReceiveClose(ProtocolDatagram datagram)
        {
            var recvdErrorCode = ProtocolOperationException.FetchExpectedErrorCode(datagram);
            
            // if graceful close, then try and validate before proceeding with close.
            if (recvdErrorCode == ProtocolOperationException.ErrorCodeNormalClose)
            {
                // Reject close datagram with invalid window id or sequence number.
                if (!(datagram.SequenceNumber == 0 && ProtocolDatagram.IsReceivedWindowIdValid(datagram.WindowId,
                    _sessionHandler.LastWindowIdReceived)))
                {
                    _sessionHandler.OnDatagramDiscarded(datagram);
                    return;
                }
            }

            var cause = new ProtocolOperationException(recvdErrorCode);

            // send back acknowledgement if closing gracefully, but ignore errors.
            // also don't wait.
            if (cause.ErrorCode == ProtocolOperationException.ErrorCodeNormalClose)
            {
                var ack = new ProtocolDatagram
                {
                    SessionId = _sessionHandler.SessionId,
                    OpCode = ProtocolDatagram.OpCodeCloseAck,
                    WindowId = datagram.WindowId,
                    Options = new ProtocolDatagramOptions
                    {
                        IsWindowFull = true
                    }
                };
                /* ignore any error */
                _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint, ack, null, null);
            }

            _sessionHandler.InitiateDisposeBypassingSendClose(cause);
        }

        public void ProcessSendClose(ProtocolOperationException cause)
        {
            // send but ignore errors.
            var errorCodeToSend = ProtocolOperationException.ErrorCodeInternalError;
            if (cause.ErrorCode > 0)
            {
                errorCodeToSend = cause.ErrorCode;
            }
            var closeDatagram = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeClose,
                SessionId = _sessionHandler.SessionId,
                WindowId = _sessionHandler.NextWindowIdToSend,
                Options = new ProtocolDatagramOptions
                {
                    ErrorCode = errorCodeToSend
                }
            };
            _sendWindowHandler = _sessionHandler.CreateRetrySendHandlerAssistant();
            _sendWindowHandler.CurrentWindow = new List<ProtocolDatagram> { closeDatagram };
            _sendWindowHandler.SuccessCallback = () => OnSendSuccessOrError(cause);
            _sendWindowHandler.TimeoutCallback = () => OnSendSuccessOrError(cause);
            _sendWindowHandler.ErrorCallback = _ => OnSendSuccessOrError(cause);
            _sendWindowHandler.Start();

            SendInProgress = true;
        }

        private void OnSendSuccessOrError(ProtocolOperationException cause)
        {
            SendInProgress = false;
            _sessionHandler.InitiateDisposeBypassingSendClose(cause);
        }
    }
}
