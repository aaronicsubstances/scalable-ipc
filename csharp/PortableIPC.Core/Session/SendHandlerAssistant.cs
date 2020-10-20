using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace PortableIPC.Core.Session
{
    public class SendHandlerAssistant
    {
        private readonly ISessionHandler _sessionHandler;
        private int _pendingPduIndex;

        public SendHandlerAssistant(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public List<ProtocolDatagram> CurrentWindow { get; set; }
        public int StartIndex { get; set; }
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
            _pendingPduIndex = StartIndex;
            ContinueSending();
        }

        private void ContinueSending()
        {
            var nextMessage = CurrentWindow[_pendingPduIndex];
            nextMessage.WindowId = _sessionHandler.NextWindowIdToSend;
            nextMessage.SequenceNumber = _pendingPduIndex - StartIndex;
            _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, nextMessage)
                .Then(HandleSendSuccess, HandleSendError);
        }

        public void OnAckReceived(ProtocolDatagram ack)
        {
            if (_sessionHandler.NextWindowIdToSend != ack.WindowId)
            {
                _sessionHandler.DiscardReceivedMessage(ack);
                return;
            }

            // Receipt of an ack is interpreted as reception of message with ack's sequence numbers,
            // and all preceding messages in window as well.

            var minExpectedSeqNumber = StopAndWait ? (_pendingPduIndex - StartIndex) : 0;
            var maxExpectedSeqNumber = CurrentWindow.Count - 1 - StartIndex;
            if (ack.SequenceNumber < minExpectedSeqNumber || ack.SequenceNumber > maxExpectedSeqNumber)
            {
                // reject.
                _sessionHandler.DiscardReceivedMessage(ack);
                return;
            }
            if (!StopAndWait && ack.IsLastInWindow != true && ack.SequenceNumber != maxExpectedSeqNumber)
            {
                // ignore.
                return;
            }

            // indirectly cancel ack timeout.
            _sessionHandler.ResetIdleTimeout();

            if (ack.SequenceNumber == maxExpectedSeqNumber)
            {
                IsComplete = true;
                SuccessCallback.Invoke();
            }
            else if (ack.IsLastInWindow == true)
            {
                StartIndex += ack.SequenceNumber + 1;
                StopAndWait = false;
                _sessionHandler.IncrementNextWindowIdToSend();
                Start();
            }
            else
            {
                // continue stop and wait.
                _pendingPduIndex = StartIndex + ack.SequenceNumber + 1;
                ContinueSending();
            }
        }

        private VoidType HandleSendSuccess(VoidType _)
        {
            _sessionHandler.PostSerially(() =>
            {
                if (IsComplete)
                {
                    return;
                }
                if (StopAndWait || _pendingPduIndex + 1 >= CurrentWindow.Count)
                {
                    // set up ack timeout.
                    _sessionHandler.ResetAckTimeout(AckTimeoutSecs, ProcessAckTimeout);
                }
                else
                {
                    _pendingPduIndex++;
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
