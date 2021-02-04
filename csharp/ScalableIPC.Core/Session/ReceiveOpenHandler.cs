using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public class ReceiveOpenHandler : ISessionStateHandler
    {
        private readonly IStandardSessionHandler _sessionHandler;
        private readonly IReceiveOpenHandlerAssistant _delegateHandler;

        public ReceiveOpenHandler(IStandardSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
            _delegateHandler = _sessionHandler.CreateReceiveOpenHandlerAssistant();
            _delegateHandler.DataCallback = OnOpenReceived;
            _delegateHandler.ErrorCallback = OnOpenReceiveError;
        }

        public bool SendInProgress => false;

        public void Dispose(ProtocolOperationException cause)
        {
            _delegateHandler.Cancel();
        }

        public bool ProcessReceive(ProtocolDatagram datagram)
        {
            if (datagram.OpCode != ProtocolDatagram.OpCodeOpen)
            {
                return false;
            }

            ProcessReceiveOpen(datagram);
            return true;
        }

        private void ProcessReceiveOpen(ProtocolDatagram datagram)
        {
            _delegateHandler.OnReceive(datagram);
        }

        private void OnOpenReceived(ProtocolDatagram openRequest)
        {
            // force sends to start after 0.
            _sessionHandler.IncrementNextWindowIdToSend();

            // ready to pass on to application layer.
            ProcessOpenRequestOptions(openRequest.Options);
            _sessionHandler.OnOpenSuccess();
        }

        private void ProcessOpenRequestOptions(ProtocolDatagramOptions openRequestOptions)
        {
            if (openRequestOptions?.IdleTimeout != null)
            {
                _sessionHandler.RemoteIdleTimeout = openRequestOptions.IdleTimeout;
                _sessionHandler.ResetIdleTimeout();
            }
        }

        private void OnOpenReceiveError(ProtocolOperationException error)
        {
            _sessionHandler.InitiateDisposeBypassingSendClose(error);
        }
    }
}