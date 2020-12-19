using ScalableIPC.Core.Session;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Abstractions
{
    public interface ISessionHandler
    {
        void CompleteInit(string sessionId, bool configureForInitialSend,
            AbstractNetworkApi networkApi, GenericNetworkIdentifier remoteEndpoint);
        AbstractNetworkApi NetworkApi { get; }
        GenericNetworkIdentifier RemoteEndpoint { get; }
        ISessionTaskExecutor TaskExecutor { get; }
        string SessionId { get; }
        AbstractPromise<VoidType> ProcessReceiveAsync(ProtocolDatagram datagram);
        AbstractPromise<VoidType> ProcessSendAsync(ProtocolMessage message);
        AbstractPromise<VoidType> CloseAsync(bool closeGracefully);
        AbstractPromise<VoidType> FinaliseDisposeAsync(SessionDisposedException cause);
        int MaxReceiveWindowSize { get; set; }
        int MaxSendWindowSize { get; set; }
        int MaximumTransferUnitSize { get; set; }
        int MaxRetryCount { get; set; }
        int AckTimeoutSecs { get; set; }
        int IdleTimeoutSecs { get; set; }
        int MinRemoteIdleTimeoutSecs { get; set; }
        int MaxRemoteIdleTimeoutSecs { get; set; }
    }
}
