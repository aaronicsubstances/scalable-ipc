﻿using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Session
{
    public class SendOpenHandler : ISessionStateHandler
    {

        public void Shutdown(Exception error)
        {
            throw new NotImplementedException();
        }

        public bool ProcessReceive(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSend(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            throw new NotImplementedException();
        }

        public bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options, 
            AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }
    }
}