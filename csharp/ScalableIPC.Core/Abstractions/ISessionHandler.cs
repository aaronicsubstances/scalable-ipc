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
        AbstractPromise<bool> ProcessSendWithoutAckAsync(ProtocolMessage message);
        AbstractPromise<VoidType> CloseAsync();
        AbstractPromise<VoidType> CloseAsync(bool closeGracefully);
        AbstractPromise<VoidType> FinaliseDisposeAsync(SessionDisposedException cause);

        int IdleTimeout { get; set; } // non-positive means disable idle timer 
        int MinRemoteIdleTimeout { get; set; }
        int MaxRemoteIdleTimeout { get; set; }
        int MaxRetryPeriod { get; set; } // non-positive means disable retries by total retry time.
        int MaxWindowSize { get; set; } // non-positive means use 1.
        int MaxRemoteWindowSize { get; set; } // non-positive means ignore it.
        int MaxRetryCount { get; set; } // negative means use 0.
                                        
        // ranges from 0 to 1. non-positive means always ignore. 
        // 1 or more means always send.
        double FireAndForgetSendProbability { get; set; }
    }
}
