using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SendDataHandler: ISessionStateHandler
    {
        private const int MinimumMtu = 512;
        private readonly ISessionHandler _sessionHandler;
        private DatagramChopper _datagramChopper;
        private PromiseCompletionSource<VoidType> _pendingPromiseCallback;
        private RetrySendHandlerAssistant _sendWindowHandler;

        public SendDataHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public bool SendInProgress { get; set; }

        public void PrepareForDispose(SessionDisposedException cause)
        {
            _sendWindowHandler?.Cancel();
        }

        public void Dispose(SessionDisposedException cause)
        {
            _sendWindowHandler?.Cancel();
            SendInProgress = false;
            if (_pendingPromiseCallback != null)
            {
                _sessionHandler.Log("9316f65d-f2bd-4877-b929-a9f02b545d3c", "Send data failed");

                _pendingPromiseCallback.CompleteExceptionally(cause);
                _pendingPromiseCallback = null;
            }
        }

        public bool ProcessReceive(ProtocolDatagram message)
        {
            if (message.OpCode != ProtocolDatagram.OpCodeAck)
            {
                return false;
            }

            // to prevent clashes with other send handlers, check that specific send in progress is on.
            if (!SendInProgress)
            {
                return false;
            }

            _sessionHandler.Log("4b1d1ab5-f38a-478d-b444-b43cdf9f363a", message,
                "Ack pdu accepted for processing in send data handler");
            _sendWindowHandler.OnAckReceived(message);
            return true;
        }

        public bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            if (message.OpCode != ProtocolDatagram.OpCodeData)
            {
                return false;
            }

            _sessionHandler.Log("75cb01ea-3901-40da-b104-dfc3914e2edd", message,
                "Pdu accepted for processing in send data handler");
            ProcessSendRequest(message, promiseCb);
            return true;
        }

        private void ProcessSendRequest(ProtocolDatagram message,
           PromiseCompletionSource<VoidType> promiseCb)
        {
            _pendingPromiseCallback = promiseCb;

            // ensure minimum of 512 and maximum = pdu max
            int mtu = Math.Min(Math.Max(MinimumMtu, _sessionHandler.MaximumTransferUnitSize),
                ProtocolDatagram.MaxDatagramSize);
            _datagramChopper = new DatagramChopper(message, mtu, null);
            ContinueBulkSend(false);
            SendInProgress = true;
        }

        private bool ContinueBulkSend(bool haveSentBefore)
        {
            _sessionHandler.Log("c5b21878-ac61-4414-ba37-4248a4702084",
                (haveSentBefore ? "Attempting to continue ": "About to start") + " sending data");

            var nextWindow = new List<ProtocolDatagram>();
            
            // add 2 for for null bytes
            var reserveSpace = ProtocolDatagramOptions.OptionNameIsLastInWindow.Length +
                ProtocolDatagramOptions.OptionNameIsLastInWindowGroup.Length +
                Math.Max(true.ToString().Length, false.ToString().Length) * 2 + 2;

            int cumulativeTransferSize = 0;
            while (_datagramChopper.HasNext(reserveSpace))
            {
                // This loop is designed to be entered at least once with cooperation of
                // datagram chopper which will always yield at least 1 pdu,
                // and by checking for window count after addition,
                // and by MTU not exceeding maximum allowed window size.

                var nextPdu = _datagramChopper.Next();
                nextPdu.SessionId = _sessionHandler.SessionId;
                nextPdu.OpCode = ProtocolDatagram.OpCodeData;
                nextWindow.Add(nextPdu);
                cumulativeTransferSize += _datagramChopper.MaxPduSize;

                // effectively minimum value of max send window size is 1
                // even if actually zero or negative.
                if (nextWindow.Count >= _sessionHandler.MaxSendWindowSize)
                {
                    break;
                }

                // check that we can go another round of chopping without exceeding maximum transfer window
                // size - which is the UDP max payload size.
                if (cumulativeTransferSize + _datagramChopper.MaxPduSize > ProtocolDatagram.MaxDatagramSize)
                {
                    break;
                }
            }
            if (haveSentBefore && nextWindow.Count == 0)
            {
                _sessionHandler.Log("d7d65563-154a-4855-8efd-c19ae60817d8",
                    "No more data pdus found for send window.");
                return false;
            }

            var lastMsgInNextWindow = nextWindow[nextWindow.Count - 1];
            if (lastMsgInNextWindow.Options == null)
            {
                lastMsgInNextWindow.Options = new ProtocolDatagramOptions();
            }
            lastMsgInNextWindow.Options.IsLastInWindow = true;
            if (!_datagramChopper.HasNext(reserveSpace))
            {
                lastMsgInNextWindow.Options.IsLastInWindowGroup = true;
            }

            _sendWindowHandler = new RetrySendHandlerAssistant(_sessionHandler)
            {
                CurrentWindow = nextWindow,
                SuccessCallback = OnWindowSendSuccess
            };

            _sessionHandler.Log("d151c5bf-e922-4828-8820-8cf964dac160",
                $"Found {nextWindow.Count} data pdus to send in next window.", 
                "count", nextWindow.Count);
            _sendWindowHandler.Start();
            return true;
        }

        private void OnWindowSendSuccess()
        {
            if (ContinueBulkSend(true))
            {
                _sessionHandler.Log("d2dd3b31-8630-481d-9f18-4b91dd8345c3", 
                    "Found data pdus to continue sending");
                return;
            }

            SendInProgress = false;

            _sessionHandler.Log("edeafec4-5596-4931-9f2a-5876e1241d89", "Send data succeeded",
                "sendInProgress", _sessionHandler.IsSendInProgress());

            // complete pending promise.
            _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
            _pendingPromiseCallback = null;
            _datagramChopper = null;
        }
    }
}
