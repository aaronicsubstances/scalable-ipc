using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ScalableIPC.Core.UnitTests
{
    public class ByteUtilsTest
    {
        [Theory]
        [MemberData(nameof(CreateConvertBytesToHexData))]
        public void TestConvertBytesToHex(byte[] data, int offset, int length, string expected)
        {
            string actual = ByteUtils.ConvertBytesToHex(data, offset, length);
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
            byte[] actual = ByteUtils.ConvertHexToBytes(hex);
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
        [MemberData(nameof(CreateTestSerializeInt16BigEndianData))]
        public void TestSerializeInt16BigEndian(short v, byte[] expected)
        {
            byte[] actual = ByteUtils.SerializeInt16BigEndian(v);
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
            byte[] actual = ByteUtils.SerializeUnsignedInt16BigEndian(v);
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
            byte[] actual = ByteUtils.SerializeInt32BigEndian(v);
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
            byte[] actual = ByteUtils.SerializeInt64BigEndian(v);
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
            short actual = ByteUtils.DeserializeInt16BigEndian(data, offset);
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
            int actual = ByteUtils.DeserializeUnsignedInt16BigEndian(data, offset);
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
            int actual = ByteUtils.DeserializeInt32BigEndian(data, offset);
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
            long actual = ByteUtils.DeserializeInt64BigEndian(data, offset);
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
        public void TestGenerateUuid()
        {
            // check that conversion to hex succeeds, and that number of bytes produced = 16.
            var randSid = ByteUtils.GenerateUuid();
            var randSidBytes = ByteUtils.ConvertHexToBytes(randSid);
            Assert.Equal(16, randSidBytes.Length);
        }
    }
}
