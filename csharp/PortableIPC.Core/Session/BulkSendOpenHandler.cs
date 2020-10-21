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

        public BulkSendOpenHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public bool SendInProgress { get; set; }

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
