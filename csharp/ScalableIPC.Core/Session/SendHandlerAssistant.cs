using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public class SendHandlerAssistant
    {
        private readonly ISessionHandler _sessionHandler;
        private int _sentPduCount;

        public SendHandlerAssistant(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public List<ProtocolDatagram> CurrentWindow { get; set; }
        public int PreviousSendCount { get; set; }

        /// <summary>
        /// Used to alternate between stop and wait flow control, or go back N, in between timeouts. 
        /// </summary>
        public bool StopAndWait { get; set; }
        public int AckTimeoutSecs { get; set; }
        public Action SuccessCallback { get; set; }
        public Action TimeoutCallback { get; set; }

        public void Cancel()
        {
            if (!IsComplete)
            {
                IsCancelled = true;
                IsComplete = true;
            }
        }

        public bool IsCancelled { get; set; } = false;
        public bool IsComplete { get; set; } = false;

        public void Start()
        {
            _sentPduCount = PreviousSendCount;
            ContinueSending();
        }

        private void ContinueSending()
        {
            var nextMessage = CurrentWindow[_sentPduCount];
            int windowIdSnapshot = _sessionHandler.NextWindowIdToSend;
            nextMessage.WindowId = windowIdSnapshot;
            nextMessage.SequenceNumber = _sentPduCount - PreviousSendCount;
            _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.RemoteEndpoint, nextMessage)
                .Then(_ => HandleSendSuccess(windowIdSnapshot), HandleSendError);
            _sentPduCount++;
        }

        public void OnAckReceived(ProtocolDatagram ack)
        {
            if (_sessionHandler.NextWindowIdToSend != ack.WindowId)
            {
                _sessionHandler.DiscardReceivedMessage(ack);
                return;
            }

            // Receipt of an ack is interpreted as reception of message with ack's sequence number,
            // and all preceding messages in window as well.
            var receiveCount = ack.SequenceNumber + 1;

            var minExpectedReceiveCount = StopAndWait ? (_sentPduCount - PreviousSendCount) : 1;
            var maxExpectedReceiveCount = CurrentWindow.Count - PreviousSendCount;
            if (receiveCount < minExpectedReceiveCount || receiveCount > maxExpectedReceiveCount)
            {
                // reject.
                _sessionHandler.DiscardReceivedMessage(ack);
                return;
            }

            if (receiveCount == maxExpectedReceiveCount)
            {
                // indirectly cancel ack timeout.
                _sessionHandler.ResetIdleTimeout();

                IsComplete = true;
                _sessionHandler.IncrementNextWindowIdToSend();
                SuccessCallback.Invoke();
            }
            else if (ack.IsWindowFull == true)
            {
                // indirectly cancel ack timeout.
                _sessionHandler.ResetIdleTimeout();

                _sessionHandler.IncrementNextWindowIdToSend();
                PreviousSendCount += receiveCount;
                StopAndWait = false;
                Start();
            }
            else if (StopAndWait)
            {
                // indirectly cancel ack timeout.
                _sessionHandler.ResetIdleTimeout();

                // continue stop and wait.
                _sentPduCount = PreviousSendCount + receiveCount;
                ContinueSending();
            }
            else
            {
                // ignore but don't discard.
            }
        }

        private VoidType HandleSendSuccess(int windowIdSnapshot)
        {
            _sessionHandler.PostSerially(() =>
            {
                // check if not needed or arriving too late.
                if (IsComplete || _sessionHandler.NextWindowIdToSend != windowIdSnapshot)
                {
                    return;
                }

                if (StopAndWait || _sentPduCount >= CurrentWindow.Count)
                {
                    _sessionHandler.ResetAckTimeout(AckTimeoutSecs, ProcessAckTimeout);
                }
                else
                {
                    ContinueSending();
                }
            });
            return VoidType.Instance;
        }

        private void HandleSendError(Exception error)
        {
            _sessionHandler.PostSerially(() =>
            {
                if (!IsComplete)
                {
                    Cancel();
                    _sessionHandler.ProcessShutdown(error, false);
                }
            });
        }

        private void ProcessAckTimeout()
        {
            if (!IsComplete)
            {
                Cancel();
                TimeoutCallback.Invoke();
            }
        }
    }
}
