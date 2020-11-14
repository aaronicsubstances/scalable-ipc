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
        [MemberData(nameof(CreateConvertSessionIdBytesToHexData))]
        public void TestConvertSessionIdBytesToHex(byte[] data, int offset, int length, string expected)
        {
            string actual = ProtocolDatagram.ConvertSessionIdBytesToHex(data, offset, length);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateConvertSessionIdBytesToHexData()
        {
            return new List<object[]>
            {
                new object[]{ new byte[] { }, 0, 0, "" },
                new object[]{ new byte[] { 0xFF }, 0, 1, "ff" },
                new object[]{ new byte[] { 0, 0x68, 0x65, 0x6c }, 0, 4,
                    "0068656c" },
                new object[]{ new byte[] { 0, 0x68, 0x65, 0x6c, 0x6c, 0x6f, 0x20, 0x77, 0x6f, 0x72, 0x6c, 0x64, 0 }, 1, 11, 
                    "68656c6c6f20776f726c64" },
            };
        }

        [Theory]
        [MemberData(nameof(CreateConvertSessionIdHexToBytesData))]
        public void TestConvertSessionIdHexToBytes(string hex, byte[] expected)
        {
            byte[] actual = ProtocolDatagram.ConvertSessionIdHexToBytes(hex);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateConvertSessionIdHexToBytesData()
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
        [MemberData(nameof(CreateTestWriteInt16BigEndianData))]
        public void TestWriteInt16BigEndian(short v, byte[] expected)
        {
            byte[] actual = ProtocolDatagram.WriteInt16BigEndian(v);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestWriteInt16BigEndianData()
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
        [MemberData(nameof(CreateTestWriteInt32BigEndianData))]
        public void TestWriteInt32BigEndian(int v, byte[] expected)
        {
            byte[] actual = ProtocolDatagram.WriteInt32BigEndian(v);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestWriteInt32BigEndianData()
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
        [MemberData(nameof(CreateTestWriteInt64BigEndianData))]
        public void TestWriteInt64BigEndian(long v, byte[] expected)
        {
            byte[] actual = ProtocolDatagram.WriteInt64BigEndian(v);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestWriteInt64BigEndianData()
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
        [MemberData(nameof(CreateTestReadInt16BigEndianData))]
        public void TestReadInt16BigEndian(byte[] data, int offset, short expected)
        {
            short actual = ProtocolDatagram.ReadInt16BigEndian(data, offset);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestReadInt16BigEndianData()
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
        [MemberData(nameof(CreateTestReadInt32BigEndianData))]
        public void TestReadInt32BigEndian(byte[] data, int offset, int expected)
        {
            int actual = ProtocolDatagram.ReadInt32BigEndian(data, offset);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestReadInt32BigEndianData()
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
        [MemberData(nameof(CreateTestReadInt64BigEndianData))]
        public void TestReadInt64BigEndian(byte[] data, int offset, long expected)
        {
            long actual = ProtocolDatagram.ReadInt64BigEndian(data, offset);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestReadInt64BigEndianData()
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
    }
}
