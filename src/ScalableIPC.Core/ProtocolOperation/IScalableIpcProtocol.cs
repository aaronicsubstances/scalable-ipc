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
        int PduSizeLimit { get; set; }
        int MessageSizeLimit { get; set; }
        int MinRetryBackoffPeriod { get; set; }
        int MaxRetryBackoffPeriod { get; set; }
        int AckReceiveTimeout { get; set; }
        int DataReceiveTimeout { get; set; }
        int ProcessedMessageDisposalWaitTime { get; set;}
        bool UsePduTimestamp { get; set; }
        ScalableIpcProtocolListener EventListener { get; set; }
        TransportApi UnderlyingTransport { get; set; }
        EventLoopApi EventLoop { get; set; }
        string BeginSend(GenericNetworkIdentifier remoteEndpoint,
            byte[] data, int offset, int length, Action<ProtocolOperationException> cb);
        void Reset(ProtocolOperationException causeOfReset);
    }
}
