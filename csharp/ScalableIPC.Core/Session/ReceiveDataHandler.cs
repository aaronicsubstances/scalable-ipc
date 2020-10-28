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
            if (_sessionHandler.SessionState != SessionState.OpenedForData)
            {
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

            // ready to pass on to application layer.
            var windowOptions = new Dictionary<string, List<string>>();
            byte[] windowData = ProtocolDatagram.RetrieveData(currentWindow, windowOptions);
            _sessionHandler.PostNonSerially(() => _sessionHandler.OnDataReceived(windowData,
                windowOptions));
        }
    }
}
