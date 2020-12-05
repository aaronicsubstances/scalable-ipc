using ScalableIPC.Core.Session;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface IEndpointHandler
    {
        GenericNetworkIdentifier LocalEndpoint { get; set; }
        AbstractPromiseApi PromiseApi { get; set; }
        AbstractNetworkApi NetworkApi { get; set; }
        int IdleTimeoutSecs { get; set; } // non-positive means disable idle timer 
        int MinRemoteIdleTimeoutSecs { get; set; }
        int MaxRemoteIdleTimeoutSecs { get; set; }
        int AckTimeoutSecs { get; set; } // non-positive means disable ack timer
        int MaxSendWindowSize { get; set; } // non-positive means use 1.
        int MaxReceiveWindowSize { get; set; } // non-positive means use 1.
        int MaxRetryCount { get; set; } // non-positive means disable retries.
        int MaximumTransferUnitSize { get; set; } // bounded between 512 and UDP max payload size.
        AbstractPromise<VoidType> HandleReceiveAsync(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram message);
        void RequestSend(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb);
        void RequestSessionDispose(GenericNetworkIdentifier remoteEndpoint, string sessionId, SessionDisposedException cause);
        AbstractPromise<VoidType> FinalizeSessionDisposeAsync(GenericNetworkIdentifier remoteEndpoint, string sessionId,
            SessionDisposedException cause);
        AbstractPromise<VoidType> FinalizeSessionsDisposeAsync(GenericNetworkIdentifier remoteEndpoint, SessionDisposedException cause);
    }
}
