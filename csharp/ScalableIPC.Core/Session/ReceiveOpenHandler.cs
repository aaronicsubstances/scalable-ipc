using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public class ReceiveOpenHandler : ISessionStateHandler
    {
        private readonly IStandardSessionHandler _sessionHandler;
        private IReceiveOpenHandlerAssistant _currentWindowHandler;

        public ReceiveOpenHandler(IStandardSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public bool SendInProgress => false;

        public void PrepareForDispose(ProtocolOperationException cause)
        {
            Dispose(cause);
        }

        public void Dispose(ProtocolOperationException cause)
        {
            _currentWindowHandler?.Cancel();
            _currentWindowHandler = null;
        }

        public bool ProcessOpen(PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
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

        public bool ProcessSend(ProtocolMessage message, PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSendWithoutAck(ProtocolMessage message, PromiseCompletionSource<bool> promiseCb)
        {
            return false;
        }

        private void ProcessReceiveOpen(ProtocolDatagram datagram)
        {
            if (_currentWindowHandler == null)
            {
                _currentWindowHandler = _sessionHandler.CreateReceiveOpenHandlerAssistant();
                _currentWindowHandler.DataCallback = OnOpenReceived;
                _currentWindowHandler.ErrorCallback = OnOpenReceiveError;
            }
            _currentWindowHandler.OnReceive(datagram);
        }

        private void OnOpenReceived(ProtocolDatagram openRequest)
        {
            // force sends to start after 0.
            _sessionHandler.IncrementNextWindowIdToSend();

            // ready to pass on to application layer.
            ProcessOpenRequestOptions(openRequest.Options);
            _sessionHandler.OnOpenReceived();
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
            _sessionHandler.InitiateDispose(error, null);
        }
    }
}