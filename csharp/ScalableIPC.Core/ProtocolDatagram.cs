using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ScalableIPC.Core
{
    /// <summary>
    /// Protocol PDU structure was formed with the following in mind:
    /// 1. use of sessionid removes the need for TIME_WAIT state used by TCP. by default 32-byte session ids are generated
    ///    by combining random uuid with current timestamp. Option has been made in structure to use 16-byte session id.
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

        public const byte NullTerminator = 0;
        public const byte FalseIndicatorByte = 0;
        public const byte TrueIndicatorByte = 0xff;

        public const int MinSessionIdLength = 16;
        public const int MaxSessionIdLength = 32;

        public const long MinWindowIdCrossOverLimit = 1_000;
        public const long MaxWindowIdCrossOverLimit = 9_000_000_000_000_000_000L;

        // the 2 length indicator booleans, expected length, sessionId, opCode, window id, 
        // sequence number, null separator are always present.
        public const int MinDatagramSize = 2 + 4 + MinSessionIdLength + 1 + 4 + 4 + 1;

        public int ExpectedDatagramLength { get; set; }
        public string SessionId { get; set; }
        public byte OpCode { get; set; }
        public long WindowId { get; set; }
        public int SequenceNumber { get; set; }
        public ProtocolDatagramOptions Options { get; set; }

        public byte[] DataBytes { get; set; }
        public int DataOffset { get; set; }
        public int DataLength { get; set; }

        public static ProtocolDatagram Parse(byte[] rawBytes, int offset, int length)
        {
            int effectiveMinDatagramSize = MinDatagramSize;
            if (length < effectiveMinDatagramSize)
            {
                throw new Exception("datagram too small to be valid");
            }

            // liberally accept any size once it made it through network without errors.

            var parsedDatagram = new ProtocolDatagram();

            // validate length and checksum.
            int expectedDatagramLength = ReadInt32BigEndian(rawBytes, offset);
            if (expectedDatagramLength != length)
            {
                throw new Exception("expected datagram length incorrect");
            }

            int endOffset = offset + length;

            parsedDatagram.ExpectedDatagramLength = expectedDatagramLength;
            offset += 4; // skip past expected data length;

            byte lenIndicator = rawBytes[offset];
            offset += 1;

            int sessionIdLen;
            switch (lenIndicator)
            {
                case FalseIndicatorByte:
                    sessionIdLen = MinSessionIdLength;
                    break;
                case TrueIndicatorByte:
                    effectiveMinDatagramSize += MaxSessionIdLength - MinSessionIdLength;
                    if (length < effectiveMinDatagramSize)
                    {
                        throw new Exception("datagram too small to be valid for extended session id");
                    }
                    sessionIdLen = MaxSessionIdLength;
                    break;
                default:
                    throw new Exception("invalid session id length indicator");
            }

            parsedDatagram.SessionId = ConvertSessionIdBytesToHex(rawBytes, offset, sessionIdLen);
            offset += sessionIdLen;

            lenIndicator = rawBytes[offset];
            offset += 1;

            int windowIdLen;
            switch (lenIndicator)
            {
                case FalseIndicatorByte:
                    parsedDatagram.WindowId = ReadInt32BigEndian(rawBytes, offset);
                    windowIdLen = 4;
                    break;
                case TrueIndicatorByte:
                    effectiveMinDatagramSize += 4;
                    if (length < effectiveMinDatagramSize)
                    {
                        throw new Exception("datagram too small to be valid for extended window id");
                    }
                    parsedDatagram.WindowId = ReadInt64BigEndian(rawBytes, offset);
                    windowIdLen = 8;
                    break;
                default:
                    throw new Exception("invalid window id length indicator");
            }
            if (parsedDatagram.WindowId < 0)
            {
                throw new Exception("Negative window id not allowed");
            }
            offset += windowIdLen;

            parsedDatagram.SequenceNumber = ReadInt32BigEndian(rawBytes, offset);
            if (parsedDatagram.SequenceNumber < 0)
            {
                throw new Exception("Negative sequence number not allowed");
            }
            offset += 4;

            parsedDatagram.OpCode = rawBytes[offset];
            offset += 1;

            // Now read options until we encounter null terminator for all options, 
            // which is equivalent to empty string option name 
            string optionName = null;
            while (optionName != "")
            {
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
                    throw new Exception("null terminator for all options not found");
                }

                var optionNameOrValue = ConvertBytesToString(rawBytes, offset, nullTerminatorIndex - offset);
                offset = nullTerminatorIndex + 1;

                if (optionName == null)
                {
                    optionName = optionNameOrValue;
                }
                else
                {
                    if (parsedDatagram.Options == null)
                    {
                        parsedDatagram.Options = new ProtocolDatagramOptions();
                    }
                    try
                    {
                        parsedDatagram.Options.AddOption(optionName, optionNameOrValue);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Received invalid value for option {optionName}: {optionNameOrValue} ({ex})");
                    }

                    // important for loop: reset to null
                    optionName = null;
                }
            }

            parsedDatagram.DataBytes = rawBytes;
            parsedDatagram.DataOffset = offset;
            parsedDatagram.DataLength = endOffset - offset;

            return parsedDatagram;
        }

        public static string GenerateSessionId()
        {
            return DateTime.UtcNow.ToString("yyyMMddHHmmssfff") + Guid.NewGuid().ToString("n");
        }

        public byte[] ToRawDatagram()
        {
            byte[] rawBytes;
            using (var ms = new MemoryStream())
            {
                using (var writer = new BinaryWriter(ms))
                {
                    // Make space for expected data length.
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((byte)0);

                    // write out session id.
                    byte[] sessionId = ConvertSessionIdHexToBytes(SessionId);
                    if (sessionId.Length == MinSessionIdLength)
                    {
                        writer.Write(FalseIndicatorByte);
                    }
                    else if (sessionId.Length == MaxSessionIdLength)
                    {
                        writer.Write(TrueIndicatorByte);
                    }
                    else
                    {
                        throw new Exception($"Invalid session id length in bytes: {sessionId.Length}");
                    }
                    writer.Write(sessionId);

                    // Write out window id.
                    // the rule is all window ids >= 2^31 MUST be written out as 8 bytes.
                    if (WindowId > int.MaxValue)
                    {
                        writer.Write(TrueIndicatorByte);
                        writer.Write(WriteInt64BigEndian(WindowId));
                    }
                    else
                    {
                        // those less than 2^31 may be written as 4 or 8 bytes
                        writer.Write(FalseIndicatorByte);
                        writer.Write(WriteInt32BigEndian((int)WindowId));
                    }

                    writer.Write(WriteInt32BigEndian(SequenceNumber));

                    writer.Write(OpCode);

                    // write out all options.
                    if (Options != null)
                    {
                        foreach (var pair in Options.GenerateList())
                        {
                            var optionNameBytes = ConvertStringToBytes(pair[0]);
                            writer.Write(optionNameBytes);
                            writer.Write(NullTerminator);
                            var optionValueBytes = ConvertStringToBytes(pair[1]);
                            writer.Write(optionValueBytes);
                            writer.Write(NullTerminator);
                        }
                    }

                    writer.Write(NullTerminator);
                    if (DataBytes != null)
                    {
                        writer.Write(DataBytes, DataOffset, DataLength);
                    }
                }
                rawBytes = ms.ToArray();
            }

            if (ExpectedDatagramLength > 0 && ExpectedDatagramLength != rawBytes.Length)
            {
                throw new Exception($"expected datagram length != actual ({ExpectedDatagramLength} != {rawBytes.Length})");
            }

            InsertExpectedDataLength(rawBytes);

            return rawBytes;
        }

        private static void InsertExpectedDataLength(byte[] rawBytes)
        {
            byte[] intBytes = WriteInt32BigEndian(rawBytes.Length);
            rawBytes[0] = intBytes[0];
            rawBytes[1] = intBytes[1];
            rawBytes[2] = intBytes[2];
            rawBytes[3] = intBytes[3];
        }

        internal static byte[] ConvertStringToBytes(string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        internal static string ConvertBytesToString(byte[] data, int offset, int length)
        {
            return Encoding.UTF8.GetString(data, offset, length);
        }

        internal static string ConvertSessionIdBytesToHex(byte[] data, int offset, int len)
        {
            // send out lower case for similarity with other platforms (Java, Python, NodeJS, etc)
            return BitConverter.ToString(data, offset, len).Replace("-", "").ToLower();
        }

        internal static byte[] ConvertSessionIdHexToBytes(string hex)
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

        internal static byte[] WriteInt16BigEndian(short v)
        {
            byte[] intBytes = new byte[2];
            intBytes[0] = (byte)(0xff & (v >> 8));
            intBytes[1] = (byte)(0xff & v);
            return intBytes;
        }

        internal static byte[] WriteInt32BigEndian(int v)
        {
            byte[] intBytes = new byte[4];
            intBytes[0] = (byte)(0xff & (v >> 24));
            intBytes[1] = (byte)(0xff & (v >> 16));
            intBytes[2] = (byte)(0xff & (v >> 8));
            intBytes[3] = (byte)(0xff & v);
            return intBytes;
        }

        internal static byte[] WriteInt64BigEndian(long v)
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

        internal static short ReadInt16BigEndian(byte[] rawBytes, int offset)
        {
            byte a = rawBytes[offset];
            byte b = rawBytes[offset + 1];
            int v = (a << 8) | (b & 0xff);
            return (short)v;
        }

        internal static int ReadInt32BigEndian(byte[] rawBytes, int offset)
        {
            byte a = rawBytes[offset];
            byte b = rawBytes[offset + 1];
            byte c = rawBytes[offset + 2];
            byte d = rawBytes[offset + 3];
            int v = ((a & 0xff) << 24) | ((b & 0xff) << 16) |
                ((c & 0xff) << 8) | (d & 0xff);
            return v;
        }

        internal static long ReadInt64BigEndian(byte[] rawBytes, int offset)
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

        public static byte[] RetrieveData(List<ProtocolDatagram> messages, ProtocolDatagramOptions optionsReceiver = null)
        {
            var memoryStream = new MemoryStream();
            foreach (var msg in messages)
            {
                if (msg == null)
                {
                    break;
                }
                if (msg.Options != null && optionsReceiver != null)
                {
                    foreach (var pair in msg.Options.GenerateList())
                    {
                        optionsReceiver.AddOption(pair[0], pair[1]);
                    }
                }
                memoryStream.Write(msg.DataBytes, msg.DataOffset, msg.DataLength);
            }
            memoryStream.Flush();
            return memoryStream.ToArray();
        }
    }
}
