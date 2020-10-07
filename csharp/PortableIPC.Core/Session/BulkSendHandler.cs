using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Session
{
    public class BulkSendHandler: ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;
        private readonly SendHandler _sendHandler;

        public BulkSendHandler(ISessionHandler sessionHandler, SendHandler sendHandler)
        {
            _sessionHandler = sessionHandler;
            _sendHandler = sendHandler;
        }

        public void Close(Exception error, bool timeout)
        {

        }

        public bool ProcessErrorReceive()
        {
            return false;
        }

        public bool ProcessReceive(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSend(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSendData(byte[] rawData, AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }
    }
}
