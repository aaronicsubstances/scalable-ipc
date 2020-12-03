using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SendDataHandler: ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;
        private DatagramChopper _datagramChopper;
        private PromiseCompletionSource<VoidType> _pendingPromiseCallback;
        private RetrySendHandlerAssistant _sendWindowHandler;

        public SendDataHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public bool SendInProgress { get; set; }

        public void Shutdown(Exception error)
        {
            _sendWindowHandler?.Cancel();
            SendInProgress = false;
            if (_pendingPromiseCallback != null)
            {
                _sessionHandler.Log("9316f65d-f2bd-4877-b929-a9f02b545d3c", "Send data failed");

                _pendingPromiseCallback.CompleteExceptionally(error);
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

        public bool ProcessClose(bool closeGracefully, PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        private void ProcessSendRequest(ProtocolDatagram message,
           PromiseCompletionSource<VoidType> promiseCb)
        {
            if (_sessionHandler.SessionState >= DefaultSessionHandler.StateClosing)
            {
                // Session handler is closing so don't send data.
                promiseCb.CompleteExceptionally(new Exception("Session handler is closing"));
                return;
            }

            _pendingPromiseCallback = promiseCb;

            // Interpret non-positive MTU to mean
            // that no chopping should be done, rather send it in its entirety.
            if (_sessionHandler.MaximumTransferUnitSize < 1)
            {
                message.SessionId = _sessionHandler.SessionId;
                if (message.Options != null)
                {
                    // remove standard options except for idle timeout
                    message.Options.IsWindowFull = null;
                    message.Options.ErrorCode = null;
                }
                else
                {
                    message.Options = new ProtocolDatagramOptions();
                }
                message.Options.IsLastInWindow = true;

                _sendWindowHandler = new RetrySendHandlerAssistant(_sessionHandler)
                {
                    CurrentWindow = new List<ProtocolDatagram> { message },
                    SuccessCallback = OnWindowSendSuccess
                };

                _sessionHandler.Log("ca5f4e96-1b8a-4701-9451-e37e94b19721",
                    $"Sending data in its entirety");
                _sendWindowHandler.Start();
            }
            else
            {
                _datagramChopper = new DatagramChopper(message,
                    _sessionHandler.MaximumTransferUnitSize, null);
                ContinueBulkSend(false);
            }
            SendInProgress = true;
        }

        private bool ContinueBulkSend(bool haveSentBefore)
        {
            if (_datagramChopper == null)
            {
                return false;
            }

            _sessionHandler.Log("c5b21878-ac61-4414-ba37-4248a4702084",
                (haveSentBefore ? "Attempting to continue ": "About to start") + " sending data");

            var nextWindow = new List<ProtocolDatagram>();

            var reserveSpace = ProtocolDatagramOptions.OptionNameIsLastInWindow.Length +
                Math.Max(true.ToString().Length, false.ToString().Length);

            // ensure minimum value of 1 for max send window size.
            int maxSendWindowSize = Math.Max(1, _sessionHandler.MaxSendWindowSize);

            while (nextWindow.Count < maxSendWindowSize)
            {
                var nextPdu = _datagramChopper.Next(reserveSpace, false);
                if (nextPdu == null)
                {
                    _sessionHandler.Log("9c7619ff-3c5d-46c0-948c-419372c15d2b",
                        "No more data chunking possible");
                    break;
                }
                nextPdu.SessionId = _sessionHandler.SessionId;
                nextPdu.OpCode = ProtocolDatagram.OpCodeData;
                nextWindow.Add(nextPdu);
            }
            if (haveSentBefore && nextWindow.Count == 0)
            {
                _sessionHandler.Log("d7d65563-154a-4855-8efd-c19ae60817d8",
                    "No data chunks found for send window.");
                return false;
            }

            var lastMsgInNextWindow = nextWindow[nextWindow.Count - 1];
            if (lastMsgInNextWindow.Options == null)
            {
                lastMsgInNextWindow.Options = new ProtocolDatagramOptions();
            }
            lastMsgInNextWindow.Options.IsLastInWindow = true;

            _sendWindowHandler = new RetrySendHandlerAssistant(_sessionHandler)
            {
                CurrentWindow = nextWindow,
                SuccessCallback = OnWindowSendSuccess
            };

            _sessionHandler.Log("d151c5bf-e922-4828-8820-8cf964dac160",
                $"Found {nextWindow.Count} data chunks to send in next window.", 
                "count", nextWindow.Count);
            _sendWindowHandler.Start();
            return true;
        }

        private void OnWindowSendSuccess()
        {
            if (ContinueBulkSend(true))
            {
                _sessionHandler.Log("d2dd3b31-8630-481d-9f18-4b91dd8345c3", 
                    "Found data chunk to continue sending");
                return;
            }

            SendInProgress = false;

            _sessionHandler.Log("edeafec4-5596-4931-9f2a-5876e1241d89", "Send data succeeded",
                "sendInProgress", _sessionHandler.IsSendInProgress(),
                "sessionState", _sessionHandler.SessionState);

            // complete pending promise.
            _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
            _pendingPromiseCallback = null;
            _datagramChopper = null;
        }
    }
}
