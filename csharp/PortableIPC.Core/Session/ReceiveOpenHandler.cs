using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            // nothing to do
        }

        public bool ProcessReceive(ProtocolDatagram message)
        {
            // check opcode.
            if (message.OpCode != ProtocolDatagram.OpCodeOpen)
            {
                return false;
            }

            throw new NotImplementedException();
        }

        public bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options, 
            PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }
    }
}
