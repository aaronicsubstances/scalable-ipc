using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ScalableIPC.Core.UnitTests
{
    public class ProtocolDatagramTest
    {
        [Theory]
        [MemberData(nameof(CreateTestSerializeData))]
        public void TestSerialize(ProtocolDatagram instance, byte[] expected)
        {
            var actual = instance.Serialize();
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestSerializeData()
        {
            var testData = new List<object[]>();

            var msgId = "".PadRight(32, '1');
            var endpointId = "".PadRight(32, '5');
            var instance = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 1625087321,
                Reserved = 0x03,
                MessageId = msgId,
                MessageDestinationId = endpointId,
                SequenceNumber = 0x14,
                Data = new byte[] { 0x68, 0x65, 0x6c, 0x6c, 0x6f }, // hello
                DataOffset = 0,
                DataLength = 5
            };
            var expected = new byte[]
            {
                0x01, 0x10,  // opcode and version
                0x00, 0x00, 0x00, 0x00, // send timestamp
                0x60, 0xDC, 0xDD, 0x59, 
                0x00, 0x00, 0x00, 0x03, // reserved
                0x11, 0x11, 0x11, 0x11, // message id
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x11, 0x11, 0x11, 0x11,
                0x55, 0x55, 0x55, 0x55, // message dest id
                0x55, 0x55, 0x55, 0x55,
                0x55, 0x55, 0x55, 0x55,
                0x55, 0x55, 0x55, 0x55,
                0x00, 0x00, 0x00, 0x14, // sequence number
                0x68, 0x65, 0x6c, 0x6c, // data
                0x6f,
            };
            testData.Add(new object[] { instance, expected });

            msgId = "e4c871c91e364267ac38e1bc87af091a";
            endpointId = "0ee491726ac14d7a92121560946602a1";
            instance = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeDataAck,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 0,
                Reserved = 0,
                MessageId = msgId,
                MessageSourceId = endpointId,
                SequenceNumber = 2,
                ErrorCode = 1
            };
            expected = new byte[]
            {
                0x02, 0x10,  // opcode and version
                0x00, 0x00, 0x00, 0x00, // send timestamp
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // reserved
                0xe4, 0xc8, 0x71, 0xc9, // message id
                0x1e, 0x36, 0x42, 0x67,
                0xac, 0x38, 0xe1, 0xbc,
                0x87, 0xaf, 0x09, 0x1a,
                0x0e, 0xe4, 0x91, 0x72, // message src id
                0x6a, 0xc1, 0x4d, 0x7a,
                0x92, 0x12, 0x15, 0x60,
                0x94, 0x66, 0x02, 0xa1,
                0x00, 0x00, 0x00, 0x02, // sequence number
                0x00, 0x01, // error code
            };
            testData.Add(new object[] { instance, expected });

            msgId = "be1778d1cc2a4f54ada8ec05392fcb86";
            endpointId = "9bd0cc9e6e574079b5509555923df72e";
            instance = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeHeader,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 1_625_119_002,
                Reserved = 0,
                MessageId = msgId,
                MessageDestinationId = endpointId,
                MessageLength = 20367
            };
            expected = new byte[]
            {
                0x03, 0x10,  // opcode and version
                0x00, 0x00, 0x00, 0x00, // send timestamp
                0x60, 0xdd, 0x59, 0x1a,
                0x00, 0x00, 0x00, 0x00, // reserved
                0xbe, 0x17, 0x78, 0xd1, // message id
                0xcc, 0x2a, 0x4f, 0x54,
                0xad, 0xa8, 0xec, 0x05,
                0x39, 0x2f, 0xcb, 0x86,
                0x9b, 0xd0, 0xcc, 0x9e, // message dest id
                0x6e, 0x57, 0x40, 0x79,
                0xb5, 0x50, 0x95, 0x55,
                0x92, 0x3d, 0xf7, 0x2e,
                0x00, 0x00, 0x4f, 0x8f, // message length
            };
            testData.Add(new object[] { instance, expected });

            msgId = "4344294800114444b5ce60df6bfec4cd";
            endpointId = "53354bf8bdf941f7801132dcb3730c30";
            instance = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeHeaderAck,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 0,
                Reserved = 0,
                MessageId = msgId,
                MessageSourceId = endpointId,
                ErrorCode = 20
            };
            expected = new byte[]
            {
                0x04, 0x10,  // opcode and version
                0x00, 0x00, 0x00, 0x00, // send timestamp
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // reserved
                0x43, 0x44, 0x29, 0x48, // message id
                0x00, 0x11, 0x44, 0x44,
                0xb5, 0xce, 0x60, 0xdf,
                0x6b, 0xfe, 0xc4, 0xcd,
                0x53, 0x35, 0x4b, 0xf8, // message src id
                0xbd, 0xf9, 0x41, 0xf7,
                0x80, 0x11, 0x32, 0xdc,
                0xb3, 0x73, 0x0c, 0x30,
                0x00, 0x14, // error code
            };
            testData.Add(new object[] { instance, expected });

            return testData;
        }
    }
}
