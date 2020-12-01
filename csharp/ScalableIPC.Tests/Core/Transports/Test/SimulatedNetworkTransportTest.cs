using ScalableIPC.Core;
using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.ConcreteComponents;
using ScalableIPC.Core.Transports.Test;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.Tests.Core.Transports.Test
{
    public class SimulatedNetworkTransportTest
    {
        private readonly SimulatedNetworkTransport _localEndpoint;
        private readonly GenericNetworkIdentifier _remoteAddr1;

        public SimulatedNetworkTransportTest()
        {
            var localAddr = new GenericNetworkIdentifier { HostName = "local" };
            _localEndpoint = new SimulatedNetworkTransport
            {
                LocalEndpoint = localAddr,
                SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(TestSessionHandler))
            };

            _remoteAddr1 = new GenericNetworkIdentifier { HostName = "remote1" };
            var remoteEndpoint = new SimulatedNetworkTransport
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
