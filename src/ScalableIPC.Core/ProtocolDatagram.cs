using ScalableIPC.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ScalableIPC.Core
{
    public class ProtocolDatagram
    {
        public const byte OpCodeData = 0x01;
        public const byte OpCodeDataAck = 0x02;
        public const byte OpCodeHeader = 0x03;
        public const byte OpCodeHeaderAck = 0x04;

        public const byte ProtocolVersion1_0 = 0x10;

        public const int MinDatagramSize = 22;
        public const int MsgIdStrLength = 32;

        private static readonly Regex MsgIdRegex = new Regex($"^[a-fA-F0-9]{{{MsgIdStrLength}}}$");
        private static readonly string NullMsgId = "0".PadRight(MsgIdStrLength, '0');

        public byte OpCode { get; set; }
        public byte Version { get; set; }
        public int Reserved { get; set; }
        public string MessageId { get; set; }
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

            parsedDatagram.Data = rawBytes;
            parsedDatagram.DataOffset = offset;
            parsedDatagram.DataLength = endOffset - offset;

            // perform shallow validation
            if (parsedDatagram.Version == 0)
            {
                throw new Exception("5c0e154c-0691-4ece-adc9-32437ba6d4d4: " +
                    "missing protocol version");
            }
            if (parsedDatagram.MessageId == NullMsgId)
            {
                throw new Exception("96bfdc57-2baa-4b4e-9c84-6843b72bb871: " +
                    "message id cannot consist of only zeros");
            }
            if (parsedDatagram.MessageDestinationId == NullMsgId)
            {
                throw new Exception("f3701b60-524d-4f10-acb6-2ab0f56c99fc: " +
                    "message dest id cannot consist of only zeros");
            }
            if (parsedDatagram.SequenceNumber < 0)
            {
                throw new Exception("72ffa748-14a3-41ca-baf1-a1d9b0e771b2: " +
                    "sequence number cannot be negative");
            }
            if (parsedDatagram.ErrorCode < 0)
            {
                throw new Exception("2c302ccd-62fb-404f-8db9-0d112dfa35ee: " +
                    "error code cannot be negative");
            }
            if (parsedDatagram.MessageLength < 0)
            {
                throw new Exception("aa0b494d-0467-4abf-baa3-b1661e597873: " +
                    "message length cannot be negative");
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

        public void Validate()
        {
            if (OpCode != OpCodeHeader && OpCode != OpCodeHeaderAck &&
                OpCode != OpCodeData && OpCode != OpCodeDataAck)
            {
                throw new Exception("6f66dbe8-0c15-48b6-8c6a-856762cdf3e9: Unexpected opcode: " +
                    OpCode);
            }
            if (Version == 0)
            {
                throw new Exception("477bdefc-adf1-4b3f-b377-e6ea0e1fe9dd: " +
                    "protocol version must be set");
            }
            if (MessageId == null)
            {
                throw new Exception("b96df8fa-212b-4914-862a-8e82ed5d25e1: " +
                    "message id is required");
            }
            if (!MsgIdRegex.Match(MessageId).Success)
            {
                throw new Exception("e47fc3b1-f391-4f1a-aa82-814c01be6bea: " +
                    "invalid message id");
            }
            if (MessageId == NullMsgId)
            {
                throw new Exception("9d32a707-7f52-4a7c-a444-ca08ac104d2f: " +
                    "message id cannot consist of only zeros");
            }
            if (MessageDestinationId == null)
            {
                if (OpCode == OpCodeHeader || OpCode == OpCodeData)
                {
                    throw new Exception("f3701b60-524d-4f10-acb6-2ab0f56c99fc: " +
                        "message dest id is required");
                }
            }
            if (SequenceNumber <= 0)
            {
                if (OpCode == OpCodeData || OpCode == OpCodeDataAck)
                {
                    throw new Exception("0903ddcc-d0bb-4772-a1fc-c2530e00a423: " +
                        "sequence number must be positive");
                }
            }
            if (ErrorCode < 0)
            {
                if (OpCode == OpCodeHeaderAck || OpCode == OpCodeDataAck)
                {
                    throw new Exception("fb2c83ee-9b2f-4ec9-8b01-519cd5cb38dc: " +
                        "error code cannot be negative");
                }
            }
            if (MessageLength < 0)
            {
                if (OpCode == OpCodeHeader)
                {
                    throw new Exception("6e989cb8-6944-4e26-be87-f6717e7dbb56: " +
                        "message length cannot be negative");
                }
            }

            if (Data == null)
            {
                if (OpCode == OpCodeHeader || OpCode == OpCodeData)
                {
                    throw new Exception("7bb539e7-7670-4faf-ac75-fb7c824025c9: " +
                        "data is required");
                }
            }
            if (MessageDestinationId != null)
            {
                if (!MsgIdRegex.Match(MessageDestinationId).Success)
                {
                    throw new Exception("938436a3-56aa-45f8-97ef-9715dea14cc4: " +
                        "invalid message destination id");
                }
                if (MessageDestinationId == NullMsgId)
                {
                    throw new Exception("6cc83541-ac56-4398-9ffe-65e041419712: " +
                        "message destination id cannot consist of only zeros");
                }
            }
            if (Data != null)
            {
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
        }

        public byte[] Serialize()
        {
            // perform full validation.
            Validate();

            byte[] rawBytes;
            switch (OpCode)
            {
                case OpCodeHeader:
                    rawBytes = SerializeAsHeaderPdu();
                    break;
                case OpCodeHeaderAck:
                    rawBytes = SerializeAsHeaderAckPdu();
                    break;
                case OpCodeData:
                    rawBytes = SerializeAsDataPdu();
                    break;
                case OpCodeDataAck:
                    rawBytes = SerializeAsDataAckPdu();
                    break;
                default:
                    throw new Exception("Unknown opcode: " + OpCode);
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
            byte[] rawBytes = new byte[MinDatagramSize + 4 + Data.Length];
            int offset = SerializeBeginningMembers(rawBytes);
            ByteUtils.ConvertHexToBytes(MessageDestinationId, rawBytes, offset);
            offset += 16;
            ByteUtils.SerializeInt32BigEndian(MessageLength, rawBytes, offset);
            offset += 4;
            Array.Copy(Data, DataOffset, rawBytes, offset, Data.Length);
            offset += Data.Length;
            if (offset != rawBytes.Length)
            {
                throw new Exception("header serialization failure");
            }
            return rawBytes;
        }

        private byte[] SerializeAsHeaderAckPdu()
        {
            int dataLengthToUse = Data != null ? DataLength : 0;
            byte[] rawBytes = new byte[MinDatagramSize + 2 + dataLengthToUse];
            int offset = SerializeBeginningMembers(rawBytes);
            ByteUtils.SerializeInt16BigEndian(ErrorCode, rawBytes, offset);
            offset += 2;
            if (Data != null)
            {
                Array.Copy(Data, DataOffset, rawBytes, offset, dataLengthToUse);
                offset += dataLengthToUse;
            }
            if (offset != rawBytes.Length)
            {
                throw new Exception("header ack serialization failure");
            }
            return rawBytes;
        }

        private byte[] SerializeAsDataPdu()
        {
            byte[] rawBytes = new byte[MinDatagramSize + 4 + Data.Length];
            int offset = SerializeBeginningMembers(rawBytes);
            ByteUtils.ConvertHexToBytes(MessageDestinationId, rawBytes, offset);
            offset += 16;
            ByteUtils.SerializeInt32BigEndian(SequenceNumber, rawBytes, offset);
            offset += 4;
            Array.Copy(Data, DataOffset, rawBytes, offset, Data.Length);
            offset += Data.Length;
            if (offset != rawBytes.Length)
            {
                throw new Exception("data serialization failure");
            }
            return rawBytes;
        }

        private byte[] SerializeAsDataAckPdu()
        {
            int dataLengthToUse = Data != null ? DataLength : 0;
            byte[] rawBytes = new byte[MinDatagramSize + 4 + 2 + dataLengthToUse];
            int offset = SerializeBeginningMembers(rawBytes);
            ByteUtils.SerializeInt32BigEndian(SequenceNumber, rawBytes, offset);
            offset += 4;
            ByteUtils.SerializeInt16BigEndian(ErrorCode, rawBytes, offset);
            offset += 2;
            if (Data != null)
            {
                Array.Copy(Data, DataOffset, rawBytes, offset, dataLengthToUse);
                offset += dataLengthToUse;
            }
            if (offset != rawBytes.Length)
            {
                throw new Exception("data ack serialization failure");
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
