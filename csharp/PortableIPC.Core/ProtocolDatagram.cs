﻿using System;
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
        public const byte OpCodeClose = 5;
        public const byte OpCodeCloseAll = 11;

        private const byte NullTerminator = 0;

        public const int SessionIdLength = 50;

        // expected length, sessionId, opCode, timestamp, window id, 
        // sequence number, null separator and checksum are always present.
        public const int MinDatagramSize = 2 + SessionIdLength + 1 + 8 + 8 + 
            4 + 1 + 1;
        public const int DatagramSizeAbsoluteLimit = 65_507;

        // Reserve s_ prefix for known options at session layer.
        // Also reserver a_ for known options at application layer.
        public const string OptionNameIsLastInWindow = "s_last_in_window";
        public const string OptionNameDisableIdleTimeout = "s_no_idle_timeout";
        public const string OptionNameErrorCode = "s_err_code";

        public int ExpectedDatagramLength { get; set; }
        public string SessionId { get; set; }
        public byte OpCode { get; set; }
        public long Timestamp { get; set; }
        public long WindowId { get; set; }
        public int SequenceNumber { get; set; }
        public Dictionary<string, List<string>> Options { get; set; }
        public byte[] DataBytes { get; set; }
        public int DataOffset { get; set; }
        public int DataLength { get; set; }
        public byte Checksum { get; set; }

        // Known session layer options.
        public bool? IsLastInWindow { get; set; }
        public bool? DisableIdleTimeout { get; set; }
        public int? ErrorCode { get; set; }

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

            parsedDatagram.Timestamp = ReadInt64BigEndian(rawBytes, offset);
            offset += 8;

            parsedDatagram.WindowId = ReadInt64BigEndian(rawBytes, offset);
            if (parsedDatagram.WindowId < 0)
            {
                throw new Exception("Negative window id not allowed");
            }
            offset += 8;

            parsedDatagram.SequenceNumber = ReadInt32BigEndian(rawBytes, offset);
            if (parsedDatagram.SequenceNumber < 0)
            {
                throw new Exception("Negative sequence number not allowed");
            }
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
                            case OptionNameDisableIdleTimeout:
                                if (parsedDatagram.DisableIdleTimeout == null)
                                {
                                    parsedDatagram.DisableIdleTimeout = ParseOptionAsBoolean(optionNameOrValue);
                                }
                                break;
                            case OptionNameErrorCode:
                                if (parsedDatagram.ErrorCode == null)
                                {
                                    parsedDatagram.ErrorCode = ParseOptionAsInt32(optionNameOrValue);
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

                    byte[] sessionId = ConvertStringToBytes(SessionId);
                    if (sessionId.Length != SessionIdLength)
                    {
                        throw new Exception($"Received invalid session id: {SessionId} produces {sessionId.Length} bytes");
                    }
                    writer.Write(sessionId);

                    writer.Write(OpCode);

                    WriteInt64BigEndian(writer, Timestamp);
                    WriteInt64BigEndian(writer, WindowId);
                    WriteInt32BigEndian(writer, SequenceNumber);

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

                    // make space for checksum
                    writer.Write((byte)0);
                }
                rawBytes = ms.ToArray();
            }

            if (rawBytes.Length > DatagramSizeAbsoluteLimit)
            {
                throw new Exception("datagram too large to send");
            }

            if (ExpectedDatagramLength > 0 && ExpectedDatagramLength != rawBytes.Length)
            {
                throw new Exception($"expected datagram length != actual ({ExpectedDatagramLength} != {rawBytes.Length})");
            }

            InsertExpectedDataLength(rawBytes);
            InsertChecksum(rawBytes);

            return rawBytes;
        }

        private Dictionary<string, string> GatherKnownOptions()
        {
            var knownOptions = new Dictionary<string, string>();
            
            if (IsLastInWindow != null)
            {
                knownOptions.Add(OptionNameIsLastInWindow, IsLastInWindow.ToString());
            }
            if (DisableIdleTimeout != null)
            {
                knownOptions.Add(OptionNameDisableIdleTimeout, DisableIdleTimeout.ToString());
            }
            if (ErrorCode != null)
            {
                knownOptions.Add(OptionNameErrorCode, ErrorCode.ToString());
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
                ((c & 0xff) << 40) | ((d & 0xff) << 32) | ((e & 0xff) << 24) | ((f & 0xff) << 16) |
                ((g & 0xff) << 8) | (h & 0xff);
            return v;
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
