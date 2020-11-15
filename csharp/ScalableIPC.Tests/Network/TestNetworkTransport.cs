using ScalableIPC.Core;
using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.ConcreteComponents;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ScalableIPC.Tests.Network
{
    public class TestNetworkTransport : NetworkTransportBase
    {
        public TestNetworkTransport()
        {
            ConnectedNetworks = new Dictionary<IPEndPoint, TestNetworkTransport>();
            SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(TestSessionHandler));
            IdleTimeoutSecs = 5;
            AckTimeoutSecs = 3;
            MaxRetryCount = 0;
            MaximumTransferUnitSize = 512;
            MaxReceiveWindowSize = 1;
            MaxSendWindowSize = 1;
            MinTransmissionDelayMs = 0;
            MaxTransmissionDelayMs = 2;
        }

        public Dictionary<IPEndPoint, TestNetworkTransport> ConnectedNetworks { get; }

        public int MinTransmissionDelayMs { get; set; }
        public int MaxTransmissionDelayMs { get; set; }

        public override AbstractPromise<ISessionHandler> OpenSession(IPEndPoint remoteEndpoint, string sessionId = null,
            ISessionHandler sessionHandler = null)
        {
            if (sessionId == null)
            {
                sessionId = ProtocolDatagram.GenerateSessionId();
            }
            if (sessionHandler == null)
            {
                sessionHandler = SessionHandlerFactory.Create(true);
            }
            sessionHandler.CompleteInit(sessionId, true, this, remoteEndpoint);
            lock (_sessionHandlerStore)
            {
                _sessionHandlerStore.Add(remoteEndpoint, sessionId, new SessionHandlerWrapper
                {
                    SessionHandler = sessionHandler
                });
            }
            return PromiseApi.Resolve(sessionHandler);
        }

        protected override AbstractPromise<VoidType> HandleReceiveOpeningWindowMessage(IPEndPoint remoteEndpoint,
            ProtocolDatagram message)
        {
            // for receipt of window 0, reuse existing session handler or create one and add.
            ISessionHandler sessionHandler;
            lock (_sessionHandlerStore)
            {
                var sessionHandlerWrapper = _sessionHandlerStore.Get(remoteEndpoint, message.SessionId);
                if (sessionHandlerWrapper != null)
                {
                    sessionHandler = sessionHandlerWrapper.SessionHandler;
                }
                else
                {
                    sessionHandler = SessionHandlerFactory.Create(false);
                    sessionHandler.CompleteInit(message.SessionId, true, this, remoteEndpoint);
                    _sessionHandlerStore.Add(remoteEndpoint, message.SessionId, new SessionHandlerWrapper
                    {
                        SessionHandler = sessionHandler
                    });
                }
            }
            return sessionHandler.ProcessReceive(message);
        }

        protected override AbstractPromise<VoidType> HandleSendOpeningWindowMessage(IPEndPoint remoteEndpoint,
            ProtocolDatagram message)
        {
            byte[] data = GenerateRawDatagram(message);
            return HandleSendData(remoteEndpoint, message.SessionId, data, 0, data.Length);
        }

        protected override AbstractPromise<VoidType> HandleSendData(IPEndPoint remoteEndpoint, string sessionId, byte[] data,
            int offset, int length)
        {
            if (ConnectedNetworks.ContainsKey(remoteEndpoint))
            {
                Task.Run(async () =>
                {
                    // Simulate transmission delay here.
                    var connectedNetwork = ConnectedNetworks[remoteEndpoint];
                    int transmissionDelayMs = new Random().Next(connectedNetwork.MinTransmissionDelayMs,
                        connectedNetwork.MaxTransmissionDelayMs);
                    if (transmissionDelayMs > 0)
                    {
                        await Task.Delay(transmissionDelayMs);
                    }
                    try
                    {
                        connectedNetwork.HandleReceive(LocalEndpoint, data, offset, length);
                    }
                    catch (Exception ex)
                    {
                        CustomLoggerFacade.Log(() =>
                            new CustomLogEvent("1dec508c-2d59-4336-8617-30bb71a9a5a8", $"Error occured during message " +
                                $"receipt handling at {remoteEndpoint}", ex));
                    }
                });
                return PromiseApi.Resolve(VoidType.Instance);
            }
            else
            {
                return PromiseApi.Reject(new Exception($"{remoteEndpoint} remote endpoint not found."));
            }
        }
    }
    class TestSessionHandler : ProtocolSessionHandler
    {
        public override void OnDataReceived(byte[] data, Dictionary<string, List<string>> options)
        {
            string dataMessage = ProtocolDatagram.ConvertBytesToString(data, 0, data.Length);
            CustomLoggerFacade.Log(() => new CustomLogEvent("71931970-3923-4472-b110-3449141998e3",
                $"Received data: {dataMessage}", null));
        }
    }

    class SessionHandlerWrapper : ISessionHandlerWrapper
    {
        public ISessionHandler SessionHandler { get; set; }
    }
}
