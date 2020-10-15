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

        public bool ProcessReceive(ProtocolDatagram message)
        {
            if (message.OpCode != ProtocolDatagram.OpCodeClose)
            {
                return false;
            }
            ProcessReceiveClose(message);
            return true;
        }

        private void ProcessReceiveClose(ProtocolDatagram message)
        {
            // process termination message regardless of session state.
            Exception error = null;
            if (message.ErrorCode != null)
            {

            }
            _sessionHandler.ProcessShutdown(error, false);
        }

        public bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            if (message.OpCode != ProtocolDatagram.OpCodeClose)
            {
                return false;
            }
            ProcessSendClose(message, promiseCb);
            return true;
        }

        private void ProcessSendClose(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            // process termination message regardless of session state.

            // send but don't care about success.
            _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, message)
                .Then(_ => HandleSendSuccessOrError(message, promiseCb),
                    _ => HandleSendSuccessOrError(message, promiseCb));
        }

        private VoidType HandleSendSuccessOrError(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            _sessionHandler.PostSerially(() =>
            {
                promiseCb.CompleteSuccessfully(VoidType.Instance);

                Exception error = null;
                if (message.ErrorCode != null)
                {

                }
                _sessionHandler.ProcessShutdown(error, false);
            });
            return VoidType.Instance;
        }

        public bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options,
            PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }
    }
}
