using ScalableIPC.Core.ErrorHandling;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.ProtocolOperation
{
    internal class OutgoingTransfer
    {
        public GenericNetworkIdentifier RemoteEndpoint { get; set; }
        public string MessageId { get; set; }
        public byte[] Data { get; set; }
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public Action<ProtocolException> MessageSendCallback { get; set; }
        public object RetryBackoffTimeoutId { get; set; }
        public object ReceiveAckTimeoutId { get; set; }
        public CancellationHandle SendCancellationHandle { get; set; }
        public int ReceiveAckTimeout { get; set; }
        public string MessageDestinationId { get; set; }
        public int PendingDataLengthToSend { get; set; }
        public int PendingSequenceNumber { get; set; }
        public int PduDataSize { get; set; }
    }
}
