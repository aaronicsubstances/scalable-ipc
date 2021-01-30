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
        AbstractPromise<VoidType> CloseAsync();
        AbstractPromise<VoidType> CloseAsync(bool closeGracefully);
        AbstractPromise<VoidType> FinaliseDisposeAsync(SessionDisposedException cause);

        int IdleTimeout { get; set; } // non-positive means disable idle timer 
        int MinRemoteIdleTimeout { get; set; }
        int MaxRemoteIdleTimeout { get; set; }
        int MaxRetryPeriod { get; set; } // non-positive means disable retries by total retry time.
        int MaxSendWindowSize { get; set; } // non-positive means use 1.
        int MaxReceiveWindowSize { get; set; } // non-positive means use 1.
        int MaxRetryCount { get; set; } // negative means use 0.
        int MaximumTransferUnitSize { get; set; } // bounded between 512 and datagram max size.
    }
}
