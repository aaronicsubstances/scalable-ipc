using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core
{
    public class ProtocolEndpointManager: IProtocolEndpointManager
    {
        public const int MinimumMessageSizeLimit = 65_536;
        public const int MinimumPduSizeLimit = 512;

        public ProtocolEndpointManager()
        {
            SessionId = ByteUtils.GenerateUuid();
        }
        public string SessionId { get; }
        public int PduSizeLimit { get; set; }
        public int MessageSizeLimit { get; set; }
        public int MinimumRetryBackoffPeriod { get; set; }
        public int MaximumRetryBackoffPeriod { get; set; }
        public int AckReceiveTimeout { get; set; }
        public int DataReceiveTimeout { get; set; }
        public int ProcessedMessageDisposalWaitTime { get; set; }
        public bool UsePduTimestamp { get; set; }
        public IEndpointEventListener EndpointEventListener { get; set; }
        public AbstractTransportApi UnderlyingTransport { get; set; }
        public AbstractEventLoopApi EventLoop { get; set; }

        public string BeginSend(GenericNetworkIdentifier remoteEndpoint,
            byte[] data, int offset, int length, Action<ProtocolOperationException> cb)
        {
            string messageId = ByteUtils.GenerateUuid();
            return messageId;
        }

        public void BeginReceive(GenericNetworkIdentifier remoteEndpoint,
            byte[] data, int offset, int length, Action<ProtocolOperationException> cb)
        {

        }

        public void Reset(ProtocolOperationException causeOfReset)
        {

        }
    }
}
