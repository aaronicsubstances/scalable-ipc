using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PortableIPC.Core
{
    public class ProtocolDatagram
    {
        public const byte OpCodeOpen = 1;
        public const byte OpCodeOpenAck = 2;
        public const byte OpCodeData = 3;
        public const byte OpCodeAck = 4;
        public const byte OpCodeClose = 10;
        public const byte OpCodeCloseAll = 11;
        public const byte OpCodeError = 50;

        private const byte NullTerminator = 0;

        public const int SessionIdLength = 50;

        // expected length, sessionId, opCode, sequence number, null separator and checksum are always present.
        private const int MinDatagramSize = 2 + SessionIdLength + 1 + 4 + 1 + 1;
        private const int MaxDatagramSize = 60_000;

        // reserve s_ prefix for known options
        private const string OptionNameRetryCount = "s_retry_count";
        private const string OptionNameWindowSize = "s_window_size";
        private const string OptionNameMaxPduSize = "s_max_pdu_size";
        private const string OptionNameIsLastInWindow = "s_last_in_window";
        private const string OptionNameIdleTimeout = "s_idle_timeout";
        private const string OptionNameAckTimeout = "s_ack_timeout";
        private const string OptionNameErrorCode = "s_err_code";
        private const string OptionNameErrorMessage = "s_err_msg";

        public int ExpectedDatagramLength { get; set; }
        public string SessionId { get; set; }
        public byte OpCode { get; set; }
        public int SequenceNumber { get; set; }
        public byte Checksum { get; set; }
        public byte[] DataBytes { get; set; }
        public int DataOffset { get; set; }
        public int DataLength { get; set; }
        public Dictionary<string, List<string>> RemainingOptions { get; set; }

        // Known options.
        public int? RetryCount { get; set; }
        public int? WindowSize { get; set; }
        public int? MaxPduSize { get; set; }
        public bool? IsLastInWindow { get; set; }
        public int? IdleTimeoutSecs { get; set; }
        public int? AckTimeoutSecs { get; set; }
        public int? ErrorCode { get; set; }
        public string ErrorMessage { get; set; }

        public static ProtocolDatagram Parse(byte[] rawBytes, int offset, int length)
        {
            if (length < MinDatagramSize)
            {
                throw new Exception("datagram too small to be valid");
            }
            if (length > MaxDatagramSize)
            {
                throw new Exception("datagram too large to be valid");
            }

            var parsedDatagram = new ProtocolDatagram();

            // validate length and checksum.
            int expectedDatagramLength = ReadInt16BigEndian(rawBytes, offset);
            if (expectedDatagramLength != length)
            {
                throw new Exception("expected datagram length incorrect");
            }
            ValidateChecksum(rawBytes, offset, length);

            int endOffset = offset + length - 1; // exempt checksum

            parsedDatagram.ExpectedDatagramLength = expectedDatagramLength;
            offset += 2; // skip past expected data length;

            parsedDatagram.SessionId = ConvertBytesToString(rawBytes, offset, SessionIdLength);
            offset += SessionIdLength;

            parsedDatagram.OpCode = rawBytes[offset];
            offset += 1;

            parsedDatagram.SequenceNumber = ReadInt32BigEndian(rawBytes, offset);
            offset += 4;

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
                    throw new ProtocolSessionException(parsedDatagram.SessionId, "null terminator for all options not found");
                }

                var optionNameOrValue = ConvertBytesToString(rawBytes, offset, nullTerminatorIndex);
                offset = nullTerminatorIndex + 1;

                if (optionName == null)
                {
                    optionName = optionNameOrValue;
                }
                else
                {
                    // Identify known options first. In case of repetition, first one wins.
                    bool knownOptionEncountered = true;
                    switch (optionName)
                    {
                        case OptionNameRetryCount:
                            if (parsedDatagram.RetryCount == null)
                            {
                                parsedDatagram.RetryCount = ParseOptionAsInt16(optionName, optionNameOrValue);
                            }
                            break;
                        case OptionNameWindowSize:
                            if (parsedDatagram.WindowSize == null)
                            {
                                parsedDatagram.WindowSize = ParseOptionAsInt16(optionName, optionNameOrValue);
                            }
                            break;
                        case OptionNameMaxPduSize:
                            if (parsedDatagram.MaxPduSize == null)
                            {
                                parsedDatagram.MaxPduSize = ParseOptionAsInt16(optionName, optionNameOrValue);
                            }
                            break;
                        case OptionNameIsLastInWindow:
                            if (parsedDatagram.IsLastInWindow == null)
                            {
                                parsedDatagram.IsLastInWindow = ParseOptionAsBoolean(optionName, optionNameOrValue);
                            }
                            break;
                        case OptionNameIdleTimeout:
                            if (parsedDatagram.IdleTimeoutSecs == null)
                            {
                                parsedDatagram.IdleTimeoutSecs = ParseOptionAsInt32(optionName, optionNameOrValue);
                            }
                            break;
                        case OptionNameAckTimeout:
                            if (parsedDatagram.AckTimeoutSecs == null)
                            {
                                parsedDatagram.AckTimeoutSecs = ParseOptionAsInt32(optionName, optionNameOrValue);
                            }
                            break;
                        case OptionNameErrorCode:
                            if (parsedDatagram.ErrorCode == null)
                            {
                                parsedDatagram.ErrorCode = ParseOptionAsInt16(optionName, optionNameOrValue);
                            }
                            break;
                        case OptionNameErrorMessage:
                            if (parsedDatagram.ErrorMessage == null)
                            {
                                parsedDatagram.ErrorMessage = optionNameOrValue;
                            }
                            break;
                        default:
                            knownOptionEncountered = false;
                            break;
                    }
                    if (!knownOptionEncountered)
                    {
                        if (parsedDatagram.RemainingOptions == null)
                        {
                            parsedDatagram.RemainingOptions = new Dictionary<string, List<string>>();
                        }
                        List<string> optionValues;
                        if (parsedDatagram.RemainingOptions.ContainsKey(optionName))
                        {
                            optionValues = parsedDatagram.RemainingOptions[optionName];
                        }
                        else
                        {
                            optionValues = new List<string>();
                            parsedDatagram.RemainingOptions.Add(optionName, optionValues);
                        }
                        optionValues.Add(optionNameOrValue);
                    }
                    optionName = null;
                }
            }

            parsedDatagram.DataBytes = rawBytes;
            parsedDatagram.DataOffset = offset;
            parsedDatagram.DataLength = endOffset - offset;

            return parsedDatagram;
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

                    byte[] sessionId = ConvertStringToBytes(SessionId);
                    if (sessionId.Length != SessionIdLength)
                    {
                        throw new Exception($"Received invalid session id: {SessionId} produces {sessionId.Length} bytes");
                    }
                    writer.Write(sessionId);

                    writer.Write(OpCode);

                    WriteInt32BigEndian(writer, SequenceNumber);

                    // write out all options, starting with known ones.
                    var knownOptions = new Dictionary<string, string>();
                    if (RetryCount != null)
                    {
                        knownOptions.Add(OptionNameRetryCount, RetryCount.ToString());
                    }
                    if (WindowSize != null)
                    {
                        knownOptions.Add(OptionNameWindowSize, WindowSize.ToString());
                    }
                    if (MaxPduSize != null)
                    {
                        knownOptions.Add(OptionNameMaxPduSize, MaxPduSize.ToString());
                    }
                    if (IsLastInWindow != null)
                    {
                        knownOptions.Add(OptionNameIsLastInWindow, IsLastInWindow.ToString());
                    }
                    if (IdleTimeoutSecs != null)
                    {
                        knownOptions.Add(OptionNameIdleTimeout, IdleTimeoutSecs.ToString());
                    }
                    if (AckTimeoutSecs != null)
                    {
                        knownOptions.Add(OptionNameAckTimeout, AckTimeoutSecs.ToString());
                    }
                    if (ErrorCode != null)
                    {
                        knownOptions.Add(OptionNameErrorCode, ErrorCode.ToString());
                    }
                    if (ErrorMessage != null)
                    {
                        knownOptions.Add(OptionNameErrorMessage, ErrorMessage);
                    }
                    foreach (var kvp in knownOptions)
                    {
                        var optionNameBytes = ConvertStringToBytes(kvp.Key);
                        writer.Write(optionNameBytes);
                        writer.Write(NullTerminator);
                        var optionValueBytes = ConvertStringToBytes(kvp.Value);
                        writer.Write(optionValueBytes);
                        writer.Write(NullTerminator);
                    }
                    if (RemainingOptions != null)
                    {
                        foreach (var kvp in RemainingOptions)
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
                    
                    // make space for checksum
                    writer.Write((byte) 0);
                }
                rawBytes = ms.ToArray();
            }

            if (rawBytes.Length > MaxDatagramSize)
            {
                throw new Exception("datagram too large to send");
            }

            InsertExpectedDataLength(rawBytes);
            InsertChecksum(rawBytes);

            return rawBytes;
        }

        internal static void InsertExpectedDataLength(byte[] rawBytes)
        {
            WriteInt16BigEndian(rawBytes, 0, (short)rawBytes.Length);
        }

        internal static void InsertChecksum(byte[] rawBytes)
        {
            rawBytes[rawBytes.Length - 1] = CalculateLongitudinalParityCheck(rawBytes, 0, rawBytes.Length - 1);
        }

        internal static void ValidateChecksum(byte[] rawBytes, int offset, int length)
        {
            byte expectedLrc = CalculateLongitudinalParityCheck(rawBytes, offset, length - 1);
            if (rawBytes[offset + length - 1] != expectedLrc)
            {
                throw new Exception("checksum error");
            }
        }

        internal static byte CalculateLongitudinalParityCheck(byte[] byteData, int offset, int length)
        {
            byte chkSumByte = 0x00;
            for (int i = offset; i < offset + length; i++)
            {
                chkSumByte ^= byteData[i];
            }
            return chkSumByte;
        }

        internal static short ParseOptionAsInt16(string optionName, string optionValue)
        {
            if (short.TryParse(optionValue, out short val))
            {
                return val;
            }
            throw new Exception($"Received invalid value for option {optionName}: {optionValue}");
        }

        internal static int ParseOptionAsInt32(string optionName, string optionValue)
        {
            if (int.TryParse(optionValue, out int val))
            {
                return val;
            }
            throw new Exception($"Received invalid value for option {optionName}: {optionValue}");
        }

        internal static bool ParseOptionAsBoolean(string optionName, string optionValue)
        {
            switch (optionValue.ToLowerInvariant())
            {
                case "true":
                    return true;
                case "false":
                    return false;
            }
            throw new Exception($"Received invalid value for option {optionName}: {optionValue}");
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

        internal static void WriteInt16BigEndian(byte[] rawBytes, int offset, short v)
        {
            rawBytes[offset] = (byte)(0xff & (v >> 8));
            rawBytes[offset + 1] = (byte)(0xff & v);
        }

        internal static void WriteInt32BigEndian(BinaryWriter writer, int v)
        {
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
    }
}
