using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public class SendHandlerAssistant: ISendHandlerAssistant
    {
        private readonly IDefaultSessionHandler _sessionHandler;

        public SendHandlerAssistant(IDefaultSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public List<ProtocolDatagram> CurrentWindow { get; set; }
        public int PreviousSendCount { get; set; }
        public int CurrentSendCount { get; set; }
        public bool StopAndWait { get; set; }
        public int AckTimeoutSecs { get; set; }
        public Action SuccessCallback { get; set; }
        public Action<SessionDisposedException> DisposeCallback { get; set; }
        public Action TimeoutCallback { get; set; }
        public bool IsComplete { get; set; } = false;

        public void Start()
        {
            CurrentSendCount = PreviousSendCount;
            ContinueSending();
        }

        public void Cancel()
        {
            IsComplete = true;
        }

        private void ContinueSending()
        {
            var nextDatagram = CurrentWindow[CurrentSendCount];
            nextDatagram.WindowId = _sessionHandler.NextWindowIdToSend;
            nextDatagram.SequenceNumber = CurrentSendCount - PreviousSendCount;

            _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint, nextDatagram, e =>
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
            CurrentSendCount++;
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

            var minExpectedReceiveCount = StopAndWait ? (CurrentSendCount - PreviousSendCount) : 1;
            var maxExpectedReceiveCount = CurrentWindow.Count - PreviousSendCount;

            
            // validate ack. NB: ignore op code and just assume caller has already validated that.
            if (receiveCount < minExpectedReceiveCount || receiveCount > maxExpectedReceiveCount)
            {
                // reject.
                _sessionHandler.OnDatagramDiscarded(ack);
                return;
            }

            if (receiveCount == maxExpectedReceiveCount)
            {
                // All datagrams in window have been successfully sent and confirmed

                // cancel ack timeout.
                _sessionHandler.CancelAckTimeout();

                IsComplete = true;
                _sessionHandler.IncrementNextWindowIdToSend();
                SuccessCallback.Invoke();
            }
            else if (ack.Options?.IsWindowFull == true)
            {
                // Overflow detected in receiver window

                // cancel ack timeout.
                _sessionHandler.CancelAckTimeout();

                _sessionHandler.IncrementNextWindowIdToSend();
                PreviousSendCount += receiveCount;
                StopAndWait = false;
                Start();
            }
            else if (StopAndWait)
            {
                // Ack received for stop and wait mode to continue

                // cancel ack timeout.
                _sessionHandler.CancelAckTimeout();

                // continue stop and wait.
                CurrentSendCount = PreviousSendCount + receiveCount;
                ContinueSending();
            }
            else
            {
                // Ack is not needed

                // ignore
            }
        }

        private void HandleSendSuccess(ProtocolDatagram datagram)
        {
            _sessionHandler.TaskExecutor.PostCallback(() =>
            {
                // check if not needed or arriving too late.
                if (IsComplete || _sessionHandler.NextWindowIdToSend != datagram.WindowId)
                {
                    // send success callback received too late
                    return;
                }

                // send success callback received in time

                if (StopAndWait || CurrentSendCount >= CurrentWindow.Count)
                {
                    _sessionHandler.ResetAckTimeout(AckTimeoutSecs, ProcessAckTimeout);
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
                if (!IsComplete)
                {
                    IsComplete = true;
                    DisposeCallback.Invoke(new SessionDisposedException(error));
                }
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
