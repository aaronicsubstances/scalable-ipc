using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class BulkSendDataHandler: ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;
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
            if (opCode != ProtocolDatagram.OpCodeData)
            {
                return false;
            }

            ProcessSendRequest(rawData, options, promiseCb);
            return true;
        }

        private void ProcessSendRequest(byte[] rawData, Dictionary<string, List<string>> options,
           PromiseCompletionSource<VoidType> promiseCb)
        {
            if (_sessionHandler.SessionState != ProtocolSessionHandler.StateOpenedForData)
            {
                promiseCb.CompleteExceptionally(new Exception("Invalid session state for send data"));
                return;
            }

            if (_sessionHandler.IsSendInProgress())
            {
                promiseCb.CompleteExceptionally(new Exception("Send in progress"));
                return;
            }

            _datagramChopper = new DatagramChopper(rawData, options, _sessionHandler.MaximumTransferUnitSize);
            _pendingPromiseCallback = promiseCb;
            SendInProgress = ContinueBulkSend();
        }

        private bool ContinueBulkSend()
        {
            var reserveSpace = ProtocolDatagram.OptionNameIsLastInWindow.Length +
                Math.Max(true.ToString().Length, false.ToString().Length);
            var nextWindow = new List<ProtocolDatagram>();
            while (nextWindow.Count < _sessionHandler.MaxSendWindowSize)
            {
                var nextPdu =_datagramChopper.Next(reserveSpace, false);
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

            // complete pending promise.
            _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
            _pendingPromiseCallback = null;
        }
    }
}
