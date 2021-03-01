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
        private ISendHandlerAssistant _sendWindowHandler;

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
            if (datagram.OpCode == ProtocolDatagram.OpCodeClose ||
                datagram.OpCode == ProtocolDatagram.OpCodeCloseAck)
            {
                OnReceiveRequest(datagram);
                return true;
            }
            else
            {
                return false;
            }
        }

        public void OnReceiveRequest(ProtocolDatagram datagram)
        {
            if (_sessionHandler.State == SessionState.Opening)
            {
                _sessionHandler.RaiseReceiveError(datagram, "2085b977-1e0e-4bf3-9bc3-c85dc71ef9b3: " +
                    "close pdu/ack received in opening state");
                return;
            }
            if (_sessionHandler.State >= SessionState.Closed)
            {
                _sessionHandler.RaiseReceiveError(datagram, "a40ac0dc-0727-496e-b6f4-5c402bd22fde: " +
                    "close pdu/ack received in closed aftermath state");
                return;
            }

            if (datagram.OpCode == ProtocolDatagram.OpCodeCloseAck)
            {
                // to prevent clashes with other handlers performing sends, 
                // check that specific send in progress is on.
                if (!SendInProgress)
                {
                    _sessionHandler.RaiseReceiveError(datagram, "948b490b-93c6-4aab-94b1-7ccecf0a141a: " +
                        "close handler is not currently sending, so close ack is not needed");
                    return;
                }

                _sendWindowHandler.OnAckReceived(datagram);
            }
            else if (datagram.OpCode == ProtocolDatagram.OpCodeClose)
            {
                ProcessReceiveClose(datagram);
            }
            else
            {
                throw new Exception("unexpected op code: " + datagram.OpCode);
            }
        }

        private void ProcessReceiveClose(ProtocolDatagram datagram)
        {
            var recvdErrorCode = ProtocolOperationException.FetchExpectedAbortCode(datagram);
            if (recvdErrorCode < 0)
            {
                _sessionHandler.RaiseReceiveError(datagram, "5b91e004-2246-4b39-9ff1-7fdfb70b5055: received close pdu with invalid abort code");
                return;
            }

            // if graceful close, then try and validate before proceeding with close.
            if (recvdErrorCode == ProtocolOperationException.ErrorCodeNormalClose)
            {
                // Reject close datagram with invalid window id or sequence number.
                if (datagram.SequenceNumber != 0)
                {
                    _sessionHandler.RaiseReceiveError(datagram, "fd584c28-8b47-4dc7-acb3-a100b5e3933b: received normal close pdu with invalid seq nr");
                    return;
                }
                if (!ProtocolDatagram.IsReceivedWindowIdValid(datagram.WindowId, _sessionHandler.LastWindowIdReceived))
                {
                    _sessionHandler.RaiseReceiveError(datagram, "920980a8-4890-4b6f-a68e-925d3b84a11c: received normal close pdu with invalid window id");
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

            _sessionHandler.InitiateDispose(cause);
        }

        public void ProcessSendClose(ProtocolOperationException cause)
        {
            // send but ignore errors.
            var abortCodeToSend = ProtocolOperationException.ErrorCodeInternalError;
            if (cause.ErrorCode > 0)
            {
                abortCodeToSend = cause.ErrorCode;
            }
            var closeDatagram = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeClose,
                SessionId = _sessionHandler.SessionId,
                WindowId = _sessionHandler.NextWindowIdToSend,
                Options = new ProtocolDatagramOptions
                {
                    AbortCode = abortCodeToSend
                }
            };
            if (abortCodeToSend == ProtocolOperationException.ErrorCodeNormalClose)
            {
                _sendWindowHandler = _sessionHandler.CreateSendHandlerAssistant();
                _sendWindowHandler.ProspectiveWindowToSend = new List<ProtocolDatagram> { closeDatagram };
                _sendWindowHandler.SuccessCallback = () => OnSendSuccessOrError(cause);
                _sendWindowHandler.TimeoutCallback = () => OnSendSuccessOrError(cause);
                _sendWindowHandler.ErrorCallback = _ => OnSendSuccessOrError(cause);
                _sendWindowHandler.Start();

                SendInProgress = true;
            }
            else
            {
                // don't set send in progress, but wait for outcome and proceed regardless of outcome.
                _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint, closeDatagram,
                    null, _ => OnSendSuccessOrError(cause));
            }
        }

        private void OnSendSuccessOrError(ProtocolOperationException cause)
        {
            SendInProgress = false;
            _sessionHandler.InitiateDispose(cause);
        }
    }
}
