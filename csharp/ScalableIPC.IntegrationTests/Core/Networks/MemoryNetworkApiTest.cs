﻿using ScalableIPC.Core;
using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using ScalableIPC.Core.Helpers;
using ScalableIPC.Core.Networks;
using ScalableIPC.Core.Networks.Common;
using ScalableIPC.Core.Session;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.IntegrationTests.Core.Networks
{
    public class MemoryNetworkApiTest
    {
        private readonly MemoryNetworkApi _accraEndpoint, _kumasiEndpoint;
        private readonly GenericNetworkIdentifier _accraAddr, _kumasiAddr;

        public MemoryNetworkApiTest()
        {
            _accraAddr = new GenericNetworkIdentifier { HostName = "accra" };
            _accraEndpoint = new MemoryNetworkApi
            {
                LocalEndpoint = _accraAddr,
                SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(TestSessionHandler)),
                IdleTimeout = 5000,
                AckTimeout = 3000
            };

            _kumasiAddr = new GenericNetworkIdentifier { HostName = "kumasi" };
            _kumasiEndpoint = new MemoryNetworkApi
            {
                LocalEndpoint = _kumasiAddr,
                SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(TestSessionHandler)),
                IdleTimeout = 5000,
                AckTimeout = 3000
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
            var message = new ProtocolMessage
            {
                DataBytes = dataToSend,
                DataLength = dataToSend.Length
            };
            var pendingPromise = sessionHandler.ProcessSendAsync(message);
            await ((DefaultPromise<VoidType>)pendingPromise).WrappedTask;

            dataToSend = ProtocolDatagram.ConvertStringToBytes(" from Accra.");
            message = new ProtocolMessage
            {
                DataBytes = dataToSend,
                DataLength = dataToSend.Length
            };
            pendingPromise = sessionHandler.ProcessSendAsync(message);
            await ((DefaultPromise<VoidType>)pendingPromise).WrappedTask;

            pendingPromise = sessionHandler.CloseAsync();
            await ((DefaultPromise<VoidType>)pendingPromise).WrappedTask;
        }
        class TestSessionHandler : DefaultSessionHandler
        {
            public TestSessionHandler()
            {

                MessageReceivedHandler = (_, m) =>
                {
                    string dataMessage = ProtocolDatagram.ConvertBytesToString(m.DataBytes, m.DataOffset,
                        m.DataLength);
                    CustomLoggerFacade.Log(() => new CustomLogEvent($"Received data: {dataMessage}", null));
                };

                SessionDisposedHandler = (_, ex) =>
                {
                    CustomLoggerFacade.Log(() => new CustomLogEvent("Received session dispose event", ex));
                };
            }
        }
    }
}
