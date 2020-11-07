using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SendOpenHandler : ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;

        private RetrySendHandlerAssistant _sendWindowHandler;
        private PromiseCompletionSource<VoidType> _pendingPromiseCallback;
        private bool _isLastOpenRequest;

        public SendOpenHandler(ISessionHandler sessionHandler)
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
                _sessionHandler.Log("c2f9a95a-17ca-4fc9-ac65-08bfd8060517", "Send open failed");

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

            ProcessAckReceipt(message);
            return true;
        }

        public bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            if (message.OpCode != ProtocolDatagram.OpCodeOpen)
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
            if (_sessionHandler.SessionState != ProtocolSessionHandler.StateOpening)
            {
                promiseCb.CompleteExceptionally(new Exception("Invalid session state for send open"));
                return;
            }

            if (_sessionHandler.IsSendInProgress())
            {
                promiseCb.CompleteExceptionally(new Exception("Send in progress"));
                return;
            }

            // Process options.
            _isLastOpenRequest = message.IsLastOpenRequest == true;
            if (message.IdleTimeoutSecs != null)
            {
                _sessionHandler.SessionIdleTimeoutSecs = message.IdleTimeoutSecs.Value;
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

        private void ProcessAckReceipt(ProtocolDatagram ack)
        {
            if (!SendInProgress)
            {
                _sessionHandler.DiscardReceivedMessage(ack);
                return;
            }

            _sendWindowHandler.OnAckReceived(ack);
        }

        private void OnWindowSendSuccess()
        {
            SendInProgress = false;

            if (_isLastOpenRequest)
            {
                _sessionHandler.SessionState = ProtocolSessionHandler.StateOpenedForData;
            }

            _sessionHandler.Log("5e573074-e830-4f05-a9cb-72be78ab9943", "Send open succeeded", 
                "sendInProgress", _sessionHandler.IsSendInProgress(), 
                "lastOpenRequest", _isLastOpenRequest,
                "idleTimeout", _sessionHandler.SessionIdleTimeoutSecs,
                "sessionState", _sessionHandler.SessionState);

            // complete pending promise.
            _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
            _pendingPromiseCallback = null;
        }
    }
}
