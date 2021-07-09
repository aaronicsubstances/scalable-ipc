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

        public void BeginReceive(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram pdu)
        {
            string message = Encoding.UTF8.GetString(pdu.Data, pdu.DataOffset, pdu.DataLength);
            logs.Add($"{eventLoop.CurrentTimestamp}:received from {remoteEndpoint.HostName}:{message}");
        }
    }
}
