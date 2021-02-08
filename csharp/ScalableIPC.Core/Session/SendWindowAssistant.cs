using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public class SendWindowAssistant: ISendWindowAssistant
    {
        private readonly IStandardSessionHandler _sessionHandler;

        public SendWindowAssistant(IStandardSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public List<ProtocolDatagram> CurrentWindow { get; set; }
        public bool SendOneAtATime { get; set; }
        public int RetryCount { get; set; }
        public Action<ProtocolOperationException> ErrorCallback { get; set; }
        public Action<int> WindowFullCallback { get; set; }
        public Action TimeoutCallback { get; set; }
        public INetworkSendContext SendContext { get; private set; }
        public int SentCount { get; private set; }
        public bool IsStarted { get; private set; }
        public bool IsComplete { get; private set; } = false;

        public void Complete()
        {
            if (!IsComplete)
            {
                IsComplete = true;
                SendContext?.Dispose();
            }
        }

        public void Cancel()
        {
            Complete();
        }

        public void Start()
        {
            if (IsComplete)
            {
                throw new Exception("Cannot reuse cancelled handler");
            }
            if (IsStarted)
            {
                return;
            }

            IsStarted = true;
            SentCount = 0;
            RestartSending();
        }

        private void RestartSending()
        {
            SendContext?.Dispose();
            SendContext = _sessionHandler.NetworkApi.CreateSendContext();
            if (SendContext != null)
            {
                SendContext.RetryCount = RetryCount;
                SendContext.SessionState = _sessionHandler.State;
            }
            ContinueSending();
        }

        private void ContinueSending()
        {
            var nextDatagram = CurrentWindow[SentCount];
            nextDatagram.WindowId = _sessionHandler.NextWindowIdToSend;
            nextDatagram.SequenceNumber = SentCount;

            _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint, nextDatagram, SendContext, e =>
            {
                if (e == null)
                {
                    HandleSendSuccess(nextDatagram);
                }
                else
                {
                    HandleSendError(nextDatagram, e);
                }
            });
            // regardless of send outcome, mark datagram as sent. In that case any ack unexpectedly
            // received before send outcome will still be accepted.
            SentCount++;
        }

        public void OnAckReceived(ProtocolDatagram ack)
        {
            if (IsComplete)
            {
                throw new Exception("Cannot reuse cancelled handler");
            }
            if (!IsStarted)
            {
                throw new Exception("handler has not been started");
            }

            if (_sessionHandler.NextWindowIdToSend != ack.WindowId)
            {
                _sessionHandler.OnDatagramDiscarded(ack);
                return;
            }

            // Receipt of an ack is interpreted as reception of datagram with ack's sequence number,
            // and all preceding datagrams in window as well.
            var receiveCount = ack.SequenceNumber + 1;
            
            var minExpectedReceiveCount = SendOneAtATime ? SentCount: 1;
            var maxExpectedReceiveCount = SendOneAtATime ? CurrentWindow.Count : SentCount;
            
            // validate ack. NB: ignore op code and just assume caller has already validated that.
            if (receiveCount < minExpectedReceiveCount || receiveCount > maxExpectedReceiveCount)
            {
                // reject.
                _sessionHandler.OnDatagramDiscarded(ack);
                return;
            }

            // perhaps processing error occurred? check.
            int ackErrorCode = ProtocolOperationException.FetchExpectedErrorCode(ack);
            if (ackErrorCode > 0)
            {
                _sessionHandler.CancelAckTimeout();

                _sessionHandler.IncrementNextWindowIdToSend();
                Complete();
                ErrorCallback.Invoke(new ProtocolOperationException(ackErrorCode));
                return;
            }

            if (ack.Options?.IsWindowFull == true)
            {
                // Overflow detected in receiver window
                _sessionHandler.CancelAckTimeout();

                _sessionHandler.IncrementNextWindowIdToSend();
                Complete();

                // Look for window size at remote peer and use it for
                // subsequent send operations.
                // don't require it.
                _sessionHandler.RemoteMaxWindowSize = ack.Options?.MaxWindowSize;

                WindowFullCallback.Invoke(receiveCount);
            }
            else if (SendOneAtATime)
            {
                // Ack received for "send one at a time" mode to continue
                _sessionHandler.CancelAckTimeout();

                // continue sending one at a time.
                SentCount = receiveCount;
                RestartSending();
            }
            else if (receiveCount == SentCount)
            {
                // This means network api replied with ack when send callback has not been invoked.
                // Not the normal behaviour, but is acceptable to cater for wide range of network api 
                // characteristics.
                _sessionHandler.CancelAckTimeout();
                RestartSending();
            }
            else
            {
                // Ack is not needed. Ignore.
                _sessionHandler.OnDatagramDiscarded(ack);
            }
        }

        private void HandleSendSuccess(ProtocolDatagram datagram)
        {
            _sessionHandler.PostEventLoopCallback(() =>
            {
                // check if not needed or arriving too late.
                if (IsComplete || SentCount != datagram.SequenceNumber + 1)
                {
                    // send success callback received too late
                    return;
                }

                // send success callback received in time
                if (SendOneAtATime || SentCount >= CurrentWindow.Count)
                {
                    int ackTimeout = SendContext?.DetermineAckTimeout() ?? _sessionHandler.AckTimeout;
                    _sessionHandler.ResetAckTimeout(ackTimeout, ProcessAckTimeout);
                }
                else
                {
                    ContinueSending();
                }
            }, null);
        }

        private void HandleSendError(ProtocolDatagram datagram, Exception error)
        {
            _sessionHandler.PostEventLoopCallback(() =>
            {
                // check if not needed or arriving too late.
                if (IsComplete || SentCount != datagram.SequenceNumber + 1)
                {
                    // send error callback received too late
                    return;
                }

                Complete();
                if (error is ProtocolOperationException protEr)
                {
                    ErrorCallback.Invoke(protEr);
                }
                else
                {
                    ErrorCallback.Invoke(new ProtocolOperationException(error));
                }
            }, null);
        }

        private void ProcessAckTimeout()
        {
            if (!IsComplete)
            {
                Complete();
                TimeoutCallback.Invoke();
            }
        }
    }
}
