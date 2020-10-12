using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Session
{
    public class BulkSendOpenHandler : ISessionStateHandler
    {
        private ISessionHandler _sessionHandler;
        private SendOpenHandler _sendHandler;

        public BulkSendOpenHandler(ISessionHandler sessionHandler, SendOpenHandler sendHandler)
        {
            _sessionHandler = sessionHandler;
            _sendHandler = sendHandler;
        }

        public void Shutdown(Exception error)
        {
            throw new NotImplementedException();
        }

        public bool ProcessReceive(ProtocolDatagram message)
        {
            return false;
        }

        public bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options, PromiseCompletionSource<VoidType> promiseCb)
        {
            throw new NotImplementedException();
        }
    }
}
