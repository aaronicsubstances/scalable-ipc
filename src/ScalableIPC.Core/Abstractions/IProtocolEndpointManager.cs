using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface IProtocolEndpointManager
    {
        string SessionId { get; }
        int PduSizeLimit { get; set; }
        int MessageSizeLimit { get; set; }
        int MinimumRetryBackoffPeriod { get; set; }
        int MaximumRetryBackoffPeriod { get; set; }
        int AckReceiveTimeout { get; set; }
        int DataReceiveTimeout { get; set; }
        int ProcessedMessageDisposalWaitTime { get; set;}
        bool UsePduTimestamp { get; set; }
        IEndpointEventListener EndpointEventListener { get; set; }
        AbstractTransportApi UnderlyingTransport { get; set; }
        AbstractEventLoopApi EventLoop { get; set; }
        string BeginSend(GenericNetworkIdentifier remoteEndpoint,
            byte[] data, int offset, int length, Action<ProtocolOperationException> cb);
        void BeginReceive(GenericNetworkIdentifier remoteEndpoint,
            byte[] data, int offset, int length, Action<ProtocolOperationException> cb);
        void Reset(ProtocolOperationException causeOfReset);
    }
}
