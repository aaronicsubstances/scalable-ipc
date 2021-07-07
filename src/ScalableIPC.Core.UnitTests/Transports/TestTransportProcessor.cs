using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.UnitTests.Transports
{
    public class TestTransportProcessor : TransportApiCallbacks
    {
        private readonly List<string> logs;
        private readonly EventLoopApi eventLoop;
        public TestTransportProcessor(List<string> logs, EventLoopApi eventLoop)
        {
            this.logs = logs;
            this.eventLoop = eventLoop;
        }
        public void BeginReceive(GenericNetworkIdentifier remoteEndpoint, 
            byte[] data, int offset, int length)
        {
            string message = Encoding.UTF8.GetString(data, offset, length);
            logs.Add($"{eventLoop.CurrentTimestamp}:received from {remoteEndpoint.HostName}:{message}");
        }

        public void BeginReceive(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram pdu)
        {
            throw new NotImplementedException();
        }
    }
}
