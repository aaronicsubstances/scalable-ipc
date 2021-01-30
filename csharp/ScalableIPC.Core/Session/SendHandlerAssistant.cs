using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public class SendHandlerAssistant: ISendHandlerAssistant
    {
        private readonly IStandardSessionHandler _sessionHandler;

        public SendHandlerAssistant(IStandardSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public List<ProtocolDatagram> CurrentWindow { get; set; }
        public int SentCount { get; set; }
        public bool StopAndWait { get; set; }
        public int EffectiveAckTimeout { get; set; }
        public Action SuccessCallback { get; set; }
        public Action<SessionDisposedException> DisposeCallback { get; set; }
        public Action<int> WindowFullCallback { get; set; }
        public Action TimeoutCallback { get; set; }
        public bool IsComplete { get; set; } = false;

        public void Start()
        {
            SentCount = 0;
            ContinueSending();
        }

        public void Cancel()
        {
            IsComplete = true;
        }

        private void ContinueSending()
        {
            var nextDatagram = CurrentWindow[SentCount];
            nextDatagram.WindowId = _sessionHandler.NextWindowIdToSend;
            nextDatagram.SequenceNumber = SentCount;

            _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint, nextDatagram, (ackTimeout, e) =>
            {
                if (e == null)
                {
                    EffectiveAckTimeout = Math.Max(EffectiveAckTimeout, ackTimeout);
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
            if (_sessionHandler.NextWindowIdToSend != ack.WindowId)
            {
                _sessionHandler.OnDatagramDiscarded(ack);
                return;
            }

            // Receipt of an ack is interpreted as reception of datagram with ack's sequence number,
            // and all preceding datagrams in window as well.
            var receiveCount = ack.SequenceNumber + 1;
            
            var minExpectedReceiveCount = StopAndWait ? SentCount: 1;
            var maxExpectedReceiveCount = StopAndWait ? CurrentWindow.Count : SentCount;
            
            // validate ack. NB: ignore op code and just assume caller has already validated that.
            if (receiveCount < minExpectedReceiveCount || receiveCount > maxExpectedReceiveCount)
            {
                // reject.
                _sessionHandler.OnDatagramDiscarded(ack);
                return;
            }

            if (receiveCount == CurrentWindow.Count)
            {
                // All datagrams in window have been successfully sent and confirmed
                _sessionHandler.CancelAckTimeout();

                _sessionHandler.IncrementNextWindowIdToSend();
                IsComplete = true;
                SuccessCallback.Invoke();
            }
            else if (ack.Options?.IsWindowFull == true)
            {
                // Overflow detected in receiver window
                _sessionHandler.CancelAckTimeout();

                _sessionHandler.IncrementNextWindowIdToSend();
                IsComplete = true;
                WindowFullCallback.Invoke(receiveCount);
            }
            else if (StopAndWait)
            {
                // Ack received for stop and wait mode to continue
                _sessionHandler.CancelAckTimeout();
                EffectiveAckTimeout = 0;

                // continue stop and wait.
                SentCount = receiveCount;
                ContinueSending();
            }
            else if (receiveCount == SentCount)
            {
                // This means network api replied with ack when send callback has not been invoked.
                // Not the normal behaviour, but is acceptable to cater for wide range of network api 
                // characteristics.
                _sessionHandler.CancelAckTimeout();
                EffectiveAckTimeout = 0;
                ContinueSending();
            }
            else
            {
                // Ack is not needed. Ignore.
                _sessionHandler.OnDatagramDiscarded(ack);
            }
        }

        private void HandleSendSuccess(ProtocolDatagram datagram)
        {
            _sessionHandler.TaskExecutor.PostCallback(() =>
            {
                // check if not needed or arriving too late.
                if (IsComplete || SentCount != datagram.SequenceNumber + 1)
                {
                    // send success callback received too late
                    return;
                }

                // send success callback received in time
                if (StopAndWait || SentCount >= CurrentWindow.Count)
                {
                    _sessionHandler.ResetAckTimeout(EffectiveAckTimeout, ProcessAckTimeout);
                }
                else
                {
                    ContinueSending();
                }
            });
        }

        private void HandleSendError(ProtocolDatagram datagram, Exception error)
        {
            _sessionHandler.TaskExecutor.PostCallback(() =>
            {
                // check if not needed or arriving too late.
                if (IsComplete || SentCount != datagram.SequenceNumber + 1)
                {
                    // send error callback received too late
                    return;
                }

                IsComplete = true;
                DisposeCallback.Invoke(new SessionDisposedException(error));
            });
        }

        private void ProcessAckTimeout()
        {
            if (!IsComplete)
            {
                IsComplete = true;
                TimeoutCallback.Invoke();
            }
        }
    }
}
