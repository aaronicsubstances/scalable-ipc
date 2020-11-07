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
        public const byte OpCodeOpen = 1;
        public const byte OpCodeOpenAck = 2;
        public const byte OpCodeData = 3;
        public const byte OpCodeAck = 4;
        public const byte OpCodeClose = 5;
        public const byte OpCodeCloseAll = 11;

        private const byte NullTerminator = 0;
        private const byte FalseIndicatorByte = 0;
        private const byte TrueIndicatorByte = 0xff;

        public const int MinSessionIdLength = 16;
        public const int MaxSessionIdLength = 32;

        public const long MinWindowIdCrossOverLimit = 1_000;
        public const long MaxWindowIdCrossOverLimit = 9_000_000_000_000_000_000L;

        // the 2 length indicator booleans, expected length, sessionId, opCode, window id, 
        // sequence number, null separator are always present.
        public const int MinDatagramSize = 2 + 4 + MinSessionIdLength + 1 + 4 + 4 + 1;

        // Reserve s_ prefix for known options at session layer.

        // NB: only applies to data exchange phase.
        public const string OptionNameIdleTimeout = "s_idle_timeout";

        public const string OptionNameErrorCode = "s_err_code";
        public const string OptionNameIsLastOpenRequest = "s_last_open";
        public const string OptionNameIsWindowFull = "s_window_full";
        public const string OptionNameIsLastInWindow = "s_last_in_window";

        public int ExpectedDatagramLength { get; set; }
        public string SessionId { get; set; }
        public byte OpCode { get; set; }
        public long WindowId { get; set; }
        public int SequenceNumber { get; set; }
        public Dictionary<string, List<string>> Options { get; set; }

        public byte[] DataBytes { get; set; }
        public int DataOffset { get; set; }
        public int DataLength { get; set; }

        // Known session layer options.
        public bool? IsLastInWindow { get; set; }
        public int? IdleTimeoutSecs { get; set; }
        public int? ErrorCode { get; set; }
        public bool? IsLastOpenRequest { get; set; }
        public bool? IsWindowFull { get; set; }

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

                var optionNameOrValue = ConvertBytesToString(rawBytes, offset, nullTerminatorIndex);
                offset = nullTerminatorIndex + 1;

                if (optionName == null)
                {
                    optionName = optionNameOrValue;
                }
                else
                {
                    if (parsedDatagram.Options == null)
                    {
                        parsedDatagram.Options = new Dictionary<string, List<string>>();
                    }
                    List<string> optionValues;
                    if (parsedDatagram.Options.ContainsKey(optionName))
                    {
                        optionValues = parsedDatagram.Options[optionName];
                    }
                    else
                    {
                        optionValues = new List<string>();
                        parsedDatagram.Options.Add(optionName, optionValues);
                    }
                    optionValues.Add(optionNameOrValue);

                    // Now identify known options.
                    // In case of repetition, first one wins.
                    try
                    {
                        switch (optionName)
                        {
                            case OptionNameIsLastInWindow:
                                if (parsedDatagram.IsLastInWindow == null)
                                {
                                    parsedDatagram.IsLastInWindow = ParseOptionAsBoolean(optionNameOrValue);
                                }
                                break;
                            case OptionNameIdleTimeout:
                                if (parsedDatagram.IdleTimeoutSecs == null)
                                {
                                    parsedDatagram.IdleTimeoutSecs = ParseOptionAsInt32(optionNameOrValue);
                                }
                                break;
                            case OptionNameErrorCode:
                                if (parsedDatagram.ErrorCode == null)
                                {
                                    parsedDatagram.ErrorCode = ParseOptionAsInt32(optionNameOrValue);
                                }
                                break;
                            case OptionNameIsLastOpenRequest:
                                if (parsedDatagram.IsLastOpenRequest == null)
                                {
                                    parsedDatagram.IsLastOpenRequest = ParseOptionAsBoolean(optionNameOrValue);
                                }
                                break;
                            case OptionNameIsWindowFull:
                                if (parsedDatagram.IsWindowFull == null)
                                {
                                    parsedDatagram.IsWindowFull = ParseOptionAsBoolean(optionNameOrValue);
                                }
                                break;
                            default:
                                break;
                        }
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

        public byte[] ToRawDatagram(bool includeKnownOptions)
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
                        WriteInt64BigEndian(writer, WindowId);
                    }
                    else
                    {
                        // those less than 2^31 may be written as 4 or 8 bytes
                        writer.Write(FalseIndicatorByte);
                        WriteInt32BigEndian(writer, (int)WindowId);
                    }

                    WriteInt32BigEndian(writer, SequenceNumber);

                    writer.Write(OpCode);

                    // write out all options, starting with known ones.
                    if (includeKnownOptions)
                    {
                        var knownOptions = GatherKnownOptions();
                        foreach (var kvp in knownOptions)
                        {
                            var optionNameBytes = ConvertStringToBytes(kvp.Key);
                            writer.Write(optionNameBytes);
                            writer.Write(NullTerminator);
                            var optionValueBytes = ConvertStringToBytes(kvp.Value);
                            writer.Write(optionValueBytes);
                            writer.Write(NullTerminator);
                        }
                    }
                    if (Options != null)
                    {
                        foreach (var kvp in Options)
                        {
                            var optionNameBytes = ConvertStringToBytes(kvp.Key);
                            foreach (var optionValue in kvp.Value)
                            {
                                writer.Write(optionNameBytes);
                                writer.Write(NullTerminator);
                                var optionValueBytes = ConvertStringToBytes(optionValue);
                                writer.Write(optionValueBytes);
                                writer.Write(NullTerminator);
                            }
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

        private Dictionary<string, string> GatherKnownOptions()
        {
            var knownOptions = new Dictionary<string, string>();
            
            if (IsLastInWindow != null)
            {
                knownOptions.Add(OptionNameIsLastInWindow, IsLastInWindow.ToString());
            }
            if (IdleTimeoutSecs != null)
            {
                knownOptions.Add(OptionNameIdleTimeout, IdleTimeoutSecs.ToString());
            }
            if (ErrorCode != null)
            {
                knownOptions.Add(OptionNameErrorCode, ErrorCode.ToString());
            }
            if (IsLastOpenRequest != null)
            {
                knownOptions.Add(OptionNameIsLastOpenRequest, IsLastOpenRequest.ToString());
            }
            if (IsWindowFull != null)
            {
                knownOptions.Add(OptionNameIsWindowFull, IsWindowFull.ToString());
            }
            return knownOptions;
        }

        internal static void InsertExpectedDataLength(byte[] rawBytes)
        {
            WriteInt32BigEndian(rawBytes, 0, rawBytes.Length);
        }

        internal static short ParseOptionAsInt16(string optionValue)
        {
            return short.Parse(optionValue);
        }

        internal static int ParseOptionAsInt32(string optionValue)
        {
            return int.Parse(optionValue);
        }

        internal static bool ParseOptionAsBoolean(string optionValue)
        {
            switch (optionValue.ToLowerInvariant())
            {
                case "true":
                    return true;
                case "false":
                    return false;
            }
            throw new Exception($"expected {true} or {false}");
        }

        internal static string ConvertSessionIdBytesToHex(byte[] data, int offset, int len)
        {
            return BitConverter.ToString(data, offset, len).Replace("-", "");
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
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        internal static byte[] ConvertStringToBytes(string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        internal static string ConvertBytesToString(byte[] data, int offset, int length)
        {
            return Encoding.UTF8.GetString(data, offset, length);
        }

        internal static void WriteInt16BigEndian(BinaryWriter writer, short v)
        {
            writer.Write((byte)(0xff & (v >> 8)));
            writer.Write((byte)(0xff & v));
        }

        internal static void WriteInt32BigEndian(byte[] rawBytes, int offset, int v)
        {
            rawBytes[offset] = (byte)(0xff & (v >> 24));
            rawBytes[offset + 1] = (byte)(0xff & (v >> 16));
            rawBytes[offset + 2] = (byte)(0xff & (v >> 8));
            rawBytes[offset + 3] = (byte)(0xff & v);
        }

        internal static void WriteInt32BigEndian(BinaryWriter writer, int v)
        {
            writer.Write((byte)(0xff & (v >> 24)));
            writer.Write((byte)(0xff & (v >> 16)));
            writer.Write((byte)(0xff & (v >> 8)));
            writer.Write((byte)(0xff & v));
        }

        internal static void WriteInt64BigEndian(BinaryWriter writer, long v)
        {
            writer.Write((byte)(0xff & (v >> 56)));
            writer.Write((byte)(0xff & (v >> 48)));
            writer.Write((byte)(0xff & (v >> 40)));
            writer.Write((byte)(0xff & (v >> 32)));
            writer.Write((byte)(0xff & (v >> 24)));
            writer.Write((byte)(0xff & (v >> 16)));
            writer.Write((byte)(0xff & (v >> 8)));
            writer.Write((byte)(0xff & v));
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
            long v = ((a & 0xff) << 56) | ((b & 0xff) << 48) |
                ((c & 0xff) << 40) | ((d & 0xff) << 32) |
                ((e & 0xff) << 24) | ((f & 0xff) << 16) |
                ((g & 0xff) << 8) | (h & 0xff);
            return v;
        }

        public static long ComputeNextWindowIdToSend(long nextWindowIdToSend)
        {
            // Perform simple predicatable computation of increasing and wrapping around.
            // max crossover limit.
            if (nextWindowIdToSend >= MaxWindowIdCrossOverLimit)
            {
                // return any value not exceeding min crossover limit.
                return 0;
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
            // 1. if current value has crossed max crossover limit,
            //    then next value must be less than or equal to min crossover limit.
            // 2. else next value must be larger than current value by a difference which is 
            //    less than or equal to min crossover limit.
            if (lastWindowIdProcessed < 0 || lastWindowIdProcessed >= MaxWindowIdCrossOverLimit)
            {
                return v >= 0 && v <= MinWindowIdCrossOverLimit;
            }
            else
            {
                return v > lastWindowIdProcessed && (v - lastWindowIdProcessed) <= MinWindowIdCrossOverLimit;
            }
        }

        public static byte[] RetrieveData(List<ProtocolDatagram> messages, Dictionary<string, List<string>> optionsReceiver)
        {
            var memoryStream = new MemoryStream();
            foreach (var msg in messages)
            {
                if (msg == null)
                {
                    break;
                }
                if (msg.Options != null)
                {
                    foreach (var kvp in msg.Options)
                    {
                        optionsReceiver.Add(kvp.Key, kvp.Value);
                    }
                }
                memoryStream.Write(msg.DataBytes, msg.DataOffset, msg.DataLength);
            }
            memoryStream.Flush();
            return memoryStream.ToArray();
        }
    }
}
