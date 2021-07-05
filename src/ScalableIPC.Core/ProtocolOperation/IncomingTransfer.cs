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
        public short ProcessingErrorCode { get; set; } 
        public object ExpirationTimeout { get; set; }
        public object ReceiveTimeout { get; set; }
        public MemoryStream ReceiveBuffer { get; set; }
        public int ExpectedSequenceNumber { get; set; }
        public ProtocolDatagram LastAckSent { get; set; }
    }
}
