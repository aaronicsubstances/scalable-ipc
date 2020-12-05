using ScalableIPC.Core;
using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using ScalableIPC.Core.Session;
using ScalableIPC.Core.Networks.Test;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.Tests.Core.Networks.Test
{
    public class SimulatedNetworkTransportTest
    {
        private readonly SimulatedNetworkTransport _accraEndpoint, _kumasiEndpoint;
        private readonly GenericNetworkIdentifier _accraAddr, _kumasiAddr;

        public SimulatedNetworkTransportTest()
        {
            _accraAddr = new GenericNetworkIdentifier { HostName = "accra" };
            _accraEndpoint = new SimulatedNetworkTransport
            {
                LocalEndpoint = _accraAddr,
                SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(TestSessionHandler))
            };

            _kumasiAddr = new GenericNetworkIdentifier { HostName = "kumasi" };
            _kumasiEndpoint = new SimulatedNetworkTransport
            {
                LocalEndpoint = _kumasiAddr,
                SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(TestSessionHandler))
            };
            _accraEndpoint.ConnectedNetworks.Add(_kumasiAddr, _kumasiEndpoint);
            _kumasiEndpoint.ConnectedNetworks.Add(_accraAddr, _accraEndpoint);
        }

        [Fact]
        public async Task TrialTest()
        {
            var openPromise = _accraEndpoint.OpenSessionAsync(_kumasiAddr, Guid.NewGuid().ToString("n"),
                new TestSessionHandler());
            var sessionHandler = await ((DefaultPromise<ISessionHandler>)openPromise).WrappedTask;

            var dataToSend = ProtocolDatagram.ConvertStringToBytes("Hello");
            var message = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                DataBytes = dataToSend,
                DataLength = dataToSend.Length
            };
            var pendingPromise = sessionHandler.ProcessSendAsync(message);
            await ((DefaultPromise<VoidType>)pendingPromise).WrappedTask;

            dataToSend = ProtocolDatagram.ConvertStringToBytes(" from Accra.");
            message = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                DataBytes = dataToSend,
                DataLength = dataToSend.Length
            };
            pendingPromise = sessionHandler.ProcessSendAsync(message);
            await ((DefaultPromise<VoidType>)pendingPromise).WrappedTask;

            pendingPromise = sessionHandler.CloseAsync();
            await ((DefaultPromise<VoidType>)pendingPromise).WrappedTask;
        }
    }
}
