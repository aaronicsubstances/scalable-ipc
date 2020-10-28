using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SendDataHandler: ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;

        private RetrySendHandlerAssistant _sendWindowHandler;
        private PromiseCompletionSource<VoidType> _pendingPromiseCallback;

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
            if (message.OpCode != ProtocolDatagram.OpCodeData)
            {
                return false;
            }

            ProcessSendRequest(message, promiseCb);
            return true;
        }

        public bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options, 
            PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        private void ProcessSendRequest(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            if (_sessionHandler.SessionState == SessionState.OpenedForData)
            {
                promiseCb.CompleteExceptionally(new Exception("Invalid session state for send data"));
                return;
            }

            if (_sessionHandler.IsSendInProgress())
            {
                promiseCb.CompleteExceptionally(new Exception("Send in progress"));
                return;
            }

            // create current window to send. let assistant handlers handle assignment of window and sequence numbers.
            message.IsLastInWindow = true;
            var currentWindow = new List<ProtocolDatagram> { message };

            _sendWindowHandler = new RetrySendHandlerAssistant(_sessionHandler)
            {
                CurrentWindow = currentWindow,
                SuccessCallback = OnWindowSendSuccess
            };
            _sendWindowHandler.Start();

            _pendingPromiseCallback = promiseCb;
            SendInProgress = true;
        }

        private void OnWindowSendSuccess()
        {
            SendInProgress = false;

            // complete pending promise.
            _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
            _pendingPromiseCallback = null;
        }
    }
}
