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

        public void PrepareForDispose(SessionDisposedException cause)
        {
            // nothing to do
        }

        public void Dispose(SessionDisposedException cause)
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
                if (_sendWindowHandler != null)
                {
                    _sendWindowHandler.OnAckReceived(datagram);
                }
                else
                {
                    _sessionHandler.OnDatagramDiscarded(datagram);
                }
                return true;
            }
            else if (datagram.OpCode == ProtocolDatagram.OpCodeClose)
            {
                ProcessReceiveClose(datagram);
                return true;
            }
            return false;
        }

        public bool ProcessSend(ProtocolMessage message, PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSendWithoutAck(ProtocolMessage message, PromiseCompletionSource<bool> promiseCb)
        {
            return false;
        }

        private void ProcessReceiveClose(ProtocolDatagram datagram)
        {
            var recvdAbortCode = datagram.Options?.AbortCode ?? ProtocolDatagram.AbortCodeNormalClose;
            // reject abort code if not valid for a close opcode.
            if (recvdAbortCode == ProtocolDatagram.AbortCodeRestart || 
                recvdAbortCode == ProtocolDatagram.AbortCodeShutdown)
            {
                _sessionHandler.OnDatagramDiscarded(datagram);
                return;
            }

            // if graceful close, then try and validate before proceeding with close.
            if (recvdAbortCode == ProtocolDatagram.AbortCodeNormalClose)
            {
                // Reject close datagram with invalid window id or sequence number.
                if (!(datagram.SequenceNumber == 0 && ProtocolDatagram.IsReceivedWindowIdValid(datagram.WindowId,
                    _sessionHandler.LastWindowIdReceived)))
                {
                    _sessionHandler.OnDatagramDiscarded(datagram);
                    return;
                }
            }

            var cause = new SessionDisposedException(true, recvdAbortCode);

            // send back acknowledgement if closing gracefully, but ignore errors.
            // also don't wait.
            if (cause.AbortCode == ProtocolDatagram.AbortCodeNormalClose)
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
                _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint, ack, null);
            }

            _sessionHandler.ContinueDispose(cause);
        }

        public void ProcessSendClose(SessionDisposedException cause)
        {
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
            _sendWindowHandler = _sessionHandler.CreateRetrySendHandlerAssistant();
            _sendWindowHandler.CurrentWindow = new List<ProtocolDatagram> { closeDatagram };
            _sendWindowHandler.SuccessCallback = () => OnSendSuccessOrError(cause);
            _sendWindowHandler.ErrorCallback = _ => OnSendSuccessOrError(cause);
            _sendWindowHandler.DisposeCallback = _ => OnSendSuccessOrError(cause);
            _sendWindowHandler.Start();

            SendInProgress = true;
        }

        private void OnSendSuccessOrError(SessionDisposedException cause)
        {
            SendInProgress = false;
            _sendWindowHandler = null;
            _sessionHandler.ContinueDispose(cause);
        }
    }
}
