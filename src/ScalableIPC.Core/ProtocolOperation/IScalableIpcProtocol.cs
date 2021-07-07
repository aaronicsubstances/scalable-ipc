using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.ProtocolOperation
{
    /// <summary>
    /// Purpose of this interface is to guide porting to other programming languages by differentiating between
    /// common properties and methods and implementation-specific helper properties and methods.
    /// </summary>
    internal interface IScalableIpcProtocol: TransportApiCallbacks
    {
        string EndpointOwnerId { get; }
        int MaximumPduDataSize { get; set; }
        int MaximumReceivableMessageLength { get; set; }
        int MinRetryBackoffPeriod { get; set; }
        int MaxRetryBackoffPeriod { get; set; }
        int DefaultAckTimeout { get; set; }
        int DataReceiveTimeout { get; set; }
        int ProcessedMessageDisposalWaitTime { get; set; }
        int KnownMessageDestinationLifeTime { get; set; }
        bool VaryMessageSourceIds { get; set; }
        ScalableIpcProtocolListener EventListener { get; set; }
        TransportApi UnderlyingTransport { get; set; }
        EventLoopApi EventLoop { get; set; }
        void BeginSend(GenericNetworkIdentifier remoteEndpoint, ProtocolMessage msg, MessageSendOptions options,
            Action<ProtocolOperationException> cb);
        void Reset(ProtocolOperationException causeOfReset);
    }
}
