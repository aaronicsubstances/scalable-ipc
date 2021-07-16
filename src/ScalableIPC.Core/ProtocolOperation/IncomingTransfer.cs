using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ScalableIPC.Core.ProtocolOperation
{
    internal class IncomingTransfer
    {
        public GenericNetworkIdentifier RemoteEndpoint { get; set; }
        public string MessageId { get; set; }
        public string MessageSrcId { get; set; }
        public int BytesRemaining { get; set; }
        public bool Processed { get; set; }
        public long ProcessedAt { get; set; }
        public short ProcessingErrorCode { get; set; }
        public CancellationHandle ReceiveDataTimeoutId { get; set; }
        public MemoryStream ReceiveBuffer { get; set; }
        public int ExpectedSequenceNumber { get; set; }
        public ProtocolDatagram LastAckSent { get; set; }

        public void EnsureLastAckSentExists()
        {
            if (LastAckSent == null)
            {
                LastAckSent = new ProtocolDatagram
                {
                    OpCode = ExpectedSequenceNumber == 0 ? ProtocolDatagram.OpCodeHeaderAck :
                        ProtocolDatagram.OpCodeDataAck,
                    Version = ProtocolDatagram.ProtocolVersion1_0,
                    MessageId = MessageId,
                    MessageSourceId = MessageSrcId,
                    SequenceNumber = ExpectedSequenceNumber,
                    ErrorCode = ProcessingErrorCode,
                };
            }
        }
    }
}
