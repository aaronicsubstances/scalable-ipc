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
            if (message.OpCode != ProtocolDatagram.OpCodeClose)
            {
                return false;
            }
            ProcessReceiveClose(message);
            return true;
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

        public bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options,
            PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        private void ProcessReceiveClose(ProtocolDatagram message)
        {
            // process termination message regardless of session state.
            Exception error = null;
            if (message.ErrorCode != null)
            {
                error = new Exception(FormatErrorcode(message.ErrorCode.Value));
            }
            _sessionHandler.ProcessShutdown(error, false);
        }

        public virtual string FormatErrorcode(int errorCode)
        {
            switch (errorCode)
            {
                default:
                    return $"{errorCode} Unknown error";
            }
        }

        private void ProcessSendClose(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            // process termination message regardless of session state.

            // send but ignore errors.
            _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.RemoteEndpoint, message)
                .Then(_ => HandleSendSuccessOrError(promiseCb),
                    _ => HandleSendSuccessOrError(promiseCb));
        }

        private VoidType HandleSendSuccessOrError(PromiseCompletionSource<VoidType> promiseCb)
        {            
            _sessionHandler.PostSerially(() =>
            {
                _sessionHandler.PostNonSerially(() =>
                   promiseCb.CompleteSuccessfully(VoidType.Instance));
                _sessionHandler.ProcessShutdown(null, false);
            });

            return VoidType.Instance;
        }
    }
}
