using ScalableIPC.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core
{
    public class ProtocolDatagram
    {
        public const byte OpCodeData = 0x01;
        public const byte OpCodeDataAck = 0x02;
        public const byte OpCodeHeader = 0x03;
        public const byte OpCodeHeaderAck = 0x04;

        public const byte ProtocolVersion1_0 = 0x10;

        public const int MinDatagramSize = 38;

        public byte OpCode { get; set; }
        public byte Version { get; set; }
        public int Reserved { get; set; }
        public string MessageId { get; set; }
        public string MessageSourceId { get; set; }
        public string MessageDestinationId { get; set; }
        public int MessageLength { get; set; }
        public int SequenceNumber { get; set; }
        public short ErrorCode { get; set; }
        public int DataOffset { get; set; }
        public int DataLength { get;set; }
        public byte[] Data { get; set; }

        public static ProtocolDatagram Deserialize(byte[] rawBytes, int offset, int length)
        {
            // validate arguments.
            if (rawBytes == null)
            {
                throw new ArgumentNullException(nameof(rawBytes), "6ee7a25c-090e-4321-b429-1ed4b26f3c59");
            }
            if (offset < 0)
            {
                throw new ArgumentException("58eacdaa-ae6f-4779-a7bd-ec3d28bbadc8: " +
                    "offset cannot be negative", nameof(offset));
            }
            if (length < 0)
            {
                throw new ArgumentException("02c8ef5c-9e30-4630-a1bc-c9c8dc73cfac: " +
                    "length cannot be negative", nameof(offset));
            }
            if (offset + length > rawBytes.Length)
            {
                throw new ArgumentException("cf0da519-d5e3-4bb3-b6f6-a9cb0db69fa8: " +
                    "combination of offset and length exceeeds byte array size");
            }

            if (length < MinDatagramSize)
            {
                throw new Exception("b451b01f-c474-49e4-ad3f-643d9e849664: " +
                    "datagram too small to be valid");
            }

            int endOffset = offset + length;
            
            ProtocolDatagram parsedDatagram = new ProtocolDatagram();
            
            parsedDatagram.OpCode = rawBytes[offset];
            offset += 1;

            parsedDatagram.Version = rawBytes[offset];
            offset += 1;

            parsedDatagram.Reserved = ByteUtils.DeserializeInt32BigEndian(rawBytes, offset);
            offset += 4;

            parsedDatagram.MessageId = ByteUtils.ConvertBytesToHex(rawBytes, offset, 16);
            offset += 16;

            // remainder of parsing depends on opcode.
            int newOffset = DeserializeRemainderOfHeaderPdu(parsedDatagram, rawBytes, offset, endOffset);
            if (newOffset == offset)
            {
                newOffset = DeserializeRemainderOfHeaderAckPdu(parsedDatagram, rawBytes, offset, endOffset);
            }
            if (newOffset == offset)
            {
                newOffset = DeserializeRemainderOfDataPdu(parsedDatagram, rawBytes, offset, endOffset);
            }
            if (newOffset == offset)
            {
                newOffset = DeserializeRemainderOfDataAckPdu(parsedDatagram, rawBytes, offset, endOffset);
            }

            if (newOffset == offset)
            {
                throw new Exception("1c13f39a-51f0-4f8d-80f3-5b06f6cfb769: Unexpected opcode: " +
                    parsedDatagram.OpCode);
            }
            offset = newOffset;

            if (parsedDatagram.OpCode == OpCodeData || parsedDatagram.OpCode == OpCodeHeader)
            {
                parsedDatagram.Data = rawBytes;
                parsedDatagram.DataOffset = offset;
                parsedDatagram.DataLength = endOffset - offset;
            }

            return parsedDatagram;
        }

        private static int DeserializeRemainderOfHeaderPdu(ProtocolDatagram parsedDatagram, byte[] rawBytes,
            int offset, int endOffset)
        {
            if (parsedDatagram.OpCode == OpCodeHeader)
            {
                parsedDatagram.MessageDestinationId = ByteUtils.ConvertBytesToHex(rawBytes, offset, 16);
                offset += 16;
                if (endOffset - offset < 4)
                {
                    throw new Exception("4121da80-a008-4a7a-a843-aa93656f6a30: " +
                        "datagram too small for header op code");
                }
                parsedDatagram.MessageLength = ByteUtils.DeserializeInt32BigEndian(rawBytes, offset);
                offset += 4;
            }
            return offset;
        }

        private static int DeserializeRemainderOfHeaderAckPdu(ProtocolDatagram parsedDatagram, byte[] rawBytes,
            int offset, int endOffset)
        {
            if (parsedDatagram.OpCode == OpCodeHeaderAck)
            {
                parsedDatagram.MessageSourceId = ByteUtils.ConvertBytesToHex(rawBytes, offset, 16);
                offset += 16;
                if (endOffset - offset < 2)
                {
                    throw new Exception("60713574-6f3f-4cd3-9f5c-7f175ea4e87f: " +
                        "datagram too small for header ack op code");
                }
                parsedDatagram.ErrorCode = ByteUtils.DeserializeInt16BigEndian(rawBytes, offset);
                offset += 2;
            }
            return offset;
        }

        private static int DeserializeRemainderOfDataPdu(ProtocolDatagram parsedDatagram, byte[] rawBytes,
            int offset, int endOffset)
        {
            if (parsedDatagram.OpCode == OpCodeData)
            {
                parsedDatagram.MessageDestinationId = ByteUtils.ConvertBytesToHex(rawBytes, offset, 16);
                offset += 16;
                if (endOffset - offset < 4)
                {
                    throw new Exception("340093ec-041f-44fe-a5e1-e53bcb1b500b: " +
                        "datagram too small for data op code");
                }
                parsedDatagram.SequenceNumber = ByteUtils.DeserializeInt32BigEndian(rawBytes, offset);
                offset += 4;
            }
            return offset;
        }

        private static int DeserializeRemainderOfDataAckPdu(ProtocolDatagram parsedDatagram, byte[] rawBytes,
            int offset, int endOffset)
        {
            if (parsedDatagram.OpCode == OpCodeDataAck)
            {
                parsedDatagram.MessageSourceId = ByteUtils.ConvertBytesToHex(rawBytes, offset, 16);
                offset += 16;
                if (endOffset - offset < 4+2)
                {
                    throw new Exception("2643d9ea-c82f-4f21-90ea-576b591fd294: " +
                        "datagram too small for data ack op code");
                }
                parsedDatagram.SequenceNumber = ByteUtils.DeserializeInt32BigEndian(rawBytes, offset);
                offset += 4;
                parsedDatagram.ErrorCode = ByteUtils.DeserializeInt16BigEndian(rawBytes, offset);
                offset += 2;
            }
            return offset;
        }

        public byte[] Serialize()
        {
            // validate fields.
            if (MessageId == null || MessageId.Length != 32)
            {
                throw new Exception("e47fc3b1-f391-4f1a-aa82-814c01be6bea: " +
                    "message id must consist of 32 hexadecimal characters");
            }
            if (OpCode == OpCodeData || OpCode == OpCodeHeader)
            {
                if (Data == null)
                {
                    throw new Exception("f414e24d-d8bb-44dc-afb4-d34773d28e9a: " +
                        "data must be set");
                }
                if (DataLength < 0)
                {
                    throw new Exception("9039a1e3-c4a1-4eff-b53f-059a7316b97d: " +
                        "data length must be valid, hence cannot be negative");
                }
                if (DataOffset < 0)
                {
                    throw new Exception("149f8bf9-0226-40e3-a6ca-2d00541a4d75: " +
                        "data offset must be valid, hence cannot be negative");
                }
                if (DataOffset + DataLength > Data.Length)
                {
                    throw new Exception("786322b1-f408-4b9a-a41d-d95acecda445: " +
                        "data offset and length combination exceeds data size");
                }
            }

            byte[] rawBytes = SerializeAsHeaderPdu();
            if (rawBytes == null)
            {
                rawBytes = SerializeAsHeaderAckPdu();
            }
            if (rawBytes == null)
            {
                rawBytes = SerializeAsDataPdu();
            }
            if (rawBytes == null)
            {
                rawBytes = SerializeAsDataAckPdu();
            }

            if (rawBytes == null)
            {
                throw new Exception("6f66dbe8-0c15-48b6-8c6a-856762cdf3e9: Unexpected opcode: " +
                    OpCode);
            }
            return rawBytes;
        }

        private int SerializeBeginningMembers(byte[] rawBytes)
        {
            int offset = 0;
            rawBytes[offset] = OpCode;
            offset += 1;
            rawBytes[offset] = Version;
            offset += 1;
            ByteUtils.SerializeInt32BigEndian(Reserved, rawBytes, offset);
            offset += 4;
            ByteUtils.ConvertHexToBytes(MessageId, rawBytes, offset);
            offset += 16;
            return offset;
        }

        private byte[] SerializeAsHeaderPdu()
        {
            if (OpCode != OpCodeHeader)
            {
                return null;
            }

            if (MessageDestinationId == null || MessageDestinationId.Length != 32)
            {
                throw new Exception("39d799a9-98ee-4477-bd28-f126b38212ac: " +
                    "message destination id must consist of 32 hexadecimal characters");
            }

            byte[] rawBytes = new byte[MinDatagramSize + 4 + DataLength];
            int offset = SerializeBeginningMembers(rawBytes);
            ByteUtils.ConvertHexToBytes(MessageDestinationId, rawBytes, offset);
            offset += 16;
            ByteUtils.SerializeInt32BigEndian(MessageLength, rawBytes, offset);
            offset += 4;
            Array.Copy(Data, DataOffset, rawBytes, offset, DataLength);
            offset += DataLength;
            if (offset != rawBytes.Length)
            {
                throw new Exception("serialization failure");
            }
            return rawBytes;
        }

        private byte[] SerializeAsHeaderAckPdu()
        {
            if (OpCode != OpCodeHeaderAck)
            {
                return null;
            }

            if (MessageSourceId == null || MessageSourceId.Length != 32)
            {
                throw new Exception("25b04616-6aff-4f55-b0c9-1b06922b1c44: " +
                    "message source id must consist of 32 hexadecimal characters");
            }

            byte[] rawBytes = new byte[MinDatagramSize + 2];
            int offset = SerializeBeginningMembers(rawBytes);
            ByteUtils.ConvertHexToBytes(MessageSourceId, rawBytes, offset);
            offset += 16;
            ByteUtils.SerializeInt16BigEndian(ErrorCode, rawBytes, offset);
            offset += 2;
            if (offset != rawBytes.Length)
            {
                throw new Exception("serialization failure");
            }
            return rawBytes;
        }

        private byte[] SerializeAsDataPdu()
        {
            if (OpCode != OpCodeData)
            {
                return null;
            }

            if (MessageDestinationId == null || MessageDestinationId.Length != 32)
            {
                throw new Exception("938436a3-56aa-45f8-97ef-9715dea14cc4: " +
                    "message destination id must consist of 32 hexadecimal characters");
            }

            byte[] rawBytes = new byte[MinDatagramSize + 4 + DataLength];
            int offset = SerializeBeginningMembers(rawBytes);
            ByteUtils.ConvertHexToBytes(MessageDestinationId, rawBytes, offset);
            offset += 16;
            ByteUtils.SerializeInt32BigEndian(SequenceNumber, rawBytes, offset);
            offset += 4;
            Array.Copy(Data, DataOffset, rawBytes, offset, DataLength);
            offset += DataLength;
            if (offset != rawBytes.Length)
            {
                throw new Exception("serialization failure");
            }
            return rawBytes;
        }

        private byte[] SerializeAsDataAckPdu()
        {
            if (OpCode != OpCodeDataAck)
            {
                return null;
            }

            if (MessageSourceId == null || MessageSourceId.Length != 32)
            {
                throw new Exception("3fd5c735-c487-4cef-976d-f8a0c52a06a3: " +
                    "message source id must consist of 32 hexadecimal characters");
            }

            byte[] rawBytes = new byte[MinDatagramSize + 4 + 2];
            int offset = SerializeBeginningMembers(rawBytes);
            ByteUtils.ConvertHexToBytes(MessageSourceId, rawBytes, offset);
            offset += 16;
            ByteUtils.SerializeInt32BigEndian(SequenceNumber, rawBytes, offset);
            offset += 4;
            ByteUtils.SerializeInt16BigEndian(ErrorCode, rawBytes, offset);
            offset += 2;
            if (offset != rawBytes.Length)
            {
                throw new Exception("serialization failure");
            }
            return rawBytes;
        }

        public override bool Equals(object obj)
        {
            return obj is ProtocolDatagram datagram &&
                   OpCode == datagram.OpCode &&
                   Version == datagram.Version &&
                   Reserved == datagram.Reserved &&
                   MessageId == datagram.MessageId &&
                   MessageSourceId == datagram.MessageSourceId &&
                   MessageDestinationId == datagram.MessageDestinationId &&
                   MessageLength == datagram.MessageLength &&
                   SequenceNumber == datagram.SequenceNumber &&
                   ErrorCode == datagram.ErrorCode &&
                   DataOffset == datagram.DataOffset &&
                   DataLength == datagram.DataLength &&
                   EqualityComparer<byte[]>.Default.Equals(Data, datagram.Data);
        }

        public override int GetHashCode()
        {
            int hashCode = -2063747072;
            hashCode = hashCode * -1521134295 + OpCode.GetHashCode();
            hashCode = hashCode * -1521134295 + Version.GetHashCode();
            hashCode = hashCode * -1521134295 + Reserved.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(MessageId);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(MessageSourceId);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(MessageDestinationId);
            hashCode = hashCode * -1521134295 + MessageLength.GetHashCode();
            hashCode = hashCode * -1521134295 + SequenceNumber.GetHashCode();
            hashCode = hashCode * -1521134295 + ErrorCode.GetHashCode();
            hashCode = hashCode * -1521134295 + DataOffset.GetHashCode();
            hashCode = hashCode * -1521134295 + DataLength.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<byte[]>.Default.GetHashCode(Data);
            return hashCode;
        }
    }
}
