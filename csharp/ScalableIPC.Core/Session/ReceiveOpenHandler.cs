using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public class ReceiveOpenHandler : ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;
        private ReceiveHandlerAssistant _currentWindowHandler;
        private bool _isLastOpenRequest;

        public ReceiveOpenHandler(ISessionHandler sessionHandler)
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
            if (message.OpCode != ProtocolDatagram.OpCodeOpen)
            {
                return false;
            }

            OnReceiveOpenRequest(message);
            return true;
        }

        public bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options, 
            PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        private void OnReceiveOpenRequest(ProtocolDatagram message)
        {
            // Validate state.
            // However if last open request has been received and window id received is the last one seen, 
            // send back an ack regardless of session state.
            if (_isLastOpenRequest && message.WindowId == _sessionHandler.LastWindowIdReceived)
            {
                // skip validation.
            }
            else if (_isLastOpenRequest || _sessionHandler.SessionState != SessionState.Opening)
            {
                _sessionHandler.DiscardReceivedMessage(message);
                return;
            }

            if (_currentWindowHandler == null)
            {
                _currentWindowHandler = new ReceiveHandlerAssistant(_sessionHandler)
                {
                    AckOpCode = ProtocolDatagram.OpCodeOpenAck,
                    SuccessCallback = OnWindowReceiveSuccess
                };
            }
            _currentWindowHandler.OnReceive(message);
        }

        private void OnWindowReceiveSuccess(List<ProtocolDatagram> currentWindow)
        {
            _currentWindowHandler = null;
            ProcessCurrentWindowOptions(currentWindow);
            if (_isLastOpenRequest)
            {
                _sessionHandler.SessionState = SessionState.OpenedForData;
            }

            // ready to pass on to application layer.
            var windowOptions = new Dictionary<string, List<string>>();
            byte[] windowData = ProtocolDatagram.RetrieveData(currentWindow, windowOptions);
            _sessionHandler.PostNonSerially(() => _sessionHandler.OnOpenRequest(windowData,
                windowOptions, _isLastOpenRequest));
        }

        private void ProcessCurrentWindowOptions(List<ProtocolDatagram> CurrentWindow)
        {
            // All session layer options are single valued.
            // Also session layer options in later pdus override previous ones.
            int? idleTimeoutSecs = null;
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
            }

            // NB: a break loop could be introduced in above loop to shorten execution time, 
            // but loop is left that way to document how to handle future additions to session layer options.
            if (idleTimeoutSecs.HasValue)
            {
                _sessionHandler.SessionIdleTimeoutSecs = idleTimeoutSecs.Value;
            }

            // last_open_request is only applicable when is_last_in_window is true. This ensures
            // that no matter the receiver window size, sender knows when open request phase ends at receiver side.
            // Otherwise receiver could switch to data exchange phase without sender knowing and hence breaking the
            // protocol.
            _isLastOpenRequest = CurrentWindow[maxSeqNr.Value].IsLastInWindow == true && 
                CurrentWindow[maxSeqNr.Value].IsLastOpenRequest == true;
        }
    }
}
