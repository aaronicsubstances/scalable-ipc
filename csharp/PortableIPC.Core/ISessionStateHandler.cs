using System;

namespace PortableIPC.Core
{
    internal interface ISessionStateHandler
    {
        bool ProcessErrorReceive();
        bool ProcessReceive(ProtocolDatagram message, AbstractPromiseOnHold<VoidType> promiseOnHold);
        bool ProcessSend(ProtocolDatagram message, AbstractPromiseOnHold<VoidType> promiseOnHold);
        bool ProcessSendData(byte[] rawData, AbstractPromiseOnHold<VoidType> promiseOnHold);
        void Close(Exception error, bool timeout);
    }
}