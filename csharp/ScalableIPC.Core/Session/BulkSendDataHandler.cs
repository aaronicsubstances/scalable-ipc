using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class BulkSendDataHandler: ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;
        private byte[] _windowData;
        private ProtocolDatagramOptions _windowOptions;
        private DatagramChopper _datagramChopper;
        private PromiseCompletionSource<VoidType> _pendingPromiseCallback;
        private RetrySendHandlerAssistant _sendWindowHandler;

        public BulkSendDataHandler(ISessionHandler sessionHandler)
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
                _sessionHandler.Log("9316f65d-f2bd-4877-b929-a9f02b545d3c", "Bulk send data failed");

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
                "Ack pdu accepted for processing in bulk send data handler");
            _sendWindowHandler.OnAckReceived(message);
            return true;
        }

        public bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSend(byte[] windowData, ProtocolDatagramOptions windowOptions,
            PromiseCompletionSource<VoidType> promiseCb)
        {
            _sessionHandler.Log("ab33803c-2eb4-4525-b566-0baa3e1f51ff",
                "Pdu accepted for processing in bulk send data handler");
            ProcessSendRequest(windowData, windowOptions, promiseCb);
            return true;
        }

        private void ProcessSendRequest(byte[] windowData, ProtocolDatagramOptions windowOptions,
           PromiseCompletionSource<VoidType> promiseCb)
        {
            _windowData = windowData;
            _windowOptions = windowOptions;
            _pendingPromiseCallback = promiseCb;
            SendInProgress = ContinueBulkSend(false);
        }

        private bool ContinueBulkSend(bool haveSentBefore)
        {
            _sessionHandler.Log("c5b21878-ac61-4414-ba37-4248a4702084",
                (haveSentBefore ? "Attempting to continue ": "About to start") + " sending data");

            var nextWindow = new List<ProtocolDatagram>();

            // Interpret Int.MaxValue specially if we haven't sent before, to mean
            // that no chopping should be done, rather send it in its entirety.
            if (!haveSentBefore && _sessionHandler.MaximumTransferUnitSize == int.MaxValue)
            {
                var solePdu = new ProtocolDatagram
                {
                    DataBytes = _windowData,
                    DataLength = _windowData.Length,
                    Options = _windowOptions
                };
                nextWindow.Add(solePdu);
            }
            else
            {
                if (!haveSentBefore)
                {
                    _datagramChopper = new DatagramChopper(_windowData, _windowOptions, _sessionHandler.MaximumTransferUnitSize);
                }
                var reserveSpace = ProtocolDatagramOptions.OptionNameIsLastInWindow.Length +
                    Math.Max(true.ToString().Length, false.ToString().Length);
                while (nextWindow.Count < _sessionHandler.MaxSendWindowSize)
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
                if (nextWindow.Count == 0)
                {
                    _sessionHandler.Log("d7d65563-154a-4855-8efd-c19ae60817d8",
                        "No data chunks found for send window.");
                    return false;
                }
            }

            if (nextWindow[nextWindow.Count - 1].Options == null)
            {
                nextWindow[nextWindow.Count - 1].Options = new ProtocolDatagramOptions();
            }
            nextWindow[nextWindow.Count - 1].Options.IsLastInWindow = true;

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

            _sessionHandler.Log("edeafec4-5596-4931-9f2a-5876e1241d89", "Bulk send succeeded",
                "sendInProgress", _sessionHandler.IsSendInProgress(),
                "idleTimeout", _sessionHandler.SessionIdleTimeoutSecs,
                "sessionState", _sessionHandler.SessionState);

            // complete pending promise.
            _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
            _pendingPromiseCallback = null;
            _datagramChopper = null;
            _windowData = null;
            _windowOptions = null;
        }
    }
}
