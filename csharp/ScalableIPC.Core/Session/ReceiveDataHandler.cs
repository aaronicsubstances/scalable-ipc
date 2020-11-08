using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public class ReceiveDataHandler: ISessionStateHandler
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
            // nothing to do.
        }

        public bool ProcessReceive(ProtocolDatagram message)
        {
            // assert expected op code
            if (message.OpCode != ProtocolDatagram.OpCodeData)
            {
                return false;
            }

            _sessionHandler.Log("3b1bc8eb-d682-4e73-8ea3-e27c34e48887", message,
                "Pdu accepted for processing in receive data handler");
            OnReceiveData(message);
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

        private void OnReceiveData(ProtocolDatagram message)
        {
            // validate state
            if (_sessionHandler.SessionState != ProtocolSessionHandler.StateOpenedForData)
            {
                _sessionHandler.Log("81c89fff-cffc-41ac-ab85-6edcf350f3af", message,
                    "Received data in unexpected state", 
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

            _sessionHandler.Log("18e5f70e-6961-4a67-8d2a-d54161ed5607", 
                "Successfully received full window of data",
                "count", currentWindow.Count, "sessionState", _sessionHandler.SessionState);

            // ready to pass on to application layer.
            var windowOptions = new Dictionary<string, List<string>>();
            byte[] windowData = ProtocolDatagram.RetrieveData(currentWindow, windowOptions);
            _sessionHandler.EventLoop.PostCallback(() => _sessionHandler.OnDataReceived(windowData,
                windowOptions));
        }
    }
}
