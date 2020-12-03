using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public class SendHandlerAssistant
    {
        private readonly ISessionHandler _sessionHandler;
        private readonly AbstractPromise<VoidType> _voidReturnPromise;

        private int _sentPduCount;

        public SendHandlerAssistant(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
            _voidReturnPromise = _sessionHandler.NetworkInterface.PromiseApi.Resolve(VoidType.Instance);
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

            _sessionHandler.Log("dcbcfdb9-d486-41ca-834e-ca35db609921", "Starting another round of sending", 
                "sentPduCount", _sentPduCount, "windowId", _sessionHandler.NextWindowIdToSend);
            ContinueSending();
        }

        private void ContinueSending()
        {
            var nextMessage = CurrentWindow[_sentPduCount];
            nextMessage.WindowId = _sessionHandler.NextWindowIdToSend;
            nextMessage.SequenceNumber = _sentPduCount - PreviousSendCount;

            _sessionHandler.Log("e289253e-bc8b-4d84-b337-8e3627b2759c", nextMessage, "Sending next message", 
                "sentPduCount", _sentPduCount);
            _sessionHandler.NetworkInterface.HandleSendAsync(_sessionHandler.RemoteEndpoint, nextMessage)
                .ThenOrCatchCompose(_ => HandleSendSuccess(nextMessage), e => HandleSendError(nextMessage, e));
            _sentPduCount++;
        }

        public void OnAckReceived(ProtocolDatagram ack)
        {
            if (_sessionHandler.NextWindowIdToSend != ack.WindowId)
            {
                _sessionHandler.Log("945a983c-7f71-4739-993f-7091ab158eb9", ack,
                    "Received ack with unexpected window id",
                    "windowId", _sessionHandler.NextWindowIdToSend);
                _sessionHandler.DiscardReceivedMessage(ack);
                return;
            }

            // Receipt of an ack is interpreted as reception of message with ack's sequence number,
            // and all preceding messages in window as well.
            var receiveCount = ack.SequenceNumber + 1;

            var minExpectedReceiveCount = StopAndWait ? (_sentPduCount - PreviousSendCount) : 1;
            var maxExpectedReceiveCount = CurrentWindow.Count - PreviousSendCount;

            _sessionHandler.Log("5fa0a2e6-b650-41b0-9d11-b8dba8ebcc70", ack,
                "About to validate ack",
                "receiveCount", receiveCount, "stopAndWait", StopAndWait,
                "min", minExpectedReceiveCount, "max", maxExpectedReceiveCount);
            if (receiveCount < minExpectedReceiveCount || receiveCount > maxExpectedReceiveCount)
            {
                // reject.
                _sessionHandler.Log("e813e703-cd79-4872-a536-4af3ac20f158", ack,
                    "Received ack with unexpected sequence number");
                _sessionHandler.DiscardReceivedMessage(ack);
                return;
            }

            if (receiveCount == maxExpectedReceiveCount)
            {
                _sessionHandler.Log("420af144-e772-444d-ab2d-57da89ad38b6",
                    "All messages in window have been successfully sent and confirmed");

                // cancel ack timeout.
                _sessionHandler.CancelAckTimeout();

                IsComplete = true;
                _sessionHandler.IncrementNextWindowIdToSend();
                SuccessCallback.Invoke();
            }
            else if (ack.Options?.IsWindowFull == true)
            {
                _sessionHandler.Log("049b8b41-49ce-4ffd-8f34-8f0ffb084626",
                    "Overflow detected in receiver window");

                // cancel ack timeout.
                _sessionHandler.CancelAckTimeout();

                _sessionHandler.IncrementNextWindowIdToSend();
                PreviousSendCount += receiveCount;
                StopAndWait = false;
                Start();
            }
            else if (StopAndWait)
            {
                _sessionHandler.Log("905ae2a9-a867-4d76-bda0-c8db15a153dc",
                    "Ack received for stop and wait mode to continue");

                // cancel ack timeout.
                _sessionHandler.CancelAckTimeout();

                // continue stop and wait.
                _sentPduCount = PreviousSendCount + receiveCount;
                ContinueSending();
            }
            else
            {
                _sessionHandler.Log("9c763f83-c9fe-463d-ab6f-5d939264c822", "Ack is not needed");

                // ignore but don't discard.
            }
        }

        private AbstractPromise<VoidType> HandleSendSuccess(ProtocolDatagram message)
        {
            _sessionHandler.EventLoop.PostCallback(() =>
            {
                // check if not needed or arriving too late.
                if (IsComplete || _sessionHandler.NextWindowIdToSend != message.WindowId)
                {
                    _sessionHandler.Log("664de60b-154f-4902-85cf-5eeaee13ea59", message, 
                        "send success callback received too late");
                    return;
                }

                _sessionHandler.Log("bbf832bb-63b7-4366-8b9d-2b4faab4e5fc", message,
                    "send success callback received in time");

                if (StopAndWait || _sentPduCount >= CurrentWindow.Count)
                {
                    _sessionHandler.ResetAckTimeout(AckTimeoutSecs, ProcessAckTimeout);
                }
                else
                {
                    ContinueSending();
                }
            });
            return _voidReturnPromise;
        }

        private AbstractPromise<VoidType> HandleSendError(ProtocolDatagram message, Exception error)
        {
            _sessionHandler.EventLoop.PostCallback(() =>
            {
                if (IsComplete)
                {
                    _sessionHandler.Log("867cfd5e-fec9-45c5-a8f8-1475ee7f9a63", message,
                        "Ignoring send failure", "error", error);
                }
                else
                {
                    _sessionHandler.Log("c57b8654-7c31-499d-b89b-52d1d5d7dd8d", message,
                        "Sending failed. Shutting down...", "error", error);
                    Cancel();
                    _sessionHandler.InitiateClose(new SessionCloseException(error));
                }
            });
            return _voidReturnPromise;
        }

        private void ProcessAckTimeout()
        {
            if (IsComplete)
            {
                _sessionHandler.Log("37c3d8d1-06f3-44ec-b060-e622c9198ff5",
                    "Ignoring ack timeout");
            }
            else
            {
                _sessionHandler.Log("3b2ebb8c-15fe-49cb-a657-55df547806eb",
                    "Ack receipt wait timed out");
                Cancel();
                TimeoutCallback.Invoke();
            }
        }
    }
}
