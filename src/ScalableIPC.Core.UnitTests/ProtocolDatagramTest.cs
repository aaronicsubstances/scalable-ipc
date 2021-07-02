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
        [Theory]
        [MemberData(nameof(CreateTestSerializeForErrorData))]
        public void TestSerializeForError(ProtocolDatagram instance, string expected)
        {
            var ex = Assert.ThrowsAny<Exception>(() => instance.Serialize());
            if (expected != null)
            {
                Assert.Contains(expected, ex.Message);
            }
        }

        public static List<object[]> CreateTestSerializeForErrorData()
        {
            var testData = new List<object[]>();

            // test for wrong length of msg id.
            var msgId = "".PadRight(30, '1');
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
            var expected = "e47fc3b1-f391-4f1a-aa82-814c01be6bea";
            testData.Add(new object[] { instance, expected });

            // test for null msg id.
            msgId = null;
            endpointId = "306ba29b2da24b0682589e6f25cadb36";
            instance = new ProtocolDatagram
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
            expected = "e47fc3b1-f391-4f1a-aa82-814c01be6bea";
            testData.Add(new object[] { instance, expected });

            // test for null data.
            msgId = "5f7d7630d8b6467db03d71419b6f87f0";
            endpointId = "1c26abdbef304ac4b533af48641bbc6d";
            instance = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 1625087321,
                Reserved = 0x03,
                MessageId = msgId,
                MessageDestinationId = endpointId,
                SequenceNumber = 0x14,
                Data = null,
                DataOffset = 0,
                DataLength = 5
            };
            expected = "f414e24d-d8bb-44dc-afb4-d34773d28e9a";
            testData.Add(new object[] { instance, expected });

            // test for invalid data offset.
            msgId = "0c02866124aa4a3bb0e8f70cad242a1e";
            endpointId = "2a75f7b2c1e5422f9e2da148bb50d379";
            instance = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 1625087321,
                Reserved = 0x03,
                MessageId = msgId,
                MessageDestinationId = endpointId,
                SequenceNumber = 0x14,
                Data = new byte[] { 0x68, 0x65, 0x6c, 0x6c, 0x6f }, // hello
                DataOffset = -1,
                DataLength = 5
            };
            expected = "149f8bf9-0226-40e3-a6ca-2d00541a4d75";
            testData.Add(new object[] { instance, expected });

            // test for invalid data length.
            msgId = "6a0789c7b2444014b00e348f6a695ef9";
            endpointId = "1a13893f2c534d03920062e0ad46390c";
            instance = new ProtocolDatagram
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
                DataLength = -7
            };
            expected = "9039a1e3-c4a1-4eff-b53f-059a7316b97d";
            testData.Add(new object[] { instance, expected });

            // test for invalid combination data offset and length.
            msgId = "3dda9a0042a24500919be9f4ef7dd437";
            endpointId = "f259ff43dc404d0c85cda925d0a950c9";
            instance = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 1625087321,
                Reserved = 0x03,
                MessageId = msgId,
                MessageDestinationId = endpointId,
                SequenceNumber = 0x14,
                Data = new byte[] { 0x68, 0x65, 0x6c, 0x6c, 0x6f }, // hello
                DataOffset = 4,
                DataLength = 3
            };
            expected = "786322b1-f408-4b9a-a41d-d95acecda445";
            testData.Add(new object[] { instance, expected });

            // test for null msg dest id.
            msgId = "5615e7e63dc24722b3ed0a0722c9a687";
            endpointId = null;
            instance = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 1625087321,
                Reserved = 0x03,
                MessageId = msgId,
                MessageDestinationId = endpointId,
                SequenceNumber = 0x14,
                Data = new byte[] { 0x68, 0x65, 0x6c, 0x6c, 0x6f }, // hello
                DataOffset = 4,
                DataLength = 1
            };
            expected = "938436a3-56aa-45f8-97ef-9715dea14cc4";
            testData.Add(new object[] { instance, expected });

            // test for invalid msg dest id.
            msgId = "7bf116b2bb52468284c9584c1ed04e3d";
            endpointId = "1";
            instance = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 1625087321,
                Reserved = 0x03,
                MessageId = msgId,
                MessageDestinationId = endpointId,
                SequenceNumber = 0x14,
                Data = new byte[] { 0x68, 0x65, 0x6c, 0x6c, 0x6f }, // hello
                DataOffset = 4,
                DataLength = 1
            };
            expected = "938436a3-56aa-45f8-97ef-9715dea14cc4";
            testData.Add(new object[] { instance, expected });

            // test for null message source id.
            msgId = "359d341300274004938c9f6058a68777";
            endpointId = null;
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
            expected = "3fd5c735-c487-4cef-976d-f8a0c52a06a3";
            testData.Add(new object[] { instance, expected });

            // test for invalid message source id.
            msgId = "7fbe93102c3e41e4b9264f2030e0b8d2";
            endpointId = "ab";
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
            expected = "3fd5c735-c487-4cef-976d-f8a0c52a06a3";
            testData.Add(new object[] { instance, expected });

            // test null msg dest id.
            msgId = "be1778d1cc2a4f54ada8ec05392fcb86";
            endpointId = null;
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
            expected = "39d799a9-98ee-4477-bd28-f126b38212ac";
            testData.Add(new object[] { instance, expected });

            // test invalid msg dest id.
            msgId = "da296e2643344bbfaf84eca18a53cdd4";
            endpointId = "0ff";
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
            expected = "39d799a9-98ee-4477-bd28-f126b38212ac";
            testData.Add(new object[] { instance, expected });

            // test null msg source id
            msgId = "dd686ff4616743589931d8e14c133e8b";
            endpointId = null;
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
            expected = "25b04616-6aff-4f55-b0c9-1b06922b1c44";
            testData.Add(new object[] { instance, expected });

            // test invalid msg source id
            msgId = "dd686ff4616743589931d8e14c133e8b";
            endpointId = "ae09cbb70106477fbaa25ae7d5962be0fd875c7afaa344f89c28800bb63123b7";
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
            expected = "25b04616-6aff-4f55-b0c9-1b06922b1c44";
            testData.Add(new object[] { instance, expected });

            // test invalid opcode
            msgId = "b5080f1419d04465b6dae1808b848fe6";
            endpointId = "56ebd94d8f0b48b4a853fdcef6fe61ba";
            instance = new ProtocolDatagram
            {
                OpCode = 0,
                Version = 0,
                SentAt = 0,
                Reserved = 0,
                MessageId = msgId,
                MessageSourceId = endpointId,
                MessageDestinationId = endpointId,
                ErrorCode = 0
            };
            expected = "6f66dbe8-0c15-48b6-8c6a-856762cdf3e9";
            testData.Add(new object[] { instance, expected });

            return testData;
        }
    }
}
