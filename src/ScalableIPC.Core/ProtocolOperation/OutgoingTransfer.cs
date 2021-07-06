using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.ProtocolOperation
{
    internal class OutgoingTransfer
    {
        public GenericNetworkIdentifier RemoteEndpoint { get; set; }
        public string MessageId { get; set; }
        public string MessageDestId { get; set; }
        public ProtocolDatagram PendingPdu { get; set; }
        public byte[] Data { get; set; }
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public Action<ProtocolOperationException> SendCallback { get; set; }
        public object RetryBackoffTimeout { get; set; }
        public object ReceiveAckTimeout { get; set; }
        public CancellationHandle SendCancellationHandle { get; set; }
    }
}
