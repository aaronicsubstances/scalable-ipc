using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Session
{
    public class ReceiveOpenHandler : ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;

        public ReceiveOpenHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public void Shutdown(Exception error)
        {
            throw new NotImplementedException();
        }

        public bool ProcessReceive(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            throw new NotImplementedException();
        }

        public bool ProcessSend(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options, 
            AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }
    }
}
