using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public class ReceiveDataHandler : ISessionStateHandler
    {
        private readonly IStandardSessionHandler _sessionHandler;
        private IReceiveHandlerAssistant _currentWindowHandler;

        public ReceiveDataHandler(IStandardSessionHandler sessionHandler)
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

        public void Dispose(ProtocolOperationException cause)
        {
            _currentWindowHandler?.Cancel();
            _currentWindowHandler = null;
        }

        public bool ProcessReceive(ProtocolDatagram datagram)
        {
            // check opcode.
            if (datagram.OpCode != ProtocolDatagram.OpCodeData)
            {
                return false;
            }

            OnReceiveRequest(datagram);
            return true;
        }

        private void OnReceiveRequest(ProtocolDatagram datagram)
        {
            if (_currentWindowHandler == null)
            {
                _currentWindowHandler = _sessionHandler.CreateReceiveHandlerAssistant();
                _currentWindowHandler.DataCallback = OnWindowReceived;
                _currentWindowHandler.ErrorCallback = OnWindowReceiveError;
            }
            _currentWindowHandler.OnReceive(datagram);
        }

        private void OnWindowReceived(List<ProtocolDatagram> currentWindow)
        {
            ProtocolDatagram windowAsMessage = ProtocolDatagram.CreateMessageOutOfWindow(currentWindow);

            // now create message for application layer, and decode any long options present.
            ProtocolMessage messageForApp = new ProtocolMessage
            {
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
                        try
                        {
                            var originalOption = ProtocolDatagramFragmenter.DecodeLongOption(option.Value);
                            messageForApp.Attributes.Add(originalOption[0], new List<string> { originalOption[1] });
                        }
                        catch (Exception ex)
                        {
                            throw new ProtocolOperationException(false,
                                ProtocolOperationException.ErrorCodeOptionDecodingError, ex);
                        }
                    }
                    else
                    {
                        messageForApp.Attributes.Add(option.Key, option.Value);
                    }
                }
            }

            // ready to pass on to application layer.
            ProcessCurrentWindowOptions(windowAsMessage.Options);
            _sessionHandler.OnMessageReceived(messageForApp);

            // now window handler is not needed any more
            _currentWindowHandler = null;
        }

        private void ProcessCurrentWindowOptions(ProtocolDatagramOptions windowOptions)
        {
            if (windowOptions?.IdleTimeout != null)
            {
                _sessionHandler.RemoteIdleTimeout = windowOptions.IdleTimeout;
                _sessionHandler.ResetIdleTimeout();
            }
        }

        private void OnWindowReceiveError(ProtocolOperationException error)
        {
            // now window handler is not needed any more
            _currentWindowHandler = null;

            // notify application layer.
            _sessionHandler.OnReceiveError(error);
        }
    }
}
