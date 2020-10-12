using System;
using System.Collections.Generic;

namespace PortableIPC.Core.Abstractions
{
    public interface ISessionStateHandler
    {
        void Shutdown(Exception error);
        bool ProcessReceive(ProtocolDatagram message);
        bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb);
        bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options, 
            PromiseCompletionSource<VoidType> promiseCb);
    }
}