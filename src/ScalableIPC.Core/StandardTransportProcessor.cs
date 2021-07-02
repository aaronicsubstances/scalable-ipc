using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("ScalableIPC.Core.UnitTests")]

namespace ScalableIPC.Core
{
    public class StandardTransportProcessor: IStandardTransportProcessor
    {
        public const int MinimumMessageSizeLimit = 65_536;
        public const int MinimumPduSizeLimit = 512;

        public StandardTransportProcessor()
        {
            EndpointOwnerId = ByteUtils.GenerateUuid();
        }

        public string EndpointOwnerId { get; }
        public int PduSizeLimit { get; set; }
        public int MessageSizeLimit { get; set; }
        public int MinRetryBackoffPeriod { get; set; }
        public int MaxRetryBackoffPeriod { get; set; }
        public int AckReceiveTimeout { get; set; }
        public int DataReceiveTimeout { get; set; }
        public int ProcessedMessageDisposalWaitTime { get; set; }
        public bool UsePduTimestamp { get; set; }
        public StandardTransportProcessorEventListener EventListener { get; set; }
        public TransportApi UnderlyingTransport { get; set; }
        public EventLoopApi EventLoop { get; set; }

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
