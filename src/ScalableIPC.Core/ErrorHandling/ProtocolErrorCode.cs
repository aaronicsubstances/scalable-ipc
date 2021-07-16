using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.ErrorHandling
{
    public class ProtocolErrorCode
    {
        private static readonly Dictionary<short, ProtocolErrorCode> instances = new Dictionary<short, ProtocolErrorCode>();

        // protocol ack error codes
        public static readonly ProtocolErrorCode ProcessingError = new ProtocolErrorCode(1, "general processing error");
        public static readonly ProtocolErrorCode InvalidDestinationEndpointId = new ProtocolErrorCode(2, "invalid message destination id");
        public static readonly ProtocolErrorCode MessageTooLarge = new ProtocolErrorCode(3, "max message length exceeded");
        public static readonly ProtocolErrorCode ReceiveTimeout = new ProtocolErrorCode(4, "receive timeout");
        public static readonly ProtocolErrorCode PduTooLarge = new ProtocolErrorCode(5, "max pdu data size exceeded");

        // The following error codes are not meant to be used for network
        // communications. As such they are negative.
        public static readonly ProtocolErrorCode Success = new ProtocolErrorCode(0, "success");
        public static readonly ProtocolErrorCode ApplicationError = new ProtocolErrorCode(-1, "application error");
        public static readonly ProtocolErrorCode Reset = new ProtocolErrorCode(-2, "reset");
        public static readonly ProtocolErrorCode Shutdown = new ProtocolErrorCode(-3, "shutdown");
        public static readonly ProtocolErrorCode SendTimeout = new ProtocolErrorCode(-4, "send timeout");
        public static readonly ProtocolErrorCode AbortedFromSender = new ProtocolErrorCode(-5, "aborted from sender");

        public static ProtocolErrorCode GetInstance(short value)
        {
            if (instances.ContainsKey(value))
            {
                return instances[value];
            }
            return null;
        }

        private ProtocolErrorCode(short value, string description)
        {
            Value = value;
            Description = description;
            instances.Add(value, this);
        }

        public short Value { get; }
        public string Description { get; }
    }
}
