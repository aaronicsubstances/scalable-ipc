using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class EnquireLinkHandler : ISessionStateHandler
    {
        private readonly IStandardSessionHandler _sessionHandler;

        public EnquireLinkHandler(IStandardSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public bool SendInProgress => false;

        public void Dispose(ProtocolOperationException cause)
        {
            // nothing to do.
        }

        public bool ProcessReceive(ProtocolDatagram datagram)
        {
            if (datagram.OpCode == ProtocolDatagram.OpCodeEnquireLink)
            {
                var replyDatagram = new ProtocolDatagram
                {
                    SessionId = _sessionHandler.SessionId,
                    OpCode = ProtocolDatagram.OpCodeEnquireLinkAck
                };
                _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint,
                    replyDatagram, null, null);
                return true;
            }
            else if (datagram.OpCode == ProtocolDatagram.OpCodeEnquireLinkAck)
            {
                int enquireLinkErrorCode = ProtocolOperationException.FetchExpectedErrorCode(datagram);
                if (enquireLinkErrorCode < 0)
                {
                    _sessionHandler.OnDatagramDiscarded(datagram);
                }
                else if (enquireLinkErrorCode == 0)
                {
                    _sessionHandler.OnEnquireLinkSuccess(datagram);
                }
                else
                {
                    _sessionHandler.InitiateDispose(new ProtocolOperationException(enquireLinkErrorCode));
                }
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
