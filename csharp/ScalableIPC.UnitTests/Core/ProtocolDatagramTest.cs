using ScalableIPC.Core;
using ScalableIPC.UnitTests.Helpers;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ScalableIPC.UnitTests.Core
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
                new object[]{ -1, 0 },
                new object[]{ -10, 0 },
                new object[]{ 1, 2 },
                new object[]{ 99, 100 },
                new object[]{ 100, 101 },
                new object[]{ 101, 102 },
                new object[]{ int.MaxValue - 1, int.MaxValue },
                new object[]{ int.MaxValue, int.MaxValue + 1L },
                new object[]{ int.MaxValue + 1L, int.MaxValue + 2L },
                new object[]{ 9_000_000_000_000_000 - 2, 9_000_000_000_000_000 - 1 },
                new object[]{ 9_000_000_000_000_000 - 1, 9_000_000_000_000_000 },
                new object[]{ 9_000_000_000_000_000, 0 },
                new object[]{ 9_000_000_000_000_000 + 1, 0 },
                new object[]{ long.MaxValue, 0 },
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
                new object[]{ 99, 100, false },
                new object[]{ 100, 101, false },
                new object[]{ 101, 102, false },
                new object[]{ int.MaxValue - 1, int.MaxValue, false },
                new object[]{ int.MaxValue, int.MaxValue + 1L, false },
                new object[]{ int.MaxValue + 1L, int.MaxValue + 2L, false },
                new object[]{ 9_000_000_000_000_000 - 2, 9_000_000_000_000_000 - 1, false },
                new object[]{ 9_000_000_000_000_000 - 1, 9_000_000_000_000_000, false },
                new object[]{ 9_000_000_000_000_000, 0, false },
                new object[]{ 9_000_000_000_000_000 + 1, 0, false },
                new object[]{ long.MaxValue, 0, false },
                new object[]{ 0, 0, false },
                new object[]{ 60, 60, false },
                new object[]{ 0, -1, true },
                new object[]{ 60, -1, true },
                new object[]{ 0, -2, true },
                new object[]{ 1_000, -1, true },
                new object[]{ 9_000_000_000_000_000_000, -1, false },
                new object[]{ 61, 60, true },
                new object[]{ 99, 0, true },
                new object[]{ 100, 0, true },
                new object[]{ 101, 0, false },
                new object[]{ 100, 1, true },
                new object[]{ 101, 1, true },
                new object[]{ 102, 1, false },
                new object[]{ 9_000_000_000_000_000 + 2, 9_000_000_000_000_000 - 2, true },
                new object[]{ 9_000_000_000_000_000 + 99, 9_000_000_000_000_000 - 1, true },
                new object[]{ 9_000_000_000_000_000 + 100, 9_000_000_000_000_000 - 1, false },
                new object[]{ 9_000_000_000_000_000 + 1, 9_000_000_000_000_000, false },
                new object[]{ 9_000_000_000_000_000 + 100, 9_000_000_000_000_000 + 1, false }
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
            // check that conversion to hex succeeds, and that number of bytes produced = 32.
            var randSid = ProtocolDatagram.GenerateSessionId();
            var randSidBytes = ProtocolDatagram.ConvertHexToBytes(randSid);
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

            var sessionId = "".PadLeft(64, '0');
            var instance = new ProtocolDatagram
            {
                DataLength = 200,
                SessionId = sessionId
            };
            var expected = new byte[]
            { 
                0x01, 0x01,  // expected length
                0x00, 0x00, 0x00, 0x00, // session id 
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // window id
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // sequence number
                0x00, // op code.
                0x00, 0x00, // null terminator for all options.
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
            };
            testData.Add(new object[] { instance, expected });

            sessionId = "".PadLeft(64, '0');
            instance = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeEnquireLink,
                ExpectedDatagramLength = 57,
                SessionId = sessionId,
                WindowId = int.MaxValue + 1L
            };
            expected = new byte[]
            {
                0x00, 0x39,  // expected length
                0x00, 0x00, 0x00, 0x00, // session id 
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // window id
                0x80, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // sequence number
                0x05, // op code.
                0x00, 0x00, // null terminator for all options.
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
            };
            testData.Add(new object[] { instance, expected });

            sessionId = "".PadLeft(64, '1');
            instance = new ProtocolDatagram
            {
                SessionId = sessionId,
                WindowId = 3_290_342_720_000_601_258,
                OpCode = ProtocolDatagram.OpCodeData,
                SequenceNumber = 1_895_425_975,
                Options = new ProtocolDatagramOptions
                {
                    IdleTimeout = 20
                }
            };
            expected = new byte[]
            {
                0x00, 0x4e,  // expected length
                0x11, 0x11, 0x11, 0x11, // session id 
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x2d, 0xa9, 0xa5, 0x45, // window id
                0x56, 0xed, 0xac, 0xaa,
                0x70, 0xf9, 0xe7, 0xb7, // sequence number
                0x01, // op code.
                0x00, 0x11,
                (byte)'s', (byte)'_', (byte)'i', (byte)'d',
                (byte)'l', (byte)'e', (byte)'_', (byte)'t',
                (byte)'i', (byte)'m', (byte)'e', (byte)'o',
                (byte)'u', (byte)'t', 0x00,
                0x00, 0x11,
                (byte)'2', (byte)'0',
                0x00, 0x00, // null terminator for all options.
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
            };
            testData.Add(new object[] { instance, expected });

            sessionId = "".PadLeft(64, '2');
            instance = new ProtocolDatagram
            {
                ExpectedDatagramLength = 90,
                SessionId = sessionId,
                WindowId = 720_000_601,
                OpCode = ProtocolDatagram.OpCodeDataAck,
                SequenceNumber = 1_000,
                Options = new ProtocolDatagramOptions
                {
                    ErrorCode = 5,
                    IsLastInWindow = true,
                },
                DataBytes = new byte[] { (byte)'h', (byte)'e', (byte) 'y' },
                DataOffset = 1,
                DataLength = 2
            };
            expected = new byte[]
            {
                0x00, 0x5a,  // expected length
                0x22, 0x22, 0x22, 0x22, // session id 
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x00, 0x00, 0x00, 0x00, // window id
                0x2a, 0xea, 0x56, 0x59,
                0x00, 0x00, 0x03, 0xe8, // sequence number
                0x02, // op code.
                0x00, 0x0e,
                (byte)'s', (byte)'_', (byte)'e', (byte)'r',
                (byte)'r', (byte)'o', (byte)'r', (byte)'_',
                (byte)'c', (byte)'o', (byte)'d', (byte)'e',
                0x00,
                0x00, 0x0e,
                (byte)'5',
                0x00, 0x09,
                (byte)'s', (byte)'_', (byte)'0', (byte)'1',
                0x00,
                0x00, 0x09,
                (byte)'T', (byte)'r', (byte)'u', (byte)'e',
                0x00, 0x00, // null terminator for all options.
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                (byte)'e', (byte)'y' // data
            };
            testData.Add(new object[] { instance, expected });

            sessionId = "".PadLeft(64, '3');
            instance = new ProtocolDatagram
            {
                SessionId = sessionId,
                WindowId = 1,
                OpCode = ProtocolDatagram.OpCodeClose,
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
                0x00, 0x4a,  // expected length
                0x33, 0x33, 0x33, 0x33, // session id 
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x00, 0x00, 0x00, 0x00, // window id
                0x00, 0x00, 0x00, 0x01,
                0x00, 0x00, 0x00, 0x01, // sequence number
                0x03, // op code.
                0x00, 0xa,
                (byte)'s', (byte)'_', (byte)'0', (byte)'2',
                0x00,
                0x00, 0xa,
                (byte)'F', (byte)'a', (byte)'l', (byte)'s', 
                (byte)'e',
                0x00, 0x00, // null terminator for all options.
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                (byte)'h', (byte)'e', (byte) 'y' // data
            };
            testData.Add(new object[] { instance, expected });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateTestToRawDatagramWithErrorData))]
        public void TestToRawDatagramWithError(ProtocolDatagram instance, string expected)
        {
            var ex = Assert.ThrowsAny<Exception>(() => instance.ToRawDatagram());
            if (expected != null)
            {
                Assert.Contains(expected, ex.Message);
            }
        }

        public static List<object[]> CreateTestToRawDatagramWithErrorData()
        {
            var testData = new List<object[]>();

            // data length too large.
            var sessionId = "".PadLeft(64, '0');
            var instance = new ProtocolDatagram
            {
                DataLength = 200_000,
                SessionId = sessionId
            };
            testData.Add(new object[] { instance, "f414e24d-d8bb-44dc-afb4-d34773d28e9a" });

            // expected datagram length not the same as actual, and data bytes is given.
            sessionId = "".PadLeft(64, '0');
            instance = new ProtocolDatagram
            {
                ExpectedDatagramLength = 1,
                SessionId = sessionId,
                WindowId = int.MaxValue + 1L,
                DataBytes = new byte[0]
            };
            testData.Add(new object[] { instance, "1e417e23-251c-458e-b349-ebfa9d01bde1" });

            // expected datagram length not the same as actual, and data bytes is not given.
            sessionId = "".PadLeft(64, '1');
            instance = new ProtocolDatagram
            {
                ExpectedDatagramLength = 1,
                SessionId = sessionId,
                WindowId = 3_290_342_720_000_601_258,
                OpCode = ProtocolDatagram.OpCodeDataAck,
                SequenceNumber = 1_895_425_975,
                Options = new ProtocolDatagramOptions
                {
                    IdleTimeout = 20
                }
            };
            testData.Add(new object[] { instance, "45a36c96-e6d4-493f-b271-f5614886a504" });

            // almost ok, except that eventual datagram is too large.
            sessionId = "".PadLeft(64, '2');
            instance = new ProtocolDatagram
            {
                ExpectedDatagramLength = 65_501,
                SessionId = sessionId,
                WindowId = 720_000_601,
                OpCode = ProtocolDatagram.OpCodeClose,
                SequenceNumber = 1_000,
                Options = new ProtocolDatagramOptions
                {
                    ErrorCode = 5,
                    IsLastInWindow = true,
                },
                DataLength = 65_413
            };
            testData.Add(new object[] { instance, "c7d368ce-6f80-42f5-bb78-49dd91d0d082" });

            // invalid session id.
            sessionId = "".PadLeft(30, '3');
            instance = new ProtocolDatagram
            {
                SessionId = sessionId
            };
            testData.Add(new object[] { instance, "23c2b87c-158b-4586-931e-9f2383bfd2ed" });

            instance = new ProtocolDatagram
            {
                SessionId = null
            };
            testData.Add(new object[] { instance, "772396c0-8b84-475d-a31e-06e7a6904ccc" });

            // invalid data length
            sessionId = "".PadLeft(64, '4');
            instance = new ProtocolDatagram
            {
                SessionId = sessionId,
                DataLength = -1
            };
            testData.Add(new object[] { instance, "9039a1e3-c4a1-4eff-b53f-059a7316b97d" });

            // invalid offset in data bytes.
            sessionId = "".PadLeft(64, '5');
            instance = new ProtocolDatagram
            {
                SessionId = sessionId,
                DataOffset = -1,
                DataBytes = new byte[0]
            };
            testData.Add(new object[] { instance, "149f8bf9-0226-40e3-a6ca-2d00541a4d75" });

            // invalid combination of data length and offset in data bytes
            instance = new ProtocolDatagram
            {
                SessionId = sessionId,
                DataOffset = 10,
                DataBytes = new byte[2]
            };
            testData.Add(new object[] { instance, "786322b1-f408-4b9a-a41d-d95acecda445" });

            sessionId = "".PadLeft(64, '6');
            instance = new ProtocolDatagram
            {
                SessionId = sessionId,
                DataLength = 1,
                DataBytes = new byte[0]
            };
            testData.Add(new object[] { instance, "786322b1-f408-4b9a-a41d-d95acecda445" });

            instance = new ProtocolDatagram
            {
                SessionId = sessionId,
                DataLength = 2,
                DataOffset = 2,
                DataBytes = new byte[3]
            };
            testData.Add(new object[] { instance, "786322b1-f408-4b9a-a41d-d95acecda445" });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateTestParseData))]
        public void TestParse(byte[] data, int offset, int length, ProtocolDatagram expected)
        {
            var actual = ProtocolDatagram.Parse(data, offset, length);
            Assert.Equal(expected, actual, ProtocolDatagramComparer.Default);
        }

        public static List<object[]> CreateTestParseData()
        {
            var testData = new List<object[]>();

            var input = new byte[]
            {
                0x00, 0x39,  // expected length
                0x00, 0x00, 0x00, 0x00, // session id 
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // window id
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // sequence number
                0x00, // op code.
                0x00, 0x00, // null terminator for all options.
                0x00, 0x00, 0x00, 0x00, // session id prefix
                0x00, 0x00, 0x00, 0x00,
            };
            var sessionId = "".PadLeft(64, '0');
            var expected = new ProtocolDatagram
            {
                ExpectedDatagramLength = 57,
                SessionId = sessionId,
                DataBytes = input,
                DataOffset = 57
            };
            testData.Add(new object[] { input, 0, input.Length, expected });

            input = new byte[]
            {
                0x00, 0x39,  // expected length
                0x00, 0x00, 0x00, 0x00, // session id 
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // window id
                0x80, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // sequence number
                0x00, // op code.
                0x00, 0x00, // null terminator for all options.
                0x00, 0x00, 0x00, 0x00, // session id prefix
                0x00, 0x00, 0x00, 0x00,
            };
            sessionId = "".PadLeft(64, '0');
            expected = new ProtocolDatagram
            {
                ExpectedDatagramLength = 57,
                SessionId = sessionId,
                WindowId = int.MaxValue + 1L,
                DataBytes = input,
                DataOffset = 57
            };
            testData.Add(new object[] { input, 0, input.Length, expected });

            input = new byte[]
            {
                0x00, 0x4e,  // expected length
                0x11, 0x11, 0x11, 0x11, // session id 
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x2d, 0xa9, 0xa5, 0x45, // window id
                0x56, 0xed, 0xac, 0xaa,
                0x70, 0xf9, 0xe7, 0xb7, // sequence number
                0x04, // op code.
                0x00, 0x11,
                (byte)'s', (byte)'_', (byte)'i', (byte)'d',
                (byte)'l', (byte)'e', (byte)'_', (byte)'t',
                (byte)'i', (byte)'m', (byte)'e', (byte)'o',
                (byte)'u', (byte)'t', 0x00,
                0x00, 0x11,
                (byte)'2', (byte)'0',
                0x00, 0x00, // null terminator for all options.
                0x11, 0x11, 0x11, 0x11, // session id prefix
                0x11, 0x11, 0x11, 0x11,
            };
            sessionId = "".PadLeft(64, '1');
            expected = new ProtocolDatagram
            {
                ExpectedDatagramLength = 78,
                SessionId = sessionId,
                WindowId = 3_290_342_720_000_601_258,
                OpCode = ProtocolDatagram.OpCodeCloseAck,
                SequenceNumber = 1_895_425_975,
                Options = new ProtocolDatagramOptions
                {
                    IdleTimeout = 20
                },
                DataBytes = input,
                DataOffset = 78
            };
            expected.Options.AllOptions.Add(ProtocolDatagramOptions.OptionNameIdleTimeout,
                new List<string> { "20" });
            testData.Add(new object[] { input, 0, input.Length, expected });

            input = new byte[]
            {
                0xda, // not part
                0x00, 0x5a,  // expected length
                0x22, 0x22, 0x22, 0x22, // session id 
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x00, 0x00, 0x00, 0x00, // window id.
                0x2a, 0xea, 0x56, 0x59,
                0x00, 0x00, 0x03, 0xe8, // sequence number
                0x02, // op code.
                0x00, 0x09,
                (byte)'s', (byte)'_', (byte)'0', (byte)'1',
                0x00,
                0x00, 0x09,
                (byte)'T', (byte)'r', (byte)'u', (byte)'e',
                0x00, 0x0e,
                (byte)'s', (byte)'_', (byte)'e', (byte)'r',
                (byte)'r', (byte)'o', (byte)'r', (byte)'_',
                (byte)'c', (byte)'o', (byte)'d', (byte)'e',
                0x00,
                0x00, 0x0e,
                (byte)'5',
                0x00, 0x00, // null terminator for all options.
                0x22, 0x22, 0x22, 0x22, // session id prefix
                0x22, 0x22, 0x22, 0x22,
                (byte)'e', (byte) 'y' // data
            };
            sessionId = "".PadLeft(64, '2');
            expected = new ProtocolDatagram
            {
                ExpectedDatagramLength = 90,
                SessionId = sessionId,
                WindowId = 720_000_601,
                OpCode = ProtocolDatagram.OpCodeDataAck,
                SequenceNumber = 1_000,
                Options = new ProtocolDatagramOptions
                {
                    ErrorCode = 5,
                    IsLastInWindow = true,
                },
                DataBytes = input,
                DataOffset = 89,
                DataLength = 2
            };
            expected.Options.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsLastInWindow,
                new List<string> { "True" });
            expected.Options.AllOptions.Add(ProtocolDatagramOptions.OptionNameErrorCode,
                new List<string> { "5" });
            testData.Add(new object[] { input, 1, input.Length - 1, expected });

            input = new byte[]
            {
                0x00, 0x4a,  // expected length
                0x33, 0x33, 0x33, 0x33, // session id 
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x00, 0x00, 0x00, 0x00, // window id
                0x00, 0x00, 0x00, 0x01,
                0x00, 0x00, 0x00, 0x01, // sequence number
                0x03, // op code.
                0x00, 0x0a,
                (byte)'s', (byte)'_', (byte)'0', (byte)'2',
                0x00,
                0x00, 0x0a,
                (byte)'f', (byte)'a', (byte)'l', (byte)'s',
                (byte)'e',
                0x00, 0x00, // null terminator for all options.
                0x33, 0x33, 0x33, 0x33, // session id prefix
                0x33, 0x33, 0x33, 0x33,
                (byte)'h', (byte)'e', (byte) 'y', // data
                0xcf, // not part
            };
            sessionId = "".PadLeft(64, '3');
            expected = new ProtocolDatagram
            {
                ExpectedDatagramLength = 74,
                SessionId = sessionId,
                WindowId = 1,
                OpCode = ProtocolDatagram.OpCodeClose,
                SequenceNumber = 1,
                Options = new ProtocolDatagramOptions
                {
                    IsLastInWindowGroup = false,
                },
                DataBytes = input,
                DataOffset = 71,
                DataLength = 3
            };
            expected.Options.AllOptions.Add(ProtocolDatagramOptions.OptionNameIsLastInWindowGroup,
                new List<string> { "false" });
            testData.Add(new object[] { input, 0, input.Length - 1, expected });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateTestParseWithErrorData))]
        public void TestParseWithError(byte[] data, int offset, int length, string expected)
        {
            var ex = Assert.ThrowsAny<Exception>(() => ProtocolDatagram.Parse(data, offset, length));
            if (expected != null)
            {
                Assert.Contains(expected, ex.Message);
            }
        }

        public static List<object[]> CreateTestParseWithErrorData()
        {
            var testData = new List<object[]>();

            // too short a datagram.
            var input = new byte[]
            {
                0x00, 0x1e
            };
            testData.Add(new object[] { input, 0, input.Length, "b451b01f-c474-49e4-ad3f-643d9e849664" });

            // incorrect expected length.
            input = new byte[]
            {
                0x00, 0x01,  // expected length
                0x00, 0x00, 0x00, 0x00, // session id 
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // window id.
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // sequence number
                0x00, // op code.
                0x00, 0x00, // null terminator for all options.
                0x00, 0x00, 0x00, 0x00, // session id prefix
                0x00, 0x00, 0x00, 0x00,
            };
            testData.Add(new object[] { input, 0, input.Length, "f86bd47a-1ece-4439-bb1e-07eed49f97fe" });

            // missing null terminator of all options.
            input = new byte[]
            {
                0x00, 0x3c,  // expected length
                0x00, 0x00, 0x00, 0x00, // session id 
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // window id
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // sequence number
                0x00, // op code.
                0x00, 0x09,
                0x00,
                0x00, 0x09,
                0x01, 0x02, 0x03, 0x04,
                0x05, 0x06, 0x07, 0x08
            };
            testData.Add(new object[] { input, 0, input.Length, "ff905bc7-03da-4054-ac28-a6c133f3e1b7" });

            testData.Add(new object[] { null, 0, input.Length, "6ee7a25c-090e-4321-b429-1ed4b26f3c59" });

            testData.Add(new object[] { input, 0, -1, "02c8ef5c-9e30-4630-a1bc-c9c8dc73cfac" });

            testData.Add(new object[] { input, -1, 1, "58eacdaa-ae6f-4779-a7bd-ec3d28bbadc8" });

            testData.Add(new object[] { input, 100, input.Length, "cf0da519-d5e3-4bb3-b6f6-a9cb0db69fa8" });

            // negative window id.
            input = new byte[]
            {
                0x00, 0x39,  // expected length
                0x00, 0x00, 0x00, 0x00, // session id 
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0xf0, 0x00, 0x00, 0x00, // window id
                0x80, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // sequence number
                0x00, // op code.
                0x00, 0x00, // null terminator for all options.
                0x00, 0x00, 0x00, 0x00, // session id prefix
                0x00, 0x00, 0x00, 0x00,
            };
            testData.Add(new object[] { input, 0, input.Length, "1c13f39a-51f0-4f8d-80f3-5b06f6cfb769" });

            // negative sequence number.
            input = new byte[]
            {
                0x00, 0x39,  // expected length
                0x77, 0x77, 0x77, 0x77, // session id 
                0x77, 0x77, 0x77, 0x77,
                0x77, 0x77, 0x77, 0x77,
                0x77, 0x77, 0x77, 0x77,
                0x77, 0x77, 0x77, 0x77,
                0x77, 0x77, 0x77, 0x77,
                0x77, 0x77, 0x77, 0x77,
                0x77, 0x77, 0x77, 0x77,
                0x00, 0x00, 0x00, 0x00, // window id
                0x80, 0x00, 0x00, 0x00,
                0xf0, 0x00, 0x00, 0x00, // sequence number
                0x00, // op code.
                0x00, 0x00, // null terminator for all options.
                0x77, 0x77, 0x77, 0x77, // session id prefix
                0x77, 0x77, 0x77, 0x77,
            };
            testData.Add(new object[] { input, 0, input.Length, "a1162f0f-197b-4456-ba7f-10cc3ea9ed03" });

            // invalid timeout.
            input = new byte[]
            {
                0x00, 0x4e,  // expected length
                0x11, 0x11, 0x11, 0x11, // session id 
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x2d, 0xa9, 0xa5, 0x45, // window id
                0x56, 0xed, 0xac, 0xaa,
                0x70, 0xf9, 0xe7, 0xb7, // sequence number
                0x03, // op code.
                0x00, 0x11,
                (byte)'s', (byte)'_', (byte)'i', (byte)'d',
                (byte)'l', (byte)'e', (byte)'_', (byte)'t',
                (byte)'i', (byte)'m', (byte)'e', (byte)'o',
                (byte)'u', (byte)'t', 0x00,
                0x00, 0x11,
                (byte)'2', (byte)'T',
                0x00, 0x00, // null terminator for all options.
                0x11, 0x11, 0x11, 0x11, // session id prefix
                0x11, 0x11, 0x11, 0x11,
            };
            testData.Add(new object[] { input, 0, input.Length, "invalid value" });

            // NB: test parsing of options from here onwards

            // missing null terminator after option name
            input = new byte[]
            {
                0xda, // not part
                0x00, 0x3d,  // expected length
                0x22, 0x22, 0x22, 0x22, // session id
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x22, 0x22, 0x22, 0x22,
                0x00, 0x00, 0x00, 0x00, // window id
                0x2a, 0xea, 0x56, 0x59,
                0x00, 0x00, 0x03, 0xe8, // sequence number
                0x7f, // op code.
                0x00, 0x14,
                (byte)'s', (byte)'_', (byte)'0', (byte)'1',
                (byte)'2', (byte)'3', (byte)'4', (byte)'5',
                (byte)'6', (byte)'7', (byte)'8', (byte)'9'
            };
            testData.Add(new object[] { input, 1, input.Length - 1, "079398db-9a5d-462e-be00-2c141d1242f8" });

            // different values for total option length
            input = new byte[]
            {
                0x00, 0x3c,  // expected length
                0x33, 0x33, 0x33, 0x33, // session id 
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x00, 0x00, 0x00, 0x00, // window id
                0x00, 0x00, 0x00, 0x01,
                0x00, 0x00, 0x00, 0x01, // sequence number
                0x00, // op code.
                0x00, 0x0a,
                (byte)'0', (byte)'1', (byte)'2', (byte)'3',
                (byte)'4', (byte)'5', (byte)'6', (byte)'7',
                0x00,
                0x00, 0x02,
                0xcf, // not part
            };
            testData.Add(new object[] { input, 0, input.Length - 1, "37fe94b1-ff15-41a5-bee6-c9e2525f8472" });

            // confirmatory option length missing.
            input = new byte[]
            {
                0xda, // not part
                0x00, 0x3a,  // expected length
                0x33, 0x33, 0x33, 0x33, // session id
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x00, 0x00, 0x00, 0x00, // window id
                0x00, 0x00, 0x00, 0x01,
                0x00, 0x00, 0x00, 0x01, // sequence number
                0x00, // op code.
                0x00, 0x16,
                (byte)'s', (byte)'_', (byte)'0', (byte)'2',
                (byte)'3', (byte)'4', (byte)'5', (byte)'6',
                0x00,
                0xcf, // not part
            };
            testData.Add(new object[] { input, 1, input.Length - 2 , "fedca135-ad4b-4dd3-9b60-f835c4ad6a2b" });

            // option name too long to be correct.
            input = new byte[]
            {
                0x00, 0x3e,  // expected length
                0x33, 0x33, 0x33, 0x33, // session id
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x00, 0x00, 0x00, 0x00, //window id
                0x00, 0x00, 0x00, 0x01,
                0x00, 0x00, 0x00, 0x01, // sequence number
                0x00, // op code.
                0x00, 0x02,
                (byte)'s', (byte)'_', (byte)'0', (byte)'2',
                (byte)'3', (byte)'4', (byte)'5', (byte)'6',
                0x00,
                0x00, 0x02,
                0x00, 0x00
            };
            testData.Add(new object[] { input, 0, input.Length, "d8edf607-3a4a-4c66-88c8-b2251bf33cfd" });

            // option value too short to be correct.
            input = new byte[]
            {
                0x00, 0x3d,  // expected length
                0x33, 0x33, 0x33, 0x33, // session id
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x00, 0x00, 0x00, 0x00, //window id
                0x00, 0x00, 0x00, 0x01,
                0x00, 0x00, 0x00, 0x01, // sequence number
                0x00, // op code.
                0x00, 0x17,
                (byte)'s', (byte)'_', (byte)'0', (byte)'2',
                (byte)'3', (byte)'4', (byte)'5', (byte)'6',
                0x00,
                0x00, 0x17,
                (byte)'0'
            };
            testData.Add(new object[] { input, 0, input.Length, "89074ac6-2b86-4940-8c56-97f632888cb4" });

            // session id prefix not correct.
            input = new byte[]
            {
                0x00, 0x43,  // expected length
                0x33, 0x33, 0x33, 0x33, // session id
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x00, 0x00, 0x00, 0x00, //window id
                0x00, 0x00, 0x00, 0x01,
                0x00, 0x00, 0x00, 0x01, // sequence number
                0x00, // op code.
                0x00, 0x0a,
                (byte)'s', (byte)'_', (byte)'0', (byte)'2',
                (byte)'3', (byte)'4', (byte)'5', (byte)'6',
                0x00,
                0x00, 0x0a,
                (byte)'v',
                0x00, 0x00, // null terminator for all options
                0x33, 0x33, 0x33, 0x00, // session id prefix incomplete
            };
            testData.Add(new object[] { input, 0, input.Length, "45db8f3e-4fb8-40cd-99da-81e726cce5a4" });

            // session id prefix not correct.
            input = new byte[]
            {
                0x00, 0x47,  // expected length
                0x33, 0x33, 0x33, 0x33, // session id
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x33, 0x33, 0x33, 0x33,
                0x00, 0x00, 0x00, 0x00, //window id
                0x00, 0x00, 0x00, 0x01,
                0x00, 0x00, 0x00, 0x01, // sequence number
                0x00, // op code.
                0x00, 0x0a,
                (byte)'s', (byte)'_', (byte)'0', (byte)'2',
                (byte)'3', (byte)'4', (byte)'5', (byte)'6',
                0x00,
                0x00, 0x0a,
                (byte)'v',
                0x00, 0x00, // null terminator for all options
                0x33, 0x33, 0x33, 0x00, // session id prefix
                0x00, 0x00, 0x33, 0x33
            };
            testData.Add(new object[] { input, 0, input.Length, "04d7d81e-2f46-49af-a837-de38505290b4" });

            return testData;
        }
    }
}
