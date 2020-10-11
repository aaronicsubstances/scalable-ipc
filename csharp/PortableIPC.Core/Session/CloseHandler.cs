using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Session
{
    public class CloseHandler : ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;

        public CloseHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public void Shutdown(Exception error)
        {
            // nothing to do.
        }

        public bool ProcessReceive(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            // process termination message regardless of session state.
            if (message.OpCode == ProtocolDatagram.OpCodeClose)
            {
                // validate sequence number. sequence number must not belong to range which was last used.
                if (message.SequenceNumber >= _sessionHandler.LastMinSeqReceived &&
                    message.SequenceNumber <= _sessionHandler.LastMaxSeqReceived)
                {
                    _sessionHandler.DiscardReceivedMessage(message, promiseCb);
                }
                else
                {
                    promiseCb.CompleteSuccessfully(VoidType.Instance);

                    Exception error = null;
                    if (message.ErrorMessage != null)
                    {
                        error = new Exception(message.FormatErrorMessage());
                    }
                    _sessionHandler.ProcessShutdown(error, false);
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool ProcessSend(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            // process termination message regardless of session state.
            if (message.OpCode == ProtocolDatagram.OpCodeClose)
            {
                // send but don't care about success.
                _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, message)
                    .Then(_ => HandleSendSuccessOrError(message, promiseCb), 
                        _ => HandleSendSuccessOrError(message, promiseCb));

                return true;
            }
            else
            {
                return false;
            }
        }

        private VoidType HandleSendSuccessOrError(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            _sessionHandler.PostSerially(() =>
            {
                promiseCb.CompleteSuccessfully(VoidType.Instance);

                Exception error = null;
                if (message.ErrorMessage != null)
                {
                    error = new Exception(message.FormatErrorMessage());
                }
                _sessionHandler.ProcessShutdown(error, false);
            });
            return VoidType.Instance;
        }

        public bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options,
            AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }
    }
}
