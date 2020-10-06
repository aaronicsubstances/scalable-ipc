using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace PortableIPC.Core
{
    public class ProtocolSessionHandler : ISessionHandler
    {
        public ProtocolEndpointHandler EndpointHandler { get; set; }
        public IPEndPoint ConnectedEndpoint { get; set; }
        public string SessionId { get; set; }

        public bool IsClosed { get; private set; }
        public int MaxPduSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int WindowSize { get; set; }
        public int IdleTimeoutSecs { get; set; }
        public int AckTimeoutSecs { get; set; }

        public AbstractPromise<VoidType> Close(Exception error, bool timeout)
        {
            throw new NotImplementedException();
        }

        public AbstractPromise<VoidType> ProcessErrorReceive()
        {
            throw new NotImplementedException();
        }

        public AbstractPromise<VoidType> ProcessReceive(ProtocolDatagram message)
        {
            throw new NotImplementedException();
        }

        public AbstractPromise<VoidType> ProcessSend(ProtocolDatagram message)
        {
            throw new NotImplementedException();
        }

        public AbstractPromise<VoidType> ProcessSendData(byte[] rawData)
        {
            throw new NotImplementedException();
        }

        public U RunSerially<T, U>(T arg, Func<T, U> cb)
        {
            lock (this)
            {
                return cb.Invoke(arg);
            }
        }

        public void RunStateCallbackSerially<T>(IStoredCallback<T> cb)
        {
            RunSerially(0, _ =>
            {
                if (!IsClosed)
                {
                    cb.Run();
                }
                return 0;
            });
        }

        public void ResetAckTimeout<T>(int timeoutSecs, IStoredCallback<T> cb)
        {
            throw new NotImplementedException();
        }

        public void ResetIdleTimeout()
        {
            throw new NotImplementedException();
        }

        public AbstractPromise<VoidType> HandleClosing(Exception error, bool timeout)
        {
            throw new NotImplementedException();
        }
    }
}
