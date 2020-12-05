using ScalableIPC.Core;
using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using ScalableIPC.Core.Networks;
using ScalableIPC.Core.Session;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.Tests.Core.Networks
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
                IdleTimeoutSecs = 5,
                AckTimeoutSecs = 3
            };

            _kumasiAddr = new GenericNetworkIdentifier { HostName = "kumasi" };
            _kumasiEndpoint = new MemoryNetworkApi
            {
                LocalEndpoint = _kumasiAddr,
                SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(TestSessionHandler)),
                IdleTimeoutSecs = 5,
                AckTimeoutSecs = 3
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
        class TestSessionHandler : DefaultSessionHandler
        {
            public TestSessionHandler()
            {

                MessageReceived += (_, e) =>
                {
                    string dataMessage = ProtocolDatagram.ConvertBytesToString(e.Message.DataBytes, e.Message.DataOffset,
                        e.Message.DataLength);
                    CustomLoggerFacade.Log(() => new CustomLogEvent("71931970-3923-4472-b110-3449141998e3",
                        $"Received data: {dataMessage}", null));
                };

                SessionDisposed += (_, e) =>
                {
                    CustomLoggerFacade.Log(() => new CustomLogEvent("06f62330-a218-4667-9df5-b8851fed628a",
                           $"Received close", e.Cause));
                };
            }
        }
    }
}
