using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SendDataHandler : ISessionStateHandler
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
                _sessionHandler.Log("c2f9a95a-17ca-4fc9-ac65-08bfd8060517", "Send pdu failed");

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

            _sessionHandler.Log("abd38766-8116-4123-b5ab-8313fef91f5e", message,
                "Ack pdu accepted for processing in send pdu handler");
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
                "Pdu accepted for processing in send pdu handler");
            ProcessSendRequest(message, promiseCb);
            return true;
        }

        public bool ProcessSend(byte[] data, Dictionary<string, List<string>> options,
            PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        private void ProcessSendRequest(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            // Process options.
            if (message.IdleTimeoutSecs != null)
            {
                _sessionHandler.SessionIdleTimeoutSecs = message.IdleTimeoutSecs.Value;
            }
            if (message.CloseReceiverOption != null)
            {
                _sessionHandler.SessionCloseReceiverOption = message.CloseReceiverOption;
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
            if (_sessionHandler.SessionCloseReceiverOption == true)
            {
                _sessionHandler.SessionState = ProtocolSessionHandler.StateClosing;
            }

            _sessionHandler.Log("5e573074-e830-4f05-a9cb-72be78ab9943", "Send pdu succeeded", 
                "sendInProgress", _sessionHandler.IsSendInProgress(), 
                "idleTimeout", _sessionHandler.SessionIdleTimeoutSecs,
                "closeReceiver", _sessionHandler.SessionCloseReceiverOption,
                "sessionState", _sessionHandler.SessionState);

            // complete pending promise.
            _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
            _pendingPromiseCallback = null;
        }
    }
}
