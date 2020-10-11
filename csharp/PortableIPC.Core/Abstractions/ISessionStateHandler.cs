using System;
using System.Collections.Generic;

namespace PortableIPC.Core.Abstractions
{
    public interface ISessionStateHandler
    {
        bool ProcessReceive(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb);
        bool ProcessSend(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb);
        bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options, 
            AbstractPromiseCallback<VoidType> promiseCb);
        void Shutdown(Exception error);
    }
}