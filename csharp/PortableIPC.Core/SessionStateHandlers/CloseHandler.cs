using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.SessionStateHandlers
{
    public class CloseHandler: ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;

        public CloseHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
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
