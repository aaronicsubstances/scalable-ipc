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
        public Action<ProtocolOperationException> SendCallback { get; set; }
        public object RetryBackoffTimeoutId { get; set; }
        public object ReceiveAckTimeoutId { get; set; }
        public CancellationHandle SendCancellationHandle { get; set; }
        public int AckTimeout { get; set; }
        public string MessageDestinationId { get; set; }
        public int DataLengthToSend { get; set; }
        public int PendingSequenceNumber { get; set; }
    }
}
