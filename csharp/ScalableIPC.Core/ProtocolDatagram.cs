using ScalableIPC.Core.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace ScalableIPC.Core
{
    /// <summary>
    /// Protocol PDU structure was formed with the following in mind:
    /// 1. use of sessionid removes the need for TIME_WAIT state used by TCP. 32-byte session ids are generated
    ///    by combining random uuid, current timestamp, and auto incrementing integer.
    /// 2. use of 32-bit sequence number separate from window id, allows a maximum bandwidth of 512 * 2G = 1TB (1024 GB), 
    ///    more than enough for networks with large bandwidth-delay product (assuming packet size of 512 bytes).
    /// 3. use of 64-bit window id is for ensuring that by the time 64-bit numbers are exhausted in max increments of 1000,
    ///    at a speed of 512 terabytes per second (ie 2^49 bytes/s), it will take 34 minutes (ie 2^11 seconds)
    ///    to exhaust ids and wrap around. That's more than enough for networks to discard traces of any lingering packet.
    /// </summary>
    public class ProtocolDatagram
    {
        public const byte OpCodeData = 0x00;
        public const byte OpCodeAck = 0x10;
        public const byte OpCodeClose = 0x7e;
        public const byte OpCodeCloseAll = 0x7f;

        public const int AbortCodeNormalClose = 0;
        public const int AbortCodeTimeout = 1;
        public const int AbortCodeForceClose = 2;
        public const int AbortCodeCloseAll = 3;
        public const int AbortCodeShutdown = 4;
        public const int AbortCodeError = 5;
        public const int AbortCodeWindowGroupOverflow = 6;

        public const byte NullTerminator = 0;

        public const int SessionIdLength = 32;

        public const long MinWindowIdCrossOverLimit = 1_000;
        public const long MaxWindowIdCrossOverLimit = 9_000_000_000_000_000_000L;

        // the expected length, sessionId, opCode, window id, 
        // sequence number, null terminators are always present.
        public const int MinDatagramSize = 2 + SessionIdLength + 1 + 8 + 4 + 2;
        public const int MaxDatagramSize = 65_500;
        public const int MinimumTransferUnitSize = 512;
        public const int MaxOptionByteCount = 60_000;

        private static readonly string Latin1Encoding = "ISO-8859-1";

        // used to generate session ids.
        private static int _sessionIdCounter;

        public int ExpectedDatagramLength { get; set; }
        public string SessionId { get; set; }
        public byte OpCode { get; set; }
        public long WindowId { get; set; }
        public int SequenceNumber { get; set; }
        public ProtocolDatagramOptions Options { get; set; }

        public byte[] DataBytes { get; set; }
        public int DataOffset { get; set; }
        public int DataLength { get; set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(nameof(ProtocolDatagram)).Append("{");
            sb.Append(nameof(ExpectedDatagramLength)).Append("=").Append(ExpectedDatagramLength);
            sb.Append(", ");
            sb.Append(nameof(SessionId)).Append("=").Append(SessionId);
            sb.Append(", ");
            sb.Append(nameof(OpCode)).Append("=").Append(OpCode);
            sb.Append(", ");
            sb.Append(nameof(WindowId)).Append("=").Append(WindowId);
            sb.Append(", ");
            sb.Append(nameof(SequenceNumber)).Append("=").Append(SequenceNumber);
            sb.Append(", ");
            sb.Append(nameof(Options)).Append("=").Append(Options);
            sb.Append(", ");
            sb.Append(nameof(DataOffset)).Append("=").Append(DataOffset);
            sb.Append(", ");
            sb.Append(nameof(DataLength)).Append("=").Append(DataLength);
            sb.Append(", ");
            sb.Append(nameof(DataBytes)).Append("=").Append(StringUtilities.StringifyByteArray(DataBytes));
            sb.Append("}");
            return sb.ToString();
        }

        public static ProtocolDatagram Parse(byte[] rawBytes, int offset, int length)
        {
            // validate arguments.
            if (rawBytes == null)
            {
                throw new ArgumentNullException(nameof(rawBytes));
            }
            if (offset < 0)
            {
                throw new ArgumentException("offset cannot be negative", nameof(offset));
            }
            if (length < 0)
            {
                throw new ArgumentException("length cannot be negative", nameof(offset));
            }
            if (offset + length > rawBytes.Length)
            {
                throw new ArgumentException("combination of offset and length exceeeds byte array size");
            }

            if (length < MinDatagramSize)
            {
                throw new Exception("datagram too small to be valid");
            }

            if (length > MaxDatagramSize)
            {
                throw new Exception("datagram too large to be valid");
            }

            var parsedDatagram = new ProtocolDatagram();

            // validate length.
            int expectedDatagramLength = DeserializeUnsignedInt16BigEndian(rawBytes, offset);
            if (expectedDatagramLength != length)
            {
                throw new Exception("expected datagram length incorrect");
            }

            int endOffset = offset + length;

            parsedDatagram.ExpectedDatagramLength = expectedDatagramLength;
            offset += 2; // skip past expected data length;

            parsedDatagram.SessionId = ConvertBytesToHex(rawBytes, offset, SessionIdLength);
            offset += SessionIdLength;

            parsedDatagram.WindowId = DeserializeInt64BigEndian(rawBytes, offset);
            if (parsedDatagram.WindowId < 0)
            {
                throw new Exception("Negative window id not allowed");
            }
            offset += 8;

            parsedDatagram.SequenceNumber = DeserializeInt32BigEndian(rawBytes, offset);
            if (parsedDatagram.SequenceNumber < 0)
            {
                throw new Exception("Negative sequence number not allowed");
            }
            offset += 4;

            parsedDatagram.OpCode = rawBytes[offset];
            offset += 1;

            // Now read options until we encounter null terminators for all options.
            while (!(rawBytes[offset] == NullTerminator && rawBytes[offset+1] == NullTerminator))
            {
                offset = ParseNextOption(rawBytes, offset, endOffset, parsedDatagram);

                if (endOffset - offset < 2)
                {
                    throw new Exception("null terminators for all options missing");
                }
            }

            // validate known options.
            parsedDatagram.Options?.ParseKnownOptions();

            // increment offset for null terminators of all options.
            offset += 2;

            parsedDatagram.DataBytes = rawBytes;
            parsedDatagram.DataOffset = offset;
            parsedDatagram.DataLength = endOffset - offset;

            return parsedDatagram;
        }

        private static int ParseNextOption(byte[] rawBytes, int offset, int endOffset,
            ProtocolDatagram parsedDatagram)
        {
            int totalLengthPlusOne = DeserializeUnsignedInt16BigEndian(rawBytes, offset);
            offset += 2;

            // look for null terminator.
            int nullTerminatorIndex = -1;
            for (int i = offset; i < endOffset; i++)
            {
                if (rawBytes[i] == NullTerminator)
                {
                    nullTerminatorIndex = i;
                    break;
                }
            }
            if (nullTerminatorIndex == -1)
            {
                throw new Exception("null terminator for option name not found");
            }

            var optionNameLength = nullTerminatorIndex - offset;
            if (optionNameLength >= totalLengthPlusOne)
            {
                throw new Exception("Corrupted datagram received");
            }
            var optionName = ConvertBytesToString(rawBytes, offset, optionNameLength);
            offset = nullTerminatorIndex + 1;

            if (endOffset - offset < 2)
            {
                throw new Exception("incomplete option specification: confirmatory length section missing");
            }
            var confirmatoryTotalLengthPlusOne = DeserializeUnsignedInt16BigEndian(rawBytes, offset);
            if (totalLengthPlusOne != confirmatoryTotalLengthPlusOne)
            {
                throw new Exception("Corrupted datagram received");
            }
            offset += 2;

            int optionValueLength = totalLengthPlusOne - 1 - optionNameLength;

            if (endOffset - offset < optionValueLength)
            {
                throw new Exception("incomplete option specification: option value missing");
            }
            var optionValue = ConvertBytesToString(rawBytes, offset, optionValueLength);
            offset += optionValueLength;

            if (parsedDatagram.Options == null)
            {
                parsedDatagram.Options = new ProtocolDatagramOptions();
            }
            parsedDatagram.Options.AddOption(optionName, optionValue);
            return offset;
        }

        public static string GenerateSessionId()
        {
            var prefixNum = Interlocked.Increment(ref _sessionIdCounter);
            if (prefixNum < 0)
            {
                // map -2^31 to -1, to 0 to 2^31-1
                prefixNum += int.MaxValue;
                prefixNum++;
            }
            var prefix = ConvertBytesToHex(SerializeInt32BigEndian(prefixNum), 0, 4).PadRight(16, '0');

            var date = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff").Substring(1);

            var suffix = Guid.NewGuid().ToString("n");
            return prefix + date + suffix;
        }

        public byte[] ToRawDatagram()
        {
            // validate fields.
            if (SessionId == null)
            {
                throw new Exception("session id must be set");
            }
            if (DataLength < 0)
            {
                throw new Exception("data length must be valid, hence cannot be negative");
            }
            if (DataLength > MaxDatagramSize)
            {
                throw new Exception("data payload too large to be valid");
            }
            if (DataOffset < 0)
            {
                throw new Exception("data offset must be valid, hence cannot be negative");
            }
            if (DataBytes != null)
            {
                if (DataOffset + DataLength > DataBytes.Length)
                {
                    throw new Exception("data offset and length combination exceeds data bytes size");
                }
            }

            byte[] rawBytes;
            using (var ms = new MemoryStream())
            {
                using (var writer = new BinaryWriter(ms))
                {
                    // Make space for expected data length.
                    writer.Write((byte)0);
                    writer.Write((byte)0);

                    // write out session id.
                    byte[] sessionId = ConvertHexToBytes(SessionId);
                    if (sessionId.Length != SessionIdLength)
                    {
                        throw new Exception($"Invalid session id length in bytes: {sessionId.Length}");
                    }
                    writer.Write(sessionId);

                    // Write out window id.
                    writer.Write(SerializeInt64BigEndian(WindowId));

                    writer.Write(SerializeInt32BigEndian(SequenceNumber));

                    writer.Write(OpCode);

                    // write out all options.
                    if (Options != null)
                    {
                        foreach (var pair in Options.GenerateList())
                        {
                            WriteOption(writer, pair);
                        }
                    }

                    writer.Write(NullTerminator);
                    writer.Write(NullTerminator);

                    if (DataBytes != null)
                    {
                        writer.Write(DataBytes, DataOffset, DataLength);
                    }
                }
                rawBytes = ms.ToArray();
            }

            // Data transfer options other than using DataBytes are supported by
            // requiring DataLength field at all times.

            // validate ExpectedDatagramLength if given.
            if (ExpectedDatagramLength > 0)
            {
                if (DataBytes != null && ExpectedDatagramLength != rawBytes.Length)
                {
                    throw new Exception($"expected datagram length != actual ({ExpectedDatagramLength} != {rawBytes.Length})");
                }
                if (DataBytes == null && ExpectedDatagramLength != rawBytes.Length + DataLength)
                {
                    throw new Exception($"expected datagram length != actual ({ExpectedDatagramLength} != {rawBytes.Length + DataLength})");
                }
            }
            else
            {
                // set it depending on whether DataBytes is given.
                if (DataBytes != null)
                {
                    ExpectedDatagramLength = rawBytes.Length;
                }
                else
                {
                    ExpectedDatagramLength = rawBytes.Length + DataLength;
                }
            }

            InsertExpectedDataLength(ExpectedDatagramLength, rawBytes);

            if (ExpectedDatagramLength > MaxDatagramSize)
            {
                throw new Exception("datagram too large to be valid");
            }

            return rawBytes;
        }

        private void WriteOption(BinaryWriter writer, string[] pair)
        {
            var optionNameBytes = ConvertStringToBytes(pair[0]);
            var optionValueBytes = ConvertStringToBytes(pair[1]);
            int totalLengthPlusOne = optionNameBytes.Length + optionValueBytes.Length + 1;
            writer.Write(SerializeUnsignedInt16BigEndian(totalLengthPlusOne));
            writer.Write(optionNameBytes);
            writer.Write(NullTerminator);
            writer.Write(SerializeUnsignedInt16BigEndian(totalLengthPlusOne));
            writer.Write(optionValueBytes);
        }

        private static void InsertExpectedDataLength(int expectedDataLen, byte[] dest)
        {
            byte[] intBytes = SerializeUnsignedInt16BigEndian(expectedDataLen);
            dest[0] = intBytes[0];
            dest[1] = intBytes[1];
        }

        internal static byte[] ConvertStringToBytes(string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        internal static int CountBytesInString(string s)
        {
            return Encoding.UTF8.GetByteCount(s);
        }

        internal static string ConvertBytesToString(byte[] data, int offset, int length)
        {
            return Encoding.UTF8.GetString(data, offset, length);
        }

        internal static string ConvertBytesToLatin1(byte[] data, int offset, int length)
        {
            return Encoding.GetEncoding(Latin1Encoding).GetString(data, offset, length);
        }

        internal static byte[] ConvertLatin1ToBytes(string s)
        {
            return Encoding.GetEncoding(Latin1Encoding).GetBytes(s);
        }

        internal static string ConvertBytesToHex(byte[] data, int offset, int len)
        {
            // send out lower case for similarity with other platforms (Java, Python, NodeJS, etc)
            // ensure even length.
            return BitConverter.ToString(data, offset, len).Replace("-", "").ToLower();
        }

        internal static byte[] ConvertHexToBytes(string hex)
        {
            int charCount = hex.Length;
            if (charCount % 2 != 0)
            {
                throw new Exception("arg must have even length");
            }
            byte[] bytes = new byte[charCount / 2];
            for (int i = 0; i < charCount; i += 2)
            {
                // accept both upper and lower case hex chars.
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        internal static byte[] SerializeUnsignedInt16BigEndian(int v)
        {
            return SerializeInt16BigEndian((short)v);
        }

        internal static byte[] SerializeInt16BigEndian(short v)
        {
            byte[] intBytes = new byte[2];
            intBytes[0] = (byte)(0xff & (v >> 8));
            intBytes[1] = (byte)(0xff & v);
            return intBytes;
        }

        internal static byte[] SerializeInt32BigEndian(int v)
        {
            byte[] intBytes = new byte[4];
            intBytes[0] = (byte)(0xff & (v >> 24));
            intBytes[1] = (byte)(0xff & (v >> 16));
            intBytes[2] = (byte)(0xff & (v >> 8));
            intBytes[3] = (byte)(0xff & v);
            return intBytes;
        }

        internal static byte[] SerializeInt64BigEndian(long v)
        {
            byte[] intBytes = new byte[8];
            intBytes[0] = (byte)(0xff & (v >> 56));
            intBytes[1] = (byte)(0xff & (v >> 48));
            intBytes[2] = (byte)(0xff & (v >> 40));
            intBytes[3] = (byte)(0xff & (v >> 32));
            intBytes[4] = (byte)(0xff & (v >> 24));
            intBytes[5] = (byte)(0xff & (v >> 16));
            intBytes[6] = (byte)(0xff & (v >> 8));
            intBytes[7] = (byte)(0xff & v);
            return intBytes;
        }

        internal static short DeserializeInt16BigEndian(byte[] rawBytes, int offset)
        {
            byte a = rawBytes[offset];
            byte b = rawBytes[offset + 1];
            int v = (a << 8) | (b & 0xff);
            return (short)v;
        }

        internal static int DeserializeUnsignedInt16BigEndian(byte[] rawBytes, int offset)
        {
            byte a = rawBytes[offset];
            byte b = rawBytes[offset + 1];
            int v = (a << 8) | (b & 0xff);
            return v; // NB: no cast to short.
        }

        internal static int DeserializeInt32BigEndian(byte[] rawBytes, int offset)
        {
            byte a = rawBytes[offset];
            byte b = rawBytes[offset + 1];
            byte c = rawBytes[offset + 2];
            byte d = rawBytes[offset + 3];
            int v = ((a & 0xff) << 24) | ((b & 0xff) << 16) |
                ((c & 0xff) << 8) | (d & 0xff);
            return v;
        }

        internal static long DeserializeInt64BigEndian(byte[] rawBytes, int offset)
        {
            byte a = rawBytes[offset];
            byte b = rawBytes[offset + 1];
            byte c = rawBytes[offset + 2];
            byte d = rawBytes[offset + 3];
            byte e = rawBytes[offset + 4];
            byte f = rawBytes[offset + 5];
            byte g = rawBytes[offset + 6];
            byte h = rawBytes[offset + 7];
            long v = ((long)(a & 0xff) << 56) | ((long)(b & 0xff) << 48) |
                ((long)(c & 0xff) << 40) | ((long)(d & 0xff) << 32) |
                ((long)(e & 0xff) << 24) | ((long)(f & 0xff) << 16) |
                ((long)(g & 0xff) << 8) | ((long)h & 0xff);
            return v;
        }

        public static long ComputeNextWindowIdToSend(long nextWindowIdToSend)
        {
            // Perform simple predicatable computation of increasing and wrapping around.
            // max crossover limit.
            if (nextWindowIdToSend >= MaxWindowIdCrossOverLimit)
            {
                // return any positive value not exceeding min crossover limit.
                return 1;
            }
            else
            {
                // increase by any positive amount not exceeding min crossover limit.
                return nextWindowIdToSend + 1;
            }
        }

        public static bool IsReceivedWindowIdValid(long v, long lastWindowIdProcessed)
        {
            // ANY alternate computations is allowed with these 2 requirements:
            // 1. the very first value should be 0.
            // 2. if current value has crossed max crossover limit,
            //    then next value must be less than or equal to min crossover limit, but greater than 0.
            // 3. else next value must be larger than current value by a difference which is 
            //    less than or equal to min crossover limit.
            if (lastWindowIdProcessed < 0)
            {
                return v == 0;
            }
            if (lastWindowIdProcessed >= MaxWindowIdCrossOverLimit)
            {
                return v > 0 && v <= MinWindowIdCrossOverLimit;
            }
            else
            {
                return v > lastWindowIdProcessed && (v - lastWindowIdProcessed) <= MinWindowIdCrossOverLimit;
            }
        }

        public static ProtocolDatagram CreateMessageOutOfWindow(List<ProtocolDatagram> messages)
        {
            // NB: As an optimization, if window contains 1 message, just return it.
            // MemoryNetworkApi may depend on this optimization.
            if (messages.Count == 1)
            {
                return messages[0];
            }
            var windowAsMessage = new ProtocolDatagram
            {
                Options = new ProtocolDatagramOptions()
            };
            var memoryStream = new MemoryStream();
            foreach (var msg in messages)
            {
                windowAsMessage.ExpectedDatagramLength += msg.ExpectedDatagramLength;
                windowAsMessage.OpCode = msg.OpCode;
                windowAsMessage.SessionId = msg.SessionId;
                windowAsMessage.WindowId = msg.WindowId;
                windowAsMessage.SequenceNumber = msg.SequenceNumber;
                if (msg.Options != null)
                {
                    foreach (var pair in msg.Options.GenerateList())
                    {
                        windowAsMessage.Options.AddOption(pair[0], pair[1]);
                    }

                    msg.Options.TransferParsedKnownOptionsTo(windowAsMessage.Options);
                }
                memoryStream.Write(msg.DataBytes, msg.DataOffset, msg.DataLength);
            }
            memoryStream.Flush();
            var data = memoryStream.ToArray();
            windowAsMessage.DataBytes = data;
            windowAsMessage.DataLength = data.Length;
            return windowAsMessage;
        }

        public static string FormatAbortCode(int code)
        {
            if (code == AbortCodeNormalClose)
                return "NORMAL CLOSE";
            else if (code == AbortCodeTimeout)
                return "TIMEOUT";
            else if (code == AbortCodeForceClose)
                return "FORCED CLOSE";
            else if (code == AbortCodeCloseAll)
                return "CLOSE ALL";
            else if (code == AbortCodeShutdown)
                return "SHUTTING DOWN";
            else if (code == AbortCodeError)
                return "INTERNAL ERROR";
            else
                return "UNSPECIFIED";
        }
    }
}
