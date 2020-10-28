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
                var cb = _pendingPromiseCallback;
                _pendingPromiseCallback = null;
                _sessionHandler.PostNonSerially(() => cb.CompleteExceptionally(error));
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

            ProcessSendRequest(rawData, options, promiseCb);
            return true;
        }

        private void ProcessSendRequest(byte[] rawData, Dictionary<string, List<string>> options,
           PromiseCompletionSource<VoidType> promiseCb)
        {
            if (_sessionHandler.SessionState != SessionState.Opening)
            {
                _sessionHandler.PostNonSerially(() =>
                    promiseCb.CompleteExceptionally(new Exception("Invalid session state for send open")));
                return;
            }

            if (_sessionHandler.IsSendInProgress())
            {
                _sessionHandler.PostNonSerially(() =>
                    promiseCb.CompleteExceptionally(new Exception("Send in progress")));
                return;
            }

            // Process options for session handler.
            ProcessWindowOptions(options);

            _datagramChopper = new DatagramChopper(rawData, options, _sessionHandler.MaxSendDatagramLength);
            _pendingPromiseCallback = promiseCb;
            SendInProgress = ContinueBulkSend();
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

        private bool ContinueBulkSend()
        {
            var reserveSpace = ProtocolDatagram.OptionNameIsLastInWindow.Length +
                ProtocolDatagram.OptionNameIsLastOpenRequest.Length +
                Math.Max(true.ToString().Length, false.ToString().Length) * 2;
            var nextWindow = new List<ProtocolDatagram>();
            while (nextWindow.Count < _sessionHandler.MaxSendWindowSize)
            {
                var nextPdu = _datagramChopper.Next(reserveSpace, false);
                if (nextPdu == null)
                {
                    break;
                }
                nextWindow.Add(nextPdu);
            }
            if (nextWindow.Count == 0)
            {
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
            _sendWindowHandler.Start();
            return true;
        }

        private void OnWindowSendSuccess()
        {
            if (ContinueBulkSend())
            {
                return;
            }

            SendInProgress = false;
            _sessionHandler.SessionState = SessionState.OpenedForData;

            // complete pending promise.
            var cb = _pendingPromiseCallback;
            _pendingPromiseCallback = null;
            _sessionHandler.PostNonSerially(() =>
            {
                cb.CompleteSuccessfully(VoidType.Instance);
            });
        }
    }
}
