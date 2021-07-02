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

            msgId = "b3d16e39aa534fee9f335ba9b5845c8e";
            endpointId = "567cb07c75af404286196d2fc83e62db";
            instance = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 0,
                Reserved = 0,
                MessageId = msgId,
                MessageDestinationId = endpointId,
                SequenceNumber = 0,
                Data = new byte[] { 0x68, 0x65, 0x6c, 0x6c, 0x6f }, // hello
                DataOffset = 4,
                DataLength = 1
            };
            expected = new byte[]
            {
                0x01, 0x10,  // opcode and version
                0x00, 0x00, 0x00, 0x00, // send timestamp
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // reserved
                0xb3, 0xd1, 0x6e, 0x39, // message id
                0xaa, 0x53, 0x4f, 0xee,
                0x9f, 0x33, 0x5b, 0xa9,
                0xb5, 0x84, 0x5c, 0x8e,
                0x56, 0x7c, 0xb0, 0x7c, // message dest id
                0x75, 0xaf, 0x40, 0x42,
                0x86, 0x19, 0x6d, 0x2f,
                0xc8, 0x3e, 0x62, 0xdb,
                0x00, 0x00, 0x00, 0x00, // sequence number
                0x6f, // data
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
        [MemberData(nameof(CreateTestDeserializeData))]
        public void TestDeserialize(byte[] rawBytes, int offset, int length, ProtocolDatagram expected)
        {
            var actual = ProtocolDatagram.Deserialize(rawBytes, offset, length);
            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateTestDeserializeData()
        {
            var testData = new List<object[]>();

            // test data opcode
            var rawBytes = new byte[]
            {
                0x01, 0x10,  // opcode and version
                0x00, 0x00, 0x00, 0x00, // send timestamp
                0x60, 0xDC, 0xDD, 0x59,
                0x00, 0x00, 0x00, 0x03, // reserved
                0xe6, 0xa6, 0xa7, 0xe3, // message id
                0xae, 0x7c, 0x48, 0x42,
                0xbd, 0xa6, 0x99, 0xb9,
                0xef, 0x2c, 0xaa, 0xd7,
                0x13, 0x13, 0xb5, 0xa6, // message dest id
                0x96, 0xf5, 0x45, 0x50,
                0xba, 0x59, 0x52, 0x9c,
                0x2b, 0x5d, 0x0d, 0x0e,
                0x00, 0x00, 0x00, 0x14, // sequence number
                0x68, 0x65, 0x6c, 0x6c, // data
                0x6f,
            };
            int offset = 0;
            int length = rawBytes.Length;
            var expected = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 1625087321,
                Reserved = 0x03,
                MessageId = "e6a6a7e3ae7c4842bda699b9ef2caad7",
                MessageDestinationId = "1313b5a696f54550ba59529c2b5d0d0e",
                SequenceNumber = 0x14,
                Data = rawBytes,
                DataOffset = rawBytes.Length - 5,
                DataLength = 5
            };
            testData.Add(new object[] { rawBytes, offset, length, expected });

            rawBytes = new byte[]
            {
                0x76, 0x23, // extraneous
                0x01, 0x10,  // opcode and version
                0x00, 0x00, 0x00, 0x00, // send timestamp
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // reserved
                0xb3, 0xd1, 0x6e, 0x39, // message id
                0xaa, 0x53, 0x4f, 0xee,
                0x9f, 0x33, 0x5b, 0xa9,
                0xb5, 0x84, 0x5c, 0x8e,
                0x56, 0x7c, 0xb0, 0x7c, // message dest id
                0x75, 0xaf, 0x40, 0x42,
                0x86, 0x19, 0x6d, 0x2f,
                0xc8, 0x3e, 0x62, 0xdb,
                0x00, 0x00, 0x00, 0x00, // sequence number
                0x6f, // data
                0x70, // extraneous exclusion
            };
            offset = 2;
            length = rawBytes.Length - 3;
            expected = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 0,
                Reserved = 0,
                MessageId = "b3d16e39aa534fee9f335ba9b5845c8e",
                MessageDestinationId = "567cb07c75af404286196d2fc83e62db",
                SequenceNumber = 0,
                Data = rawBytes,
                DataOffset = rawBytes.Length - 2,
                DataLength = 1
            };
            testData.Add(new object[] { rawBytes, offset, length, expected });
            
            // test data ack
            rawBytes = new byte[]
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
            offset = 0;
            length = rawBytes.Length;
            expected = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeDataAck,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 0,
                Reserved = 0,
                MessageId = "e4c871c91e364267ac38e1bc87af091a",
                MessageSourceId = "0ee491726ac14d7a92121560946602a1",
                SequenceNumber = 2,
                ErrorCode = 1
            };
            testData.Add(new object[] { rawBytes, offset, length, expected });

            rawBytes = new byte[]
            {
                0x60, // extraneous
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
                0x00, 0x00 // extraneous inclusion.
            };
            offset = 1;
            length = rawBytes.Length - 1;
            expected = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeDataAck,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 0,
                Reserved = 0,
                MessageId = "e4c871c91e364267ac38e1bc87af091a",
                MessageSourceId = "0ee491726ac14d7a92121560946602a1",
                SequenceNumber = 2,
                ErrorCode = 1
            };
            testData.Add(new object[] { rawBytes, offset, length, expected });

            // test header opcode
            rawBytes = new byte[]
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
            offset = 0;
            length = rawBytes.Length;
            expected = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeHeader,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 1_625_119_002,
                Reserved = 0,
                MessageId = "be1778d1cc2a4f54ada8ec05392fcb86",
                MessageDestinationId = "9bd0cc9e6e574079b5509555923df72e",
                MessageLength = 20367
            };
            testData.Add(new object[] { rawBytes, offset, length, expected });

            rawBytes = new byte[]
            {
                0x8a, // extraneous
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
                0x90, 0x41, // extraneous inclusion
            };
            offset = 1;
            length = rawBytes.Length - 1;
            expected = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeHeader,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 1_625_119_002,
                Reserved = 0,
                MessageId = "be1778d1cc2a4f54ada8ec05392fcb86",
                MessageDestinationId = "9bd0cc9e6e574079b5509555923df72e",
                MessageLength = 20367
            };
            testData.Add(new object[] { rawBytes, offset, length, expected });

            // test header ack
            rawBytes = new byte[]
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
            offset = 0;
            length = rawBytes.Length;
            expected = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeHeaderAck,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 0,
                Reserved = 0,
                MessageId = "4344294800114444b5ce60df6bfec4cd",
                MessageSourceId = "53354bf8bdf941f7801132dcb3730c30",
                ErrorCode = 20
            };
            testData.Add(new object[] { rawBytes, offset, length, expected });

            rawBytes = new byte[]
            {
                0x00, 0x01, 0x02, 0x03, // extraneous
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
                0x00, 0x14, // error code,
                0x04, 0x03, 0x02, 0x01 // extraneous inclusion
            };
            offset = 4;
            length = rawBytes.Length - 4;
            expected = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeHeaderAck,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                SentAt = 0,
                Reserved = 0,
                MessageId = "4344294800114444b5ce60df6bfec4cd",
                MessageSourceId = "53354bf8bdf941f7801132dcb3730c30",
                ErrorCode = 20
            };
            testData.Add(new object[] { rawBytes, offset, length, expected });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateTestSerializeForErrorData))]
        public void TestSerializeForError(ProtocolDatagram instance, string expectedErrorId)
        {
            var ex = Assert.ThrowsAny<Exception>(() => instance.Serialize());
            if (expectedErrorId != null)
            {
                Assert.Contains(expectedErrorId, ex.Message);
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

        [Theory]
        [MemberData(nameof(CreateTestDeserializeForErrorData))]
        public void TestDeserializeForError(byte[] rawBytes, int offset, int length, string expectedErrorId)
        {
            var ex = Assert.ThrowsAny<Exception>(() => ProtocolDatagram.Deserialize(rawBytes, offset, length));
            if (expectedErrorId != null)
            {
                Assert.Contains(expectedErrorId, ex.Message);
            }
        }

        public static List<object[]> CreateTestDeserializeForErrorData()
        {
            var testData = new List<object[]>();

            // test for definitely too small
            var rawBytes = new byte[]
            {
                0x01, 0x10,  // opcode and version
                0x00, 0x00, 0x00, 0x00, // send timestamp
                0x60, 0xDC, 0xDD, 0x59,
                0x00, 0x00, 0x00, 0x03, // reserved
                0xe6, 0xa6, 0xa7, 0xe3, // message id
                0xae, 0x7c, 0x48, 0x42,
                0xbd, 0xa6, 0x99, 0xb9,
                0xef, 0x2c, 0xaa, 0xd7
            };
            int offset = 0;
            int length = rawBytes.Length;
            var expected = "b451b01f-c474-49e4-ad3f-643d9e849664";
            testData.Add(new object[] { rawBytes, offset, length, expected });

            // test null data
            rawBytes = null;
            offset = 0;
            length = 5;
            expected = "6ee7a25c-090e-4321-b429-1ed4b26f3c59";
            testData.Add(new object[] { rawBytes, offset, length, expected });

            // test invalid data offset
            rawBytes = new byte[]
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
            offset = -1;
            length = rawBytes.Length;
            expected = "58eacdaa-ae6f-4779-a7bd-ec3d28bbadc8";
            testData.Add(new object[] { rawBytes, offset, length, expected });

            // test invalid data length
            rawBytes = new byte[]
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
            offset = 0;
            length = -2;
            expected = "02c8ef5c-9e30-4630-a1bc-c9c8dc73cfac";
            testData.Add(new object[] { rawBytes, offset, length, expected });

            // test invalid offset - length combination.
            rawBytes = new byte[]
            {
                0x76, 0x23, // extraneous
                0x01, 0x10,  // opcode and version
                0x00, 0x00, 0x00, 0x00, // send timestamp
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // reserved
                0xb3, 0xd1, 0x6e, 0x39, // message id
                0xaa, 0x53, 0x4f, 0xee,
                0x9f, 0x33, 0x5b, 0xa9,
                0xb5, 0x84, 0x5c, 0x8e,
                0x56, 0x7c, 0xb0, 0x7c, // message dest id
                0x75, 0xaf, 0x40, 0x42,
                0x86, 0x19, 0x6d, 0x2f,
                0xc8, 0x3e, 0x62, 0xdb,
                0x00, 0x00, 0x00, 0x00, // sequence number
                0x6f, // data
                0x70, // extraneous exclusion
            };
            offset = 2;
            length = rawBytes.Length;
            expected = "cf0da519-d5e3-4bb3-b6f6-a9cb0db69fa8";
            testData.Add(new object[] { rawBytes, offset, length, expected });

            // test too small data pdu
            rawBytes = new byte[]
            {
                0x76, 0x23, // extraneous
                0x01, 0x10,  // opcode and version
                0x00, 0x00, 0x00, 0x00, // send timestamp
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, // reserved
                0xb3, 0xd1, 0x6e, 0x39, // message id
                0xaa, 0x53, 0x4f, 0xee,
                0x9f, 0x33, 0x5b, 0xa9,
                0xb5, 0x84, 0x5c, 0x8e,
                0x56, 0x7c, 0xb0, 0x7c, // message dest id
                0x75, 0xaf, 0x40, 0x42,
                0x86, 0x19, 0x6d, 0x2f,
                0xc8, 0x3e, 0x62, 0xdb
            };
            offset = 2;
            length = rawBytes.Length - 2;
            expected = "340093ec-041f-44fe-a5e1-e53bcb1b500b";
            testData.Add(new object[] { rawBytes, offset, length, expected });

            // test too small data ack
            rawBytes = new byte[]
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
                0x94, 0x66, 0x02, 0xa1
            };
            offset = 0;
            length = rawBytes.Length;
            expected = "2643d9ea-c82f-4f21-90ea-576b591fd294";
            testData.Add(new object[] { rawBytes, offset, length, expected });

            // test too small header
            rawBytes = new byte[]
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
                0x92, 0x3d, 0xf7, 0x2e
            };
            offset = 0;
            length = rawBytes.Length;
            expected = "4121da80-a008-4a7a-a843-aa93656f6a30";
            testData.Add(new object[] { rawBytes, offset, length, expected });

            // test too small header ack
            rawBytes = new byte[]
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
                0xb3, 0x73, 0x0c, 0x30
            };
            offset = 0;
            length = rawBytes.Length;
            expected = "60713574-6f3f-4cd3-9f5c-7f175ea4e87f";
            testData.Add(new object[] { rawBytes, offset, length, expected });

            // test invalid opcode
            rawBytes = new byte[]
            {
                0x00, 0x01, 0x02, 0x03, // extraneous
                0xf4, 0x10,  // opcode and version
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
                0x00, 0x14, // error code,
                0x04, 0x03, 0x02, 0x01 // extraneous inclusion
            };
            offset = 4;
            length = rawBytes.Length - 4;
            expected = "1c13f39a-51f0-4f8d-80f3-5b06f6cfb769";
            testData.Add(new object[] { rawBytes, offset, length, expected });

            return testData;
        }
    }
}
