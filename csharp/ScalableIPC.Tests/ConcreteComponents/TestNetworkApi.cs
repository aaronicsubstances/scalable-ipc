using ScalableIPC.Core;
using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.ConcreteComponents;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ScalableIPC.Tests.ConcreteComponents
{
    class TestNetworkApi : AbstractNetworkApi
    {
        public TestNetworkApi()
        {
            PromiseApi = new DefaultPromiseApi();
            EventLoop = new DefaultEventLoopApi();
            RemoteEndpointHandlers = new Dictionary<IPEndPoint, ProtocolEndpointHandler>();
            CommonEndpointConfig = new EndpointConfig
            {
                SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(ProtocolSessionHandler)),
                IdleTimeoutSecs = 1,
                AckTimeoutSecs = 3,
                MaxRetryCount = 0,
                MaximumTransferUnitSize = 512,
                MaxReceiveWindowSize = 1,
                MaxSendWindowSize = 1
            };
            ExtraEndpointConfigs = new Dictionary<IPEndPoint, ExtraEndpointConfig>();
        }

        public DefaultPromiseApi PromiseApi { get;}
        public DefaultEventLoopApi EventLoop { get; }
        public Dictionary<IPEndPoint, ProtocolEndpointHandler> RemoteEndpointHandlers { get; }
        public EndpointConfig CommonEndpointConfig { get; }
        public Dictionary<IPEndPoint, ExtraEndpointConfig> ExtraEndpointConfigs { get; }

        public void CompleteInit()
        {
            foreach (var kvp in RemoteEndpointHandlers)
            {
                var remoteEndpointHandler = kvp.Value;
                remoteEndpointHandler.NetworkSocket = this;
                if (remoteEndpointHandler.EndpointConfig == null)
                {
                    remoteEndpointHandler.EndpointConfig = new EndpointConfig
                    {
                        LocalEndpoint = kvp.Key,
                        AckTimeoutSecs = CommonEndpointConfig.AckTimeoutSecs,
                        IdleTimeoutSecs = CommonEndpointConfig.IdleTimeoutSecs,
                        MaximumTransferUnitSize = CommonEndpointConfig.MaximumTransferUnitSize,
                        MaxReceiveWindowSize = CommonEndpointConfig.MaxReceiveWindowSize,
                        MaxRetryCount = CommonEndpointConfig.MaxRetryCount,
                        MaxSendWindowSize = CommonEndpointConfig.MaxSendWindowSize,
                        SessionHandlerFactory = CommonEndpointConfig.SessionHandlerFactory
                    };
                }
                if (!ExtraEndpointConfigs.ContainsKey(kvp.Key))
                {
                    ExtraEndpointConfigs.Add(kvp.Key, new ExtraEndpointConfig
                    {
                        MinTransmissionDelayMs = 0,
                        MaxTransmissionDelayMs = 2,
                    });
                }
            }
        }

        public AbstractPromise<VoidType> HandleSend(IPEndPoint remoteEndpoint, byte[] data, int offset, int length)
        {
            if (RemoteEndpointHandlers.ContainsKey(remoteEndpoint))
            {
                Task.Run(async () =>
                {
                    // Simulate transmission delay here.
                    var extraEndpointConfig = ExtraEndpointConfigs[remoteEndpoint];
                    int transmissionDelayMs = new Random().Next(extraEndpointConfig.MinTransmissionDelayMs, 
                        extraEndpointConfig.MaxTransmissionDelayMs);
                    if (transmissionDelayMs > 0)
                    {
                        await Task.Delay(transmissionDelayMs);
                    }
                    try
                    {
                        var remoteEndpointHandler = RemoteEndpointHandlers[remoteEndpoint];
                        remoteEndpointHandler.HandleReceive(remoteEndpoint, data, offset, length);
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
}
