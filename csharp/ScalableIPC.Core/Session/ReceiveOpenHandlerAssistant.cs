using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class ReceiveOpenHandlerAssistant: IReceiveOpenHandlerAssistant
    {
        private readonly IStandardSessionHandler _sessionHandler;

        public ReceiveOpenHandlerAssistant(IStandardSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public Action<ProtocolDatagram> DataCallback { get; set; }
        public Action<ProtocolOperationException> ErrorCallback { get; set; }

        public void Cancel()
        {
        }

        public void OnReceive(ProtocolDatagram datagram)
        {
            // Reject unexpected window id
            if (datagram.WindowId != 0 || datagram.SequenceNumber != 0)
            {
                _sessionHandler.OnDatagramDiscarded(datagram);
                return;
            }
            if (_sessionHandler.LastWindowIdReceived != -1)
            {
                // already received and announced to application layer.
                // just send back repeat acknowledgement.
                if (_sessionHandler.LastAck != null && _sessionHandler.LastAck.OpCode == ProtocolDatagram.OpCodeOpenAck)
                {
                    /* fire and forget */
                    _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint,
                        _sessionHandler.LastAck, null, null);
                }
                else
                {
                    _sessionHandler.OnDatagramDiscarded(datagram);
                }
                return;
            }

            // Open is successful
            ProtocolOperationException processingError = null;
            try
            {
                DataCallback.Invoke(datagram);
            }
            catch (ProtocolOperationException ex)
            {
                processingError = ex;
            }
            catch (Exception ex)
            {
                processingError = new ProtocolOperationException(ex);
            }

            // Reset last window bounds and current window.
            _sessionHandler.LastWindowIdReceived = datagram.WindowId;

            // finally send ack response for full window
            // ignore any send ack errors.
            _sessionHandler.LastAck = new ProtocolDatagram
            {
                SessionId = datagram.SessionId,
                OpCode = ProtocolDatagram.OpCodeOpenAck,
                WindowId = datagram.WindowId,
                SequenceNumber = 0,
                Options = new ProtocolDatagramOptions
                {
                    IsWindowFull = true,
                    MaxWindowSize = _sessionHandler.MaxWindowSize,
                    ErrorCode = processingError?.ErrorCode
                }
            };
            _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint,
                _sessionHandler.LastAck, null, null);

            if (processingError != null)
            {
                ErrorCallback.Invoke(processingError);
            }
        }
    }
}
