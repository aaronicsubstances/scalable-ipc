﻿using System;

namespace PortableIPC.Core.Abstractions
{
    public interface ISessionStateHandler
    {
        bool ProcessErrorReceive();
        bool ProcessReceive(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb);
        bool ProcessSend(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb);
        bool ProcessSendData(byte[] rawData, AbstractPromiseCallback<VoidType> promiseCb);
        void Shutdown(Exception error, bool timeout);
    }
}