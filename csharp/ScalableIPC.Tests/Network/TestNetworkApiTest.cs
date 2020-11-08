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
    public class TestNetworkApiTest
    {
        private readonly TestNetworkApi _networkApi;
        private readonly IEndpointHandler _localEndpointHandler;
        private readonly IPEndPoint _localEndpoint;
        private readonly IPEndPoint _remoteEndpoint1;

        public TestNetworkApiTest()
        {
            _networkApi = new TestNetworkApi();
            _localEndpointHandler = new ProtocolEndpointHandler(_networkApi.PromiseApi, new DefaultEventLoopApi());
            _localEndpoint = new IPEndPoint(new IPAddress(new byte[] { 192, 0, 0, 1 }), 30);
            _networkApi.RemoteEndpointHandlers.Add(_localEndpoint,
                _localEndpointHandler);

            _remoteEndpoint1 = new IPEndPoint(new IPAddress(new byte[] { 192, 0, 0, 2 }), 30);
            _networkApi.RemoteEndpointHandlers.Add(_remoteEndpoint1,
                new ProtocolEndpointHandler(_networkApi.PromiseApi, new DefaultEventLoopApi()));
            _networkApi.CompleteInit();
            _networkApi.RemoteEndpointHandlers[_remoteEndpoint1].EndpointConfig.SessionHandlerFactory =
                new DefaultSessionHandlerFactory(typeof(TestSessionHandler));
        }

        [Fact]
        public async Task TrialTest()
        {
            ISessionHandler sessionHandler = new TestSessionHandler();
            var pendingPromise = _localEndpointHandler.OpenSession(_remoteEndpoint1, Guid.NewGuid().ToString("n"), 
                sessionHandler);
            await ((DefaultPromise<VoidType>)pendingPromise).WrappedTask;

            var dataToSend = ProtocolDatagram.ConvertStringToBytes("Hello");
            var message = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeOpen,
                DataBytes = dataToSend,
                DataLength = dataToSend.Length,
                IsLastOpenRequest = true
            };
            pendingPromise = sessionHandler.ProcessSend(message);
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

            pendingPromise = sessionHandler.Shutdown(null, false);
            await ((DefaultPromise<VoidType>)pendingPromise).WrappedTask;
        }
    }
}
