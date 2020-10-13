using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace PortableIPC.Core
{
    public class ProtocolDatagram
    {
        public const byte OpCodeOpen = 1;
        public const byte OpCodeOpenAck = 2;
        public const byte OpCodeData = 3;
        public const byte OpCodeAck = 4;
        public const byte OpCodeClose = 5;
        public const byte OpCodeCloseAll = 11;

        private const byte NullTerminator = 0;

        public const int SessionIdLength = 50;

        // expected length, sessionId, opCode, sequence number, null separator and checksum are always present.
        private const int MinDatagramSize = 2 + SessionIdLength + 1 + 4 + 1 + 1;
        private const int MaxDatagramSize = 60_000;

        // reserve s_ prefix for known options at session layer.
        // later will reserver a_ for known options at application layer.
        public const string OptionNameRetryCount = "s_retry_count";
        public const string OptionNameDataWindowSize = "s_data_window_size";
        public const string OptionNameMaxPduSize = "s_max_pdu_size";
        public const string OptionNameIsLastInDataWindow = "s_last_in_data_window";
        public const string OptionNameIsLastInOpenRequest = "s_last_in_open";
        public const string OptionNameIdleTimeout = "s_idle_timeout";
        public const string OptionNameAckTimeout = "s_ack_timeout";

        // use code number prefix like 200 OK, 500 Internal Server Error
        public const string OptionNameErrorMessage = "s_err_msg";

        public int ExpectedDatagramLength { get; set; }
        public string SessionId { get; set; }
        public byte OpCode { get; set; }
        public int SequenceNumber { get; set; }
        public Dictionary<string, List<string>> Options { get; set; }
        public byte[] DataBytes { get; set; }
        public int DataOffset { get; set; }
        public int DataLength { get; set; }
        public byte Checksum { get; set; }

        // Known session layer options.
        public List<int> RetryCount { get; set; }
        public List<int> DataWindowSize { get; set; }

        public List<int> MaxPduSize { get; set; }
        public bool? IsLastInDataWindow { get; set; }
        public bool? IsLastInOpenRequest { get; set; }

        public List<int> IdleTimeoutSecs { get; set; }
        public List<int> AckTimeoutSecs { get; set; }
        public List<string> ErrorMessage { get; set; }

        public string FormatErrorMessage()
        {
            if (ErrorMessage == null)
            {
                return null;
            }
            return string.Join("\n", ErrorMessage);
        }

        public static ProtocolDatagram Parse(byte[] rawBytes, int offset, int length)
        {
            if (length < MinDatagramSize)
            {
                throw new Exception("datagram too small to be valid");
            }

            // liberally accept any size once it made it through network without errors.

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

            if (parsedDatagram.SequenceNumber < 0)
            {
                throw new Exception("Negative sequence number not allowed");
            }

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
                    try
                    {
                        switch (optionName)
                        {
                            case OptionNameRetryCount:
                                if (parsedDatagram.RetryCount == null)
                                {
                                    parsedDatagram.RetryCount = new List<int>();
                                }
                                parsedDatagram.RetryCount.Add(ParseOptionAsInt16(optionNameOrValue));
                                break;
                            case OptionNameDataWindowSize:
                                if (parsedDatagram.DataWindowSize == null)
                                {
                                    parsedDatagram.DataWindowSize = new List<int>();
                                }
                                parsedDatagram.DataWindowSize.Add(ParseOptionAsInt16(optionNameOrValue));
                                break;
                            case OptionNameMaxPduSize:
                                if (parsedDatagram.MaxPduSize == null)
                                {
                                    parsedDatagram.MaxPduSize = new List<int>();
                                }
                                parsedDatagram.MaxPduSize.Add(ParseOptionAsInt16(optionNameOrValue));
                                break;
                            case OptionNameIsLastInDataWindow:
                                // In case of repetition, first one wins.
                                if (parsedDatagram.IsLastInDataWindow == null)
                                {
                                    parsedDatagram.IsLastInDataWindow = ParseOptionAsBoolean(optionNameOrValue);
                                }
                                break;
                            case OptionNameIsLastInOpenRequest:
                                // In case of repetition, first one wins.
                                if (parsedDatagram.IsLastInOpenRequest == null)
                                {
                                    parsedDatagram.IsLastInOpenRequest = ParseOptionAsBoolean(optionNameOrValue);
                                }
                                break;
                            case OptionNameIdleTimeout:
                                if (parsedDatagram.IdleTimeoutSecs == null)
                                {
                                    parsedDatagram.IdleTimeoutSecs = new List<int>();
                                }
                                parsedDatagram.IdleTimeoutSecs.Add(ParseOptionAsInt32(optionNameOrValue));
                                break;
                            case OptionNameAckTimeout:
                                if (parsedDatagram.AckTimeoutSecs == null)
                                {
                                    parsedDatagram.AckTimeoutSecs = new List<int>();
                                }
                                parsedDatagram.AckTimeoutSecs.Add(ParseOptionAsInt32(optionNameOrValue));
                                break;
                            case OptionNameErrorMessage:
                                if (parsedDatagram.ErrorMessage == null)
                                {
                                    parsedDatagram.ErrorMessage = new List<string>();
                                }
                                parsedDatagram.ErrorMessage.Add(optionNameOrValue);
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

                    byte[] sessionId = ConvertStringToBytes(SessionId);
                    if (sessionId.Length != SessionIdLength)
                    {
                        throw new Exception($"Received invalid session id: {SessionId} produces {sessionId.Length} bytes");
                    }
                    writer.Write(sessionId);

                    writer.Write(OpCode);

                    WriteInt32BigEndian(writer, SequenceNumber);

                    // write out all options, starting with known ones.
                    if (includeKnownOptions)
                    {
                        var knownOptions = GatherKnownOptions();
                        foreach (var kvp in knownOptions)
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

        private Dictionary<string, List<string>> GatherKnownOptions()
        {
            var knownOptions = new Dictionary<string, List<string>>();
            if (RetryCount != null)
            {
                knownOptions.Add(OptionNameRetryCount, RetryCount.Select(x => x.ToString()).ToList());
            }
            if (DataWindowSize != null)
            {
                knownOptions.Add(OptionNameDataWindowSize, DataWindowSize.Select(x => x.ToString()).ToList());
            }
            if (MaxPduSize != null)
            {
                knownOptions.Add(OptionNameMaxPduSize, MaxPduSize.Select(x => x.ToString()).ToList());
            }
            if (IsLastInDataWindow != null)
            {
                knownOptions.Add(OptionNameIsLastInDataWindow, new List<string> { IsLastInDataWindow.ToString() });
            }
            if (IsLastInOpenRequest != null)
            {
                knownOptions.Add(OptionNameIsLastInOpenRequest, new List<string> { IsLastInOpenRequest.ToString() });
            }
            if (IdleTimeoutSecs != null)
            {
                knownOptions.Add(OptionNameIdleTimeout, IdleTimeoutSecs.Select(x => x.ToString()).ToList());
            }
            if (AckTimeoutSecs != null)
            {
                knownOptions.Add(OptionNameAckTimeout, AckTimeoutSecs.Select(x => x.ToString()).ToList());
            }
            if (ErrorMessage != null)
            {
                knownOptions.Add(OptionNameErrorMessage, ErrorMessage);
            }
            return knownOptions;
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

        public static bool ValidateSequenceNumbers(List<int> sequenceNumbers)
        {
            // Check for strictly monotonically increasing sequence.
            // By this check, sequence numbers are not allowed to straddle maximum 32-bit integer boundary.
            for (int i = 1; i < sequenceNumbers.Count; i++)
            {
                if (sequenceNumbers[i] < sequenceNumbers[i - 1])
                {
                    return false;
                }
            }
            
            return false;
        }

        public static int ComputeNextSequenceStart(int currSeq, int windowSize)
        {
            // Detect wrap around and start from 0.
            if (currSeq < 0 || currSeq + windowSize < 0)
            {
                return 0;
            }

            // Ensure next sequence start is a multiple of window size.
            currSeq++;
            int rem = currSeq % windowSize;
            if (rem != 0)
            {
                currSeq += (windowSize - rem);
            }
            return currSeq;
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
