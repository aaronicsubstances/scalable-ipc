using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public class ReceiveDataHandler : ISessionStateHandler
    {
        private readonly IReferenceSessionHandler _sessionHandler;
        private ReceiveHandlerAssistant _currentWindowHandler;

        public ReceiveDataHandler(IReferenceSessionHandler sessionHandler)
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

        public void PrepareForDispose(SessionDisposedException cause)
        {
            // nothing to do
        }

        public void Dispose(SessionDisposedException cause)
        {
            // nothing to do
        }

        public bool ProcessReceive(ProtocolDatagram datagram)
        {
            // check opcode.
            if (datagram.OpCode != ProtocolDatagram.OpCodeData)
            {
                return false;
            }

            _sessionHandler.Log("cdd5a60c-239d-440d-b7cb-03516c9ed818", datagram,
                "Datagram accepted for processing in receive handler");
            OnReceiveRequest(datagram);
            return true;
        }

        public bool ProcessSend(ProtocolMessage message, PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        private void OnReceiveRequest(ProtocolDatagram datagram)
        {
            if (_currentWindowHandler == null)
            {
                _currentWindowHandler = new ReceiveHandlerAssistant(_sessionHandler)
                {
                    AckOpCode = ProtocolDatagram.OpCodeAck,
                    SuccessCallback = OnWindowReceiveSuccess
                };
            }
            _currentWindowHandler.OnReceive(datagram);
        }

        private bool OnWindowReceiveSuccess(List<ProtocolDatagram> currentWindow)
        {
            try
            {
                ProtocolDatagram windowAsMessage = ProtocolDatagram.CreateMessageOutOfWindow(currentWindow);
                ProcessCurrentWindowOptions(windowAsMessage.Options);

                _sessionHandler.Log("85b3284a-7787-4949-a8de-84211f91e154",
                    "Successfully received window group",
                    "count", currentWindow.Count,
                    "remoteIdleTimeout", _sessionHandler.RemoteIdleTimeoutSecs);

                // now create message for application layer, and decode any long options present.
                ProtocolMessage messageForApp = new ProtocolMessage
                {
                    SessionId = windowAsMessage.SessionId,
                    DataBytes = windowAsMessage.DataBytes,
                    DataOffset = windowAsMessage.DataOffset,
                    DataLength = windowAsMessage.DataLength
                };
                if (windowAsMessage.Options != null)
                {
                    messageForApp.Attributes = new Dictionary<string, List<string>>();
                    foreach (var option in windowAsMessage.Options.AllOptions)
                    {
                        if (option.Key.StartsWith(ProtocolDatagramFragmenter.EncodedOptionNamePrefix))
                        {
                            // NB: long option decoding could result in errors.
                            var originalOption = ProtocolDatagramFragmenter.DecodeLongOption(option.Value);
                            messageForApp.Attributes.Add(originalOption[0], new List<string> { originalOption[1] });
                        }
                        else
                        {
                            messageForApp.Attributes.Add(option.Key, option.Value);
                        }
                    }
                }

                // ready to pass on to application layer.
                _sessionHandler.OnMessageReceived(new MessageReceivedEventArgs { Message = messageForApp });

                // now window handler is not needed any more
                _currentWindowHandler = null;

                return true;
            }
            catch (Exception ex)
            {
                _sessionHandler.Log("fb19272c-9f47-4be2-9949-9ac0dd55e2c0",
                    "Failed to pass window group to application layer",
                    "error", ex);
                return false;
            }
        }

        private void ProcessCurrentWindowOptions(ProtocolDatagramOptions windowOptions)
        {
            if (windowOptions?.IdleTimeoutSecs != null)
            {
                _sessionHandler.RemoteIdleTimeoutSecs = windowOptions.IdleTimeoutSecs;
            }
        }
    }
}
