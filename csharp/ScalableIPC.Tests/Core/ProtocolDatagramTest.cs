using ScalableIPC.Core;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ScalableIPC.Tests.Core
{
    public class ProtocolDatagramTest
    {
        [Theory]
        [MemberData(nameof(CreateConvertBytesToHexData))]
        public void TestConvertBytesToHex(byte[] data, int offset, int length, string expected)
        {
            string actual = ProtocolDatagram.ConvertBytesToHex(data, offset, length);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateConvertBytesToHexData()
        {
            return new List<object[]>
            {
                new object[]{ new byte[] { }, 0, 0, "" },
                new object[]{ new byte[] { 0xFF }, 0, 1, "ff" },
                new object[]{ new byte[] { 0, 0x68, 0x65, 0x6c }, 0, 4,
                    "0068656c" },
                new object[]{ new byte[] { 0x01, 0x68, 0x65, 0x6c }, 0, 4,
                    "0168656c" },
                new object[]{ new byte[] { 0, 0x68, 0x65, 0x6c, 0x6c, 0x6f, 0x20, 0x77, 0x6f, 0x72, 0x6c, 0x64, 0 }, 1, 11, 
                    "68656c6c6f20776f726c64" },
            };
        }

        [Theory]
        [MemberData(nameof(CreateConvertHexToBytesData))]
        public void TestConvertHexToBytes(string hex, byte[] expected)
        {
            byte[] actual = ProtocolDatagram.ConvertHexToBytes(hex);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateConvertHexToBytesData()
        {
            return new List<object[]>
            {
                new object[]{ "", new byte[] { } },
                new object[]{ "ff", new byte[] { 0xFF } },
                new object[]{ "FF", new byte[] { 0xFF } },
                new object[]{ "0068656c", new byte[] { 0, 0x68, 0x65, 0x6c } },
                new object[]{ "68656C6c6F20776F726C64", new byte[] { 0x68, 0x65, 0x6c, 0x6c, 0x6f,
                    0x20, 0x77, 0x6f, 0x72, 0x6c, 0x64 } },
            };
        }

        [Theory]
        [MemberData(nameof(CreateTestComputeNextWindowIdToSendData))]
        public void TestComputeNextWindowIdToSend(long nextWindowIdToSend, long expected)
        {
            long actual = ProtocolDatagram.ComputeNextWindowIdToSend(nextWindowIdToSend);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestComputeNextWindowIdToSendData()
        {
            return new List<object[]>
            {
                new object[]{ 0, 1 },
                new object[]{ 1, 2 },
                new object[]{ 999, 1000 },
                new object[]{ 1000, 1001 },
                new object[]{ 1001, 1002 },
                new object[]{ int.MaxValue - 1, int.MaxValue },
                new object[]{ int.MaxValue, int.MaxValue + 1L },
                new object[]{ int.MaxValue + 1L, int.MaxValue + 2L },
                new object[]{ 9_000_000_000_000_000_000 - 2, 9_000_000_000_000_000_000 - 1 },
                new object[]{ 9_000_000_000_000_000_000 - 1, 9_000_000_000_000_000_000 },
                new object[]{ 9_000_000_000_000_000_000, 1 },
                new object[]{ 9_000_000_000_000_000_000 + 1, 1 },
                new object[]{ long.MaxValue, 1 },
            };
        }

        [Theory]
        [MemberData(nameof(CreateTestIsReceivedWindowIdValidData))]
        public void TestIsReceivedWindowIdValid(long v, long lastWindowIdProcessed, bool expected)
        {
            bool actual = ProtocolDatagram.IsReceivedWindowIdValid(v, lastWindowIdProcessed);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestIsReceivedWindowIdValidData()
        {
            return new List<object[]>
            {
                new object[]{ 0, 1, false },
                new object[]{ 1, 2, false },
                new object[]{ 999, 1000, false },
                new object[]{ 1000, 1001, false },
                new object[]{ 1001, 1002, false },
                new object[]{ int.MaxValue - 1, int.MaxValue, false },
                new object[]{ int.MaxValue, int.MaxValue + 1L, false },
                new object[]{ int.MaxValue + 1L, int.MaxValue + 2L, false },
                new object[]{ 9_000_000_000_000_000_000 - 2, 9_000_000_000_000_000_000 - 1, false },
                new object[]{ 9_000_000_000_000_000_000 - 1, 9_000_000_000_000_000_000, false },
                new object[]{ 9_000_000_000_000_000_000, 0, false },
                new object[]{ 9_000_000_000_000_000_000 + 1, 0, false },
                new object[]{ long.MaxValue, 0, false },
                new object[]{ 0, 0, false },
                new object[]{ 160, 160, false },
                new object[]{ 0, -1, true },
                new object[]{ 160, -1, false },
                new object[]{ 161, 160, true },
                new object[]{ 999, 0, true },
                new object[]{ 1000, 0, true },
                new object[]{ 1001, 0, false },
                new object[]{ 1000, 1, true },
                new object[]{ 1001, 1, true },
                new object[]{ 1002, 1, false },
                new object[]{ 9_000_000_000_000_000_000 + 2, 9_000_000_000_000_000_000 - 2, true },
                new object[]{ 9_000_000_000_000_000_000 + 999, 9_000_000_000_000_000_000 - 1, true },
                new object[]{ 9_000_000_000_000_000_000 + 1000, 9_000_000_000_000_000_000 - 1, false },
                new object[]{ 9_000_000_000_000_000_000 + 1, 9_000_000_000_000_000_000, false },
                new object[]{ 9_000_000_000_000_000_000 + 1000, 9_000_000_000_000_000_000 + 1, false }
            };
        }

        [Theory]
        [MemberData(nameof(CreateTestSerializeInt16BigEndianData))]
        public void TestSerializeInt16BigEndian(short v, byte[] expected)
        {
            byte[] actual = ProtocolDatagram.SerializeInt16BigEndian(v);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestSerializeInt16BigEndianData()
        {
            return new List<object[]>
            {
                new object[]{ 0, new byte[] { 0, 0 } },
                new object[]{ 1_000, new byte[] { 3, 232 } },
                new object[]{ 10_000, new byte[] { 39, 16 } },
                new object[]{ 30_000, new byte[] { 117, 48 } },
                new object[]{ -30_000, new byte[] { 138, 208 } },
            };
        }

        [Theory]
        [MemberData(nameof(CreateTestSerializeUnsignedInt16BigEndianData))]
        public void TestSerializeUnsignedInt16BigEndian(int v, byte[] expected)
        {
            byte[] actual = ProtocolDatagram.SerializeUnsignedInt16BigEndian(v);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestSerializeUnsignedInt16BigEndianData()
        {
            return new List<object[]>
            {
                new object[]{ 0, new byte[] { 0, 0 } },
                new object[]{ 1_000, new byte[] { 3, 232 } },
                new object[]{ 10_000, new byte[] { 39, 16 } },
                new object[]{ 30_000, new byte[] { 117, 48 } },
                new object[]{ 35536, new byte[] { 138, 208 } },
                new object[]{ 65535, new byte[] { 0xff, 0xff } },
            };
        }

        [Theory]
        [MemberData(nameof(CreateTestSerializeInt32BigEndianData))]
        public void TestSerializeInt32BigEndian(int v, byte[] expected)
        {
            byte[] actual = ProtocolDatagram.SerializeInt32BigEndian(v);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestSerializeInt32BigEndianData()
        {
            return new List<object[]>
            {
                new object[]{ 0, new byte[] { 0, 0, 0, 0 } },
                new object[]{ 1_000, new byte[] { 0, 0, 3, 232 } },
                new object[]{ 10_000, new byte[] { 0, 0, 39, 16 } },
                new object[]{ 30_000, new byte[] { 0, 0, 117, 48 } },
                new object[]{ -30_000, new byte[] { 255, 255, 138, 208 } },
                new object[]{ 1_000_000, new byte[] { 0, 15, 66, 64 } },
                new object[]{ 1_000_000_000, new byte[] { 59, 154, 202, 0 } },
                new object[]{ 2_000_000_100, new byte[] { 119, 53, 148, 100 } },
                new object[]{ -2_000_000_100, new byte[] { 136, 202, 107, 156 } },
            };
        }

        [Theory]
        [MemberData(nameof(CreateTestSerializeInt64BigEndianData))]
        public void TestSerializeInt64BigEndian(long v, byte[] expected)
        {
            byte[] actual = ProtocolDatagram.SerializeInt64BigEndian(v);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestSerializeInt64BigEndianData()
        {
            return new List<object[]>
            {
                new object[]{ 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 } },
                new object[]{ 1_000, new byte[] { 0, 0, 0, 0, 0, 0, 3, 232 } },
                new object[]{ 10_000, new byte[] { 0, 0, 0, 0, 0, 0, 39, 16 } },
                new object[]{ 30_000, new byte[] { 0, 0, 0, 0, 0, 0, 117, 48 } },
                new object[]{ -30_000, new byte[] { 255, 255, 255, 255, 255, 255, 138, 208 } },
                new object[]{ 1_000_000, new byte[] { 0, 0, 0, 0, 0, 15, 66, 64 } },
                new object[]{ 1_000_000_000, new byte[] { 0, 0, 0, 0, 59, 154, 202, 0 } },
                new object[]{ 2_000_000_100, new byte[] { 0, 0, 0, 0, 119, 53, 148, 100 } },
                new object[]{ -2_000_000_100, new byte[] { 255, 255, 255, 255, 136, 202, 107, 156 } },
                new object[]{ 1_000_000_000_000L, new byte[] { 0, 0, 0, 232, 212, 165, 16, 0 } },
                new object[]{ 1_000_000_000_000_000L, new byte[] { 0, 3, 141, 126, 164, 198, 128, 0 } },
                new object[]{ 1_000_000_000_000_000_000L, new byte[] { 13, 224, 182, 179, 167, 100, 0, 0 } },
                new object[]{ 2_000_000_000_000_000_000L, new byte[] { 27, 193, 109, 103, 78, 200, 0, 0 } },
                new object[]{ 4_000_000_000_000_000_000L, new byte[] { 55, 130, 218, 206, 157, 144, 0, 0 } },
                new object[]{ 9_000_000_000_000_000_000L, new byte[] { 124, 230, 108, 80, 226, 132, 0, 0 } },
                new object[]{ 9_199_999_999_999_999_999L, new byte[] { 127, 172, 247, 65, 157, 151, 255, 255 } },
                new object[]{ -9_199_999_999_999_999_999L, new byte[] { 128, 83, 8, 190, 98, 104, 0, 1 } },
            };
        }

        [Theory]
        [MemberData(nameof(CreateTestDeserializeInt16BigEndianData))]
        public void TestDeserializeInt16BigEndian(byte[] data, int offset, short expected)
        {
            short actual = ProtocolDatagram.DeserializeInt16BigEndian(data, offset);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestDeserializeInt16BigEndianData()
        {
            return new List<object[]>
            {
                new object[]{ new byte[] { 0, 0 }, 0, 0 },
                new object[]{ new byte[] { 3, 232 }, 0, 1000 },
                new object[]{ new byte[] { 39, 16, 1 }, 0, 10_000 },
                new object[]{ new byte[] { 0, 117, 48, 1 }, 1, 30_000 },
                new object[]{ new byte[] { 0, 138, 208 }, 1, -30_000 },
            };
        }

        [Theory]
        [MemberData(nameof(CreateTestDeserializeUnsignedInt16BigEndianData))]
        public void TestDeserializeUnsignedInt16BigEndian(byte[] data, int offset, int expected)
        {
            int actual = ProtocolDatagram.DeserializeUnsignedInt16BigEndian(data, offset);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestDeserializeUnsignedInt16BigEndianData()
        {
            return new List<object[]>
            {
                new object[]{ new byte[] { 0, 0 }, 0, 0 },
                new object[]{ new byte[] { 3, 232 }, 0, 1000 },
                new object[]{ new byte[] { 39, 16, 1 }, 0, 10_000 },
                new object[]{ new byte[] { 0, 117, 48, 1 }, 1, 30_000 },
                new object[]{ new byte[] { 0, 138, 208 }, 1, 35_536 },
                new object[]{ new byte[] { 0xff, 0xff }, 0, 65535 }
            };
        }

        [Theory]
        [MemberData(nameof(CreateTestDeserializeInt32BigEndianData))]
        public void TestDeserializeInt32BigEndian(byte[] data, int offset, int expected)
        {
            int actual = ProtocolDatagram.DeserializeInt32BigEndian(data, offset);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestDeserializeInt32BigEndianData()
        {
            return new List<object[]>
            {
                new object[]{ new byte[] { 0, 0, 0, 0 }, 0, 0 },
                new object[]{ new byte[] { 0, 0, 3, 232 }, 0, 1_000 },
                new object[]{ new byte[] { 0, 0, 39, 16 }, 0, 10_000 },
                new object[]{ new byte[] { 0, 0, 117, 48 }, 0, 30_000 },
                new object[]{ new byte[] { 255, 255, 138, 208 }, 0, -30_000 },
                new object[]{ new byte[] { 0, 15, 66, 64 }, 0, 1_000_000 },
                new object[]{ new byte[] { 59, 154, 202, 0, 1 }, 0, 1_000_000_000 },
                new object[]{ new byte[] { 0, 119, 53, 148, 100 }, 1, 2_000_000_100 },
                new object[]{ new byte[] { 0, 136, 202, 107, 156, 1 }, 1, -2_000_000_100 },
            };
        }

        [Theory]
        [MemberData(nameof(CreateTestDeserializeInt64BigEndianData))]
        public void TestDeserializeInt64BigEndian(byte[] data, int offset, long expected)
        {
            long actual = ProtocolDatagram.DeserializeInt64BigEndian(data, offset);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestDeserializeInt64BigEndianData()
        {
            return new List<object[]>
            {
                new object[]{ new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }, 0, 0 },
                new object[]{ new byte[] { 0, 0, 0, 0, 0, 0, 3, 232 }, 0, 1_000 },
                new object[]{ new byte[] { 0, 0, 0, 0, 0, 0, 39, 16 }, 0, 10_000 },
                new object[]{ new byte[] { 0, 0, 0, 0, 0, 0, 117, 48 }, 0, 30_000 },
                new object[]{ new byte[] { 255, 255, 255, 255, 255, 255, 138, 208 }, 0, -30_000 },
                new object[]{ new byte[] { 0, 0, 0, 0, 0, 15, 66, 64 }, 0, 1_000_000 },
                new object[]{ new byte[] { 0, 0, 0, 0, 59, 154, 202, 0 }, 0, 1_000_000_000 },
                new object[]{ new byte[] { 0, 0, 0, 0, 119, 53, 148, 100 }, 0, 2_000_000_100 },
                new object[]{ new byte[] { 255, 255, 255, 255, 136, 202, 107, 156 }, 0, -2_000_000_100 },
                new object[]{ new byte[] { 0, 0, 0, 232, 212, 165, 16, 0 }, 0, 1_000_000_000_000L },
                new object[]{ new byte[] { 0, 3, 141, 126, 164, 198, 128, 0 }, 0, 1_000_000_000_000_000L },
                new object[]{ new byte[] { 13, 224, 182, 179, 167, 100, 0, 0 }, 0, 1_000_000_000_000_000_000L },
                new object[]{ new byte[] { 27, 193, 109, 103, 78, 200, 0, 0 }, 0, 2_000_000_000_000_000_000L },
                new object[]{ new byte[] { 55, 130, 218, 206, 157, 144, 0, 0 }, 0, 4_000_000_000_000_000_000L },
                new object[]{ new byte[] { 124, 230, 108, 80, 226, 132, 0, 0, 1 }, 0, 9_000_000_000_000_000_000L },
                new object[]{ new byte[] { 0, 127, 172, 247, 65, 157, 151, 255, 255 }, 1, 9_199_999_999_999_999_999L },
                new object[]{ new byte[] { 0, 128, 83, 8, 190, 98, 104, 0, 1, 0 }, 1, -9_199_999_999_999_999_999L },
            };
        }

        [Fact]
        public void TestGenerateSessionId()
        {
            // check that conversion to hex succeeds, and that number of bytes produced = 16 or 32.
            var randSid = ProtocolDatagram.GenerateSessionId(false);
            var randSidBytes = ProtocolDatagram.ConvertHexToBytes(randSid);
            Assert.Equal(16, randSidBytes.Length);
            randSid = ProtocolDatagram.GenerateSessionId(true);
            randSidBytes = ProtocolDatagram.ConvertHexToBytes(randSid);
            Assert.Equal(32, randSidBytes.Length);
            randSid = ProtocolDatagram.GenerateSessionId();
            randSidBytes = ProtocolDatagram.ConvertHexToBytes(randSid);
            Assert.Equal(32, randSidBytes.Length);
        }

        [Theory]
        [MemberData(nameof(CreateTestToRawDatagramData))]
        public void TestToRawDatagram(ProtocolDatagram instance, byte[] expected)
        {
            var actual = instance.ToRawDatagram();
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestToRawDatagramData()
        {
            var testData = new List<object[]>();

            var sessionId = "".PadLeft(32, '0');
            var instance = new ProtocolDatagram
            {
                DataLength = 200,
                SessionId = sessionId
            };
            var expected = new byte[]
            { 
                0x00, 0xe6,  // expected length
                0x00, // indicates short session id
                0x00, 0x00, 0x00, 0x00, // session id 
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, // indicates short window id.
                0x00, 0x00, 0x00, 0x00, // window id
                0x00, 0x00, 0x00, 0x00, // sequence number
                0x00, // op code.
                0x00, // null terminator for all options.

            };
            testData.Add(new object[] { instance, expected });

            sessionId = "".PadLeft(64, '0');
            instance = new ProtocolDatagram
            {
                ExpectedDatagramLength = 50,
                SessionId = sessionId,
                WindowId = int.MaxValue + 1L
            };
            expected = new byte[]
            {
                0x00, 0x32,  // expected length
                0xff, // indicates long session id
                0x00, 0x00, 0x00, 0x00, // session id 
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0xff, // indicates long window id.
                0x00, 0x00, 0x00, 0x00, // window id
                0x80, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // sequence number
                0x00, // op code.
                0x00, // null terminator for all options.

            };
            testData.Add(new object[] { instance, expected });

            sessionId = "".PadLeft(64, '1');
            instance = new ProtocolDatagram
            {
                SessionId = sessionId,
                WindowId = 3_290_342_720_000_601_258,
                OpCode = ProtocolDatagram.OpCodeAck,
                SequenceNumber = 1_895_425_975,
                Options = new ProtocolDatagramOptions
                {
                    IdleTimeoutSecs = 20
                }
            };
            expected = new byte[]
            {
                0x00, 0x45,  // expected length
                0xff, // indicates long session id
                0x11, 0x11, 0x11, 0x11, // session id 
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0xff, // indicates long window id.
                0x2d, 0xa9, 0xa5, 0x45, // window id
                0x56, 0xed, 0xac, 0xaa,
                0x70, 0xf9, 0xe7, 0xb7, // sequence number
                0x10, // op code.
                (byte)'s', (byte)'_', (byte)'i', (byte)'d',
                (byte)'l', (byte)'e', (byte)'_', (byte)'t',
                (byte)'i', (byte)'m', (byte)'e', (byte)'o',
                (byte)'u', (byte)'t', 0x00,
                0x00, 0x02, 
                (byte)'2', (byte)'0',
                0x00, // null terminator for all options.

            };
            testData.Add(new object[] { instance, expected });

            sessionId = "".PadLeft(32, '2');
            instance = new ProtocolDatagram
            {
                ExpectedDatagramLength = 59,
                SessionId = sessionId,
                WindowId = 720_000_601,
                OpCode = ProtocolDatagram.OpCodeClose,
                SequenceNumber = 1_000,
                Options = new ProtocolDatagramOptions
                {
                    AbortCode = 5,
                    IsLastInWindow = true,
                },
                DataBytes = new byte[] { (byte)'h', (byte)'e', (byte) 'y' },
                DataOffset = 1,
                DataLength = 2
            };
            expected = new byte[]
            {
                0x00, 0x3b,  // expected length
                0x00, // indicates short session id
                0x22, 0x22, 0x22, 0x22, // session id 
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x00, // indicates short window id.
                0x2a, 0xea, 0x56, 0x59, // window id
                0x00, 0x00, 0x03, 0xe8, // sequence number
                0x7e, // op code.
                (byte)'s', (byte)'_', (byte)'a', (byte)'b',
                (byte)'o', (byte)'r', (byte)'t', (byte)'_',
                (byte)'c', (byte)'o', (byte)'d', (byte)'e',
                0x00,
                0x00, 0x01,
                (byte)'5',
                (byte)'s', (byte)'_', (byte)'0', (byte)'1',
                0x00,
                0x00, 0x04,
                (byte)'T', (byte)'r', (byte)'u', (byte)'e',
                0x00, // null terminator for all options.
                (byte)'e', (byte) 'y' // data
            };
            testData.Add(new object[] { instance, expected });

            sessionId = "".PadLeft(32, '3');
            instance = new ProtocolDatagram
            {
                SessionId = sessionId,
                WindowId = 1,
                OpCode = ProtocolDatagram.OpCodeData,
                SequenceNumber = 1,
                Options = new ProtocolDatagramOptions
                {
                    IsLastInWindowGroup = false,
                },
                DataBytes = new byte[] { (byte)'h', (byte)'e', (byte)'y' },
                DataOffset = 0,
                DataLength = 3
            };
            expected = new byte[]
            {
                0x00, 0x2d,  // expected length
                0x00, // indicates short session id
                0x33, 0x33, 0x33, 0x33, // session id 
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x00, // indicates short window id.
                0x00, 0x00, 0x00, 0x01, // window id
                0x00, 0x00, 0x00, 0x01, // sequence number
                0x00, // op code.
                (byte)'s', (byte)'_', (byte)'0', (byte)'2',
                0x00,
                0x00, 0x05,
                (byte)'F', (byte)'a', (byte)'l', (byte)'s', 
                (byte)'e',
                0x00, // null terminator for all options.
                (byte)'h', (byte)'e', (byte) 'y' // data
            };
            testData.Add(new object[] { instance, expected });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateTestToRawDatagramWithErrorData))]
        public void TestToRawDatagramWithError(ProtocolDatagram instance)
        {
            Assert.ThrowsAny<Exception>(() => instance.ToRawDatagram());
        }

        public static List<object[]> CreateTestToRawDatagramWithErrorData()
        {
            var testData = new List<object[]>();

            // data length too large.
            var sessionId = "".PadLeft(32, '0');
            var instance = new ProtocolDatagram
            {
                DataLength = 200_000,
                SessionId = sessionId
            };
            testData.Add(new object[] { instance });

            // expected datagram length not the same as actual, and data bytes is given.
            sessionId = "".PadLeft(64, '0');
            instance = new ProtocolDatagram
            {
                ExpectedDatagramLength = 1,
                SessionId = sessionId,
                WindowId = int.MaxValue + 1L,
                DataBytes = new byte[0]
            };
            testData.Add(new object[] { instance });

            // expected datagram length not the same as actual, and data bytes is not given.
            sessionId = "".PadLeft(64, '1');
            instance = new ProtocolDatagram
            {
                ExpectedDatagramLength = 1,
                SessionId = sessionId,
                WindowId = 3_290_342_720_000_601_258,
                OpCode = ProtocolDatagram.OpCodeAck,
                SequenceNumber = 1_895_425_975,
                Options = new ProtocolDatagramOptions
                {
                    IdleTimeoutSecs = 20
                }
            };
            testData.Add(new object[] { instance });

            // almost ok, except that eventual datagram is too large.
            sessionId = "".PadLeft(32, '2');
            instance = new ProtocolDatagram
            {
                ExpectedDatagramLength = 65_501,
                SessionId = sessionId,
                WindowId = 720_000_601,
                OpCode = ProtocolDatagram.OpCodeClose,
                SequenceNumber = 1_000,
                Options = new ProtocolDatagramOptions
                {
                    AbortCode = 5,
                    IsLastInWindow = true,
                },
                DataLength = 65_444
            };
            testData.Add(new object[] { instance });

            // invalid session id.
            sessionId = "".PadLeft(30, '3');
            instance = new ProtocolDatagram
            {
                SessionId = sessionId
            };
            testData.Add(new object[] { instance });

            instance = new ProtocolDatagram
            {
                SessionId = null
            };
            testData.Add(new object[] { instance });

            // invalid data length
            sessionId = "".PadLeft(32, '4');
            instance = new ProtocolDatagram
            {
                SessionId = sessionId,
                DataLength = -1
            };
            testData.Add(new object[] { instance });

            // invalid offset in data bytes.
            sessionId = "".PadLeft(32, '5');
            instance = new ProtocolDatagram
            {
                SessionId = sessionId,
                DataOffset = -1,
                DataBytes = new byte[0]
            };
            testData.Add(new object[] { instance });
            instance = new ProtocolDatagram
            {
                SessionId = sessionId,
                DataOffset = 10,
                DataBytes = new byte[2]
            };
            testData.Add(new object[] { instance });

            // invalid combination of data length and offset in data bytes
            sessionId = "".PadLeft(32, '6');
            instance = new ProtocolDatagram
            {
                SessionId = sessionId,
                DataLength = 1,
                DataBytes = new byte[0]
            };
            testData.Add(new object[] { instance });

            instance = new ProtocolDatagram
            {
                SessionId = sessionId,
                DataLength = 2,
                DataOffset = 2,
                DataBytes = new byte[3]
            };
            testData.Add(new object[] { instance });

            return testData;
        }
    }
}
