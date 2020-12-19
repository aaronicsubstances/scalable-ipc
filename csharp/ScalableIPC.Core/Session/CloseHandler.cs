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
        private RetrySendHandlerAssistant _sendWindowHandler;

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
            _sendWindowHandler?.Cancel();
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
            if (datagram.OpCode == ProtocolDatagram.OpCodeCloseAck)
            {
                if (_sendWindowHandler != null)
                {
                    _sendWindowHandler.OnAckReceived(datagram);
                }
                else
                {
                    _sessionHandler.DiscardReceivedDatagram(datagram);
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

        private void ProcessReceiveClose(ProtocolDatagram datagram)
        {
            var cause = new SessionDisposedException(true, datagram.Options?.AbortCode ?? ProtocolDatagram.AbortCodeNormalClose);

            // if graceful close, then try and validate before proceeding with close.
            if (cause.AbortCode == ProtocolDatagram.AbortCodeNormalClose)
            {
                // Reject close datagram with invalid window id or sequence number.
                if (datagram.SequenceNumber == 0 && ProtocolDatagram.IsReceivedWindowIdValid(datagram.WindowId,
                    _sessionHandler.LastWindowIdReceived))
                {
                    // all is set to reply with an ack.
                }
                else
                {
                    _sessionHandler.DiscardReceivedDatagram(datagram);
                    return;
                }
            }

            // use to reject follow up close requests
            _sessionHandler.LastMaxSeqReceived = datagram.SequenceNumber;
            _sessionHandler.LastWindowIdReceived = datagram.WindowId;

            _sessionHandler.InitiateDispose(cause, null, false);

            // send back acknowledgement if closing gracefully.
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

                _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint, ack,
                    _ => { /* ignore any error */ });
            }

            _sessionHandler.ContinueDispose(cause);
        }

        public void ProcessSendClose(SessionDisposedException cause, PromiseCompletionSource<VoidType> promiseCb)
        {
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
            _sendWindowHandler = new RetrySendHandlerAssistant(_sessionHandler)
            {
                CurrentWindow = new List<ProtocolDatagram> { closeDatagram },
                SuccessCallback = () => OnSendSuccessOrError(cause),
                DisposeCallback = _ => OnSendSuccessOrError(cause),
            };
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
