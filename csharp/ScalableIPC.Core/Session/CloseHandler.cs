using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class CloseHandler : ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;
        private readonly AbstractPromise<VoidType> _voidReturnPromise;

        public CloseHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
            _voidReturnPromise = _sessionHandler.NetworkInterface.PromiseApi.Resolve(VoidType.Instance);
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

            _sessionHandler.Log("7b79fdc3-e704-4dba-8e92-1621b78c4e18", message,
                "Pdu received for processing in close handler");
            ProcessReceiveClose(message);
            return true;
        }

        public bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            if (message.OpCode != ProtocolDatagram.OpCodeClose)
            {
                return false;
            }

            _sessionHandler.Log("1469844c-255b-4b44-bd54-0578310798c8", message,
                "Pdu accepted for sending in close handler");
            ProcessSendClose(message, promiseCb);
            return true;
        }

        private void ProcessReceiveClose(ProtocolDatagram message)
        {
            // process termination message regardless of session state.
            var error = new SessionCloseException(SessionCloseException.ReasonCloseReceived,
                message.Options?.ErrorCode);
            _sessionHandler.InitiateClose(error);
        }

        private void ProcessSendClose(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            // process termination message regardless of session state.
            _sessionHandler.Log("6e462e36-a9b9-4ea3-8735-c389e3dd0d36", "Sending closing message");

            // send but ignore errors.
            _sessionHandler.NetworkInterface.HandleSendAsync(_sessionHandler.RemoteEndpoint, message)
                .CatchCompose(_ => _voidReturnPromise)
                .Then(_ => HandleSendSuccessOrError(promiseCb));
        }

        private VoidType HandleSendSuccessOrError(PromiseCompletionSource<VoidType> promiseCb)
        {            
            _sessionHandler.EventLoop.PostCallback(() =>
            {
                _sessionHandler.Log("63a2eff5-d376-44c9-8d98-fd752f4a0c7b", 
                    "Shutting down after sending closing message");

                promiseCb.CompleteSuccessfully(VoidType.Instance);
                _sessionHandler.InitiateClose(new SessionCloseException());
            });

            return VoidType.Instance;
        }
    }
}
