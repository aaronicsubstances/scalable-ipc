using ScalableIPC.Core;
using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.ConcreteComponents;
using ScalableIPC.Tests.ConcreteComponents;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.Tests.Network
{
    public class NetworkTransportTest
    {
        private readonly TestNetworkTransport _localEndpoint;
        private readonly IPEndPoint _remoteAddr1;

        public NetworkTransportTest()
        {
            var localAddr = new IPEndPoint(new IPAddress(new byte[] { 192, 0, 0, 1 }), 30);
            _localEndpoint = new TestNetworkTransport
            {
                LocalEndpoint = localAddr
            };

            _remoteAddr1 = new IPEndPoint(new IPAddress(new byte[] { 192, 0, 0, 2 }), 30);
            var remoteEndpoint = new TestNetworkTransport
            {
                LocalEndpoint = _remoteAddr1
            };
            _localEndpoint.ConnectedNetworks.Add(_remoteAddr1, remoteEndpoint);
            remoteEndpoint.ConnectedNetworks.Add(localAddr, _localEndpoint);
        }

        [Fact]
        public async Task TrialTest()
        {
            var openPromise = _localEndpoint.OpenSession(_remoteAddr1, Guid.NewGuid().ToString("n"),
                new TestSessionHandler());
            var sessionHandler = await ((DefaultPromise<ISessionHandler>)openPromise).WrappedTask;

            var dataToSend = ProtocolDatagram.ConvertStringToBytes("Hello");
            var message = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                DataBytes = dataToSend,
                DataLength = dataToSend.Length
            };
            var pendingPromise = sessionHandler.ProcessSend(message);
            await ((DefaultPromise<VoidType>)pendingPromise).WrappedTask;

            dataToSend = ProtocolDatagram.ConvertStringToBytes(" World!");
            message = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                DataBytes = dataToSend,
                DataLength = dataToSend.Length
            };
            pendingPromise = sessionHandler.ProcessSend(message);
            await ((DefaultPromise<VoidType>)pendingPromise).WrappedTask;

            pendingPromise = sessionHandler.Close(null, false);
            await ((DefaultPromise<VoidType>)pendingPromise).WrappedTask;
        }
    }
}
