using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public class ReceiveDataHandler : ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;
        private ReceiveHandlerAssistant _currentWindowHandler;

        public ReceiveDataHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public bool SendInProgress
        {
            get
            {
                return false;
            }
        }

        public void Shutdown(Exception error)
        {
            // nothing to do
        }

        public bool ProcessReceive(ProtocolDatagram message)
        {
            // check opcode.
            if (message.OpCode != ProtocolDatagram.OpCodeData)
            {
                return false;
            }

            _sessionHandler.Log("cdd5a60c-239d-440d-b7cb-03516c9ed818", message,
                "Pdu accepted for processing in receive pdu handler");
            OnReceiveRequest(message);
            return true;
        }

        public bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSend(byte[] data, Dictionary<string, List<string>> options, 
            PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        private void OnReceiveRequest(ProtocolDatagram message)
        {
            // Validate state.
            // However if window id received is the last one seen, 
            // send back an ack regardless of session state.
            if (message.WindowId == _sessionHandler.LastWindowIdReceived)
            {
                // skip validation.
            }
            else if (_sessionHandler.SessionState != ProtocolSessionHandler.StateDataExchange)
            {
                var explanation = _sessionHandler.SessionState == ProtocolSessionHandler.StateClosing ?
                    "Receiver end of session is closed" : "Received data in unexpected state";
                _sessionHandler.Log("cb0169b9-f283-48dd-99f5-fe62b6e52468", message,
                    explanation,
                    "sessionState", _sessionHandler.SessionState);
                _sessionHandler.DiscardReceivedMessage(message);
                return;
            }

            if (_currentWindowHandler == null)
            {
                _currentWindowHandler = new ReceiveHandlerAssistant(_sessionHandler)
                {
                    AckOpCode = ProtocolDatagram.OpCodeAck,
                    SuccessCallback = OnWindowReceiveSuccess
                };
            }
            _currentWindowHandler.OnReceive(message);
        }

        private void OnWindowReceiveSuccess(List<ProtocolDatagram> currentWindow)
        {
            _currentWindowHandler = null;
            ProcessCurrentWindowOptions(currentWindow);
            if (_sessionHandler.SessionCloseReceiverOption == true)
            {
                _sessionHandler.SessionState = ProtocolSessionHandler.StateClosing;
            }

            _sessionHandler.Log("85b3284a-7787-4949-a8de-84211f91e154",
                "Successfully received full window of data",
                "count", currentWindow.Count, "sessionState", _sessionHandler.SessionState,
                "idleTimeout", _sessionHandler.SessionIdleTimeoutSecs,
                "closeReceiver", _sessionHandler.SessionCloseReceiverOption, 
                "sessionState", _sessionHandler.SessionState);

            // ready to pass on to application layer.
            var windowOptions = new Dictionary<string, List<string>>();
            byte[] windowData = ProtocolDatagram.RetrieveData(currentWindow, windowOptions);
            _sessionHandler.EventLoop.PostCallback(() => _sessionHandler.OnDataReceived(windowData,
                windowOptions));
        }

        private void ProcessCurrentWindowOptions(List<ProtocolDatagram> CurrentWindow)
        {
            // All session layer options are single valued.
            // Also session layer options in later pdus override previous ones.
            int? idleTimeoutSecs = null;
            bool? closeReceiverOption = null;
            int? maxSeqNr = null;
            for (int i = CurrentWindow.Count - 1; i >= 0; i--)
            {
                var msg = CurrentWindow[i];
                if (msg == null)
                {
                    continue;
                }
                if (maxSeqNr == null)
                {
                    maxSeqNr = i;
                }
                if (!idleTimeoutSecs.HasValue && msg.IdleTimeoutSecs != null)
                {
                    idleTimeoutSecs = msg.IdleTimeoutSecs;
                }
                if (!closeReceiverOption.HasValue && msg.CloseReceiverOption != null)
                {
                    closeReceiverOption = msg.CloseReceiverOption;
                }
            }

            // NB: a break loop could be introduced in above loop to shorten execution time, 
            // but loop is left that way to document how to handle future additions to session layer options.

            if (idleTimeoutSecs.HasValue)
            {
                _sessionHandler.SessionIdleTimeoutSecs = idleTimeoutSecs;
            }
            if (closeReceiverOption.HasValue)
            {
                _sessionHandler.SessionCloseReceiverOption = closeReceiverOption;
            }
        }
    }
}
