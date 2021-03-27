﻿using ScalableIPC.Core.Session.Abstractions;
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
            if (datagram.OpCode == ProtocolDatagram.OpCodeEnquireLink ||
                datagram.OpCode == ProtocolDatagram.OpCodeEnquireLinkAck)
            {
                OnReceiveRequest(datagram);
                return true;
            }
            else
            {
                return false;
            }
        }

        public void OnReceiveRequest(ProtocolDatagram datagram)
        {
            if (datagram.OpCode == ProtocolDatagram.OpCodeEnquireLink)
            {
                if (_sessionHandler.State != SessionState.Opened)
                {
                    _sessionHandler.RaiseReceiveError(datagram, "a71359dc-cc8f-440d-979f-ecdd9f83faa3: " +
                        "enquire link pdu received outside opened state");
                    return;
                }
                var replyDatagram = new ProtocolDatagram
                {
                    SessionId = _sessionHandler.SessionId,
                    OpCode = ProtocolDatagram.OpCodeEnquireLinkAck
                };
                _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint,
                    replyDatagram, null, null);
            }
            else if (datagram.OpCode == ProtocolDatagram.OpCodeEnquireLinkAck)
            {
                if (_sessionHandler.State == SessionState.Opening)
                {
                    _sessionHandler.RaiseReceiveError(datagram, "0819b8bf-0c75-4113-b58f-81a1423d15c3: " +
                        "enquire link ack received in opening state");
                    return;
                }
                if (_sessionHandler.State >= SessionState.Closed)
                {
                    _sessionHandler.RaiseReceiveError(datagram, "a71359dc-cc8f-440d-979f-ecdd9f83faa3: " +
                        "enquire link ack received in closed aftermath state");
                    return;
                }
                ProcessAck(datagram);
            }
            else
            {
                throw new Exception("unexpected op code: " + datagram.OpCode);
            }
        }

        private void ProcessAck(ProtocolDatagram datagram)
        {
            int enquireLinkErrorCode = ProtocolOperationException.FetchExpectedErrorCode(datagram);
            if (enquireLinkErrorCode < 0)
            {
                _sessionHandler.RaiseReceiveError(datagram, "1d521c3b-1b30-41b2-81a4-5561f4f55fb1: " +
                    "received enquire link ack pdu with invalid error code");
                return;
            }

            if (enquireLinkErrorCode == 0)
            {
                _sessionHandler.OnEnquireLinkSuccess(datagram);
            }
            else
            {
                _sessionHandler.InitiateDispose(new ProtocolOperationException(enquireLinkErrorCode));
            }
        }
    }
}