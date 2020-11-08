using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class BulkSendOpenHandler : ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;
        private DatagramChopper _datagramChopper;
        private PromiseCompletionSource<VoidType> _pendingPromiseCallback;
        private RetrySendHandlerAssistant _sendWindowHandler;

        public BulkSendOpenHandler(ISessionHandler sessionHandler)
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
                _sessionHandler.Log("e08bd369-8c55-4e6b-aa04-ce7c00858a7c", "Bulk send open failed");

                _pendingPromiseCallback.CompleteExceptionally(error);
                _pendingPromiseCallback = null;
            }
        }

        public bool ProcessReceive(ProtocolDatagram message)
        {
            if (message.OpCode != ProtocolDatagram.OpCodeOpenAck)
            {
                return false;
            }

            // to prevent clashes with other send handlers, check that specific send in progress is on.
            if (!SendInProgress)
            {
                return false;
            }

            _sessionHandler.Log("44557edb-3334-4ff3-b4c0-d4e9f12205e1", message,
                "OpenAck pdu accepted for processing in bulk send open handler");
            _sendWindowHandler.OnAckReceived(message);
            return true;
        }

        public bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSend(int opCode, byte[] rawData, Dictionary<string, List<string>> options,
            PromiseCompletionSource<VoidType> promiseCb)
        {
            if (opCode != ProtocolDatagram.OpCodeOpen)
            {
                return false;
            }

            _sessionHandler.Log("b6831a63-210c-4baf-95bd-34d96a295313",
                "Pdu accepted for processing in bulk send open handler");
            ProcessSendRequest(rawData, options, promiseCb);
            return true;
        }

        private void ProcessSendRequest(byte[] rawData, Dictionary<string, List<string>> options,
           PromiseCompletionSource<VoidType> promiseCb)
        {
            if (_sessionHandler.SessionState != ProtocolSessionHandler.StateOpening)
            {
                promiseCb.CompleteExceptionally(new Exception("Invalid session state for bulk send open"));
                return;
            }

            if (_sessionHandler.IsSendInProgress())
            {
                promiseCb.CompleteExceptionally(new Exception("Send in progress"));
                return;
            }

            // Process options for session handler.
            ProcessWindowOptions(options);

            _datagramChopper = new DatagramChopper(rawData, options, _sessionHandler.MaximumTransferUnitSize);
            _pendingPromiseCallback = promiseCb;
            SendInProgress = ContinueBulkSend(false);
        }

        private void ProcessWindowOptions(Dictionary<string, List<string>> options)
        {
            // All session layer options are single valued. If multiple are specified, pick first.
            if (options.ContainsKey(ProtocolDatagram.OptionNameIdleTimeout))
            {
                var optionIdleTimeout = options[ProtocolDatagram.OptionNameIdleTimeout].FirstOrDefault();
                if (optionIdleTimeout != null)
                {
                    _sessionHandler.SessionIdleTimeoutSecs = ProtocolDatagram.ParseOptionAsInt32(optionIdleTimeout);
                }
            }
        }

        private bool ContinueBulkSend(bool haveSentBefore)
        {
            _sessionHandler.Log("ee9a4cef-b691-4288-a2b0-f148aaba0295",
                (haveSentBefore ? "Attempting to continue" : "About to start") + " sending open requests");

            var reserveSpace = ProtocolDatagram.OptionNameIsLastInWindow.Length +
                ProtocolDatagram.OptionNameIsLastOpenRequest.Length +
                Math.Max(true.ToString().Length, false.ToString().Length) * 2;
            var nextWindow = new List<ProtocolDatagram>();
            while (nextWindow.Count < _sessionHandler.MaxSendWindowSize)
            {
                var nextPdu = _datagramChopper.Next(reserveSpace, false);
                if (nextPdu == null)
                {
                    _sessionHandler.Log("5a86e127-c9c2-4a07-a21a-36b5d3058bae",
                        "No more open request chunking possible");
                    break;
                }
                nextPdu.SessionId = _sessionHandler.SessionId;
                nextPdu.OpCode = ProtocolDatagram.OpCodeOpen;
                nextWindow.Add(nextPdu);
            }
            if (nextWindow.Count == 0)
            {
                _sessionHandler.Log("a530b2b6-4e9a-4059-8f2a-f70d9bff43c3",
                    "No open request chunks found for send window.");
                return false;
            }
            nextWindow[nextWindow.Count - 1].IsLastInWindow = true;
            bool lastOpenRequest = _datagramChopper.Next(reserveSpace, true) == null;
            nextWindow[nextWindow.Count - 1].IsLastOpenRequest = lastOpenRequest;

            _sendWindowHandler = new RetrySendHandlerAssistant(_sessionHandler)
            {
                CurrentWindow = nextWindow,
                SuccessCallback = OnWindowSendSuccess
            };

            _sessionHandler.Log("0af17085-c3d5-416d-8bc0-8ff4932d6e32",
                $"Found {nextWindow.Count} open request chunks to send in next window.",
                "count", nextWindow.Count);
            _sendWindowHandler.Start();
            return true;
        }

        private void OnWindowSendSuccess()
        {
            if (ContinueBulkSend(true))
            {
                return;
            }

            SendInProgress = false;
            _sessionHandler.SessionState = ProtocolSessionHandler.StateOpenedForData;

            _sessionHandler.Log("edeafec4-5596-4931-9f2a-5876e1241d89", "Bulk send open succeeded",
                "sendInProgress", _sessionHandler.IsSendInProgress(),
                "idleTimeout", _sessionHandler.SessionIdleTimeoutSecs,
                "sessionState", _sessionHandler.SessionState);

            // complete pending promise.
            _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
            _pendingPromiseCallback = null;
        }
    }
}
