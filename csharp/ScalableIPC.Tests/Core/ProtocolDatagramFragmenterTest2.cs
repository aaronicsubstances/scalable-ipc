using ScalableIPC.Core;
using ScalableIPC.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ScalableIPC.Tests.Core
{
    /// <summary>
    /// For test of instances.
    /// </summary>
    public class ProtocolDatagramFragmenterTest2
    {
        private readonly ProtocolDatagramFragmenter _instance;
        private readonly ProtocolMessage _message;

        public ProtocolDatagramFragmenterTest2()
        {
            _message = new ProtocolMessage
            {
                DataBytes = new byte[0], // contents not needed
                DataOffset = 190,
                DataLength = 5000,
                Attributes = new Dictionary<string, List<string>>
                {
                    { "s_e_f", new List<string>{ "so" } },
                    { "protocol", new List<string>{ "http" } },
                    { "rand", new List<string>{ "4578", "910" } }
                },
            };
            int maxFragmentSize = 512;
            List<string> extraOptionsToSkip = null;
            int maxFragmentOptionsSize = 1024;
            int maxFragmentBatchSize = 2048;
            _instance = new ProtocolDatagramFragmenter(_message, maxFragmentSize, extraOptionsToSkip,
                maxFragmentOptionsSize, maxFragmentBatchSize);
        }

        [Fact]
        public void TestNext()
        {
            var testData = CreateTestNextData();
            for (int i = 0; i < testData.Count; i++)
            {
                List<ProtocolDatagram> expected = testData[i];
                var actual = _instance.Next();
                Assert.Equal(expected, actual, ProtocolDatagramComparer.Default);
            }
            Assert.Equal(new List<ProtocolDatagram>(), _instance.Next());
        }

        private List<List<ProtocolDatagram>> CreateTestNextData()
        {
            var testData = new List<List<ProtocolDatagram>>();

            var options = new ProtocolDatagramOptions();
            options.AllOptions.Add("protocol", new List<string> { "http" });
            options.AllOptions.Add("rand", new List<string> { "4578", "910" });

            // 1st batch of fragments.
            {
                var fragments = new List<ProtocolDatagram>();
                testData.Add(fragments);

                var datagram = new ProtocolDatagram
                {
                    Options = options,
                    DataBytes = _message.DataBytes,
                    DataOffset = _message.DataOffset,
                    DataLength = 370
                };
                fragments.Add(datagram);

                datagram = new ProtocolDatagram
                {
                    DataBytes = _message.DataBytes,
                    DataOffset = _message.DataOffset + 370,
                    DataLength = 412
                };
                fragments.Add(datagram);

                datagram = new ProtocolDatagram
                {
                    DataBytes = _message.DataBytes,
                    DataOffset = _message.DataOffset + 782,
                    DataLength = 412
                };
                fragments.Add(datagram);

                datagram = new ProtocolDatagram
                {
                    DataBytes = _message.DataBytes,
                    DataOffset = _message.DataOffset + 1194,
                    DataLength = 412
                };
                fragments.Add(datagram);

                datagram = new ProtocolDatagram
                {
                    DataBytes = _message.DataBytes,
                    DataOffset = _message.DataOffset + 1606,
                    DataLength = 400
                };
                fragments.Add(datagram);
            }

            // 2nd batch of fragments.
            {
                var fragments = new List<ProtocolDatagram>();
                testData.Add(fragments);

                var datagram = new ProtocolDatagram
                {
                    Options = options,
                    DataBytes = _message.DataBytes,
                    DataOffset = _message.DataOffset + 2006,
                    DataLength = 370
                };
                fragments.Add(datagram);

                datagram = new ProtocolDatagram
                {
                    DataBytes = _message.DataBytes,
                    DataOffset = _message.DataOffset + 2376,
                    DataLength = 412
                };
                fragments.Add(datagram);

                datagram = new ProtocolDatagram
                {
                    DataBytes = _message.DataBytes,
                    DataOffset = _message.DataOffset + 2788,
                    DataLength = 412
                };
                fragments.Add(datagram);

                datagram = new ProtocolDatagram
                {
                    DataBytes = _message.DataBytes,
                    DataOffset = _message.DataOffset + 3200,
                    DataLength = 412
                };
                fragments.Add(datagram);

                datagram = new ProtocolDatagram
                {
                    DataBytes = _message.DataBytes,
                    DataOffset = _message.DataOffset + 3612,
                    DataLength = 400
                };
                fragments.Add(datagram);
            }

            // 3rd batch of fragments.
            {
                var fragments = new List<ProtocolDatagram>();
                testData.Add(fragments);

                var datagram = new ProtocolDatagram
                {
                    Options = options,
                    DataBytes = _message.DataBytes,
                    DataOffset = _message.DataOffset + 4012,
                    DataLength = 370
                };
                fragments.Add(datagram);

                datagram = new ProtocolDatagram
                {
                    DataBytes = _message.DataBytes,
                    DataOffset = _message.DataOffset + 4382,
                    DataLength = 412
                };
                fragments.Add(datagram);

                datagram = new ProtocolDatagram
                {
                    DataBytes = _message.DataBytes,
                    DataOffset = _message.DataOffset + 4794,
                    DataLength = 206
                };
                fragments.Add(datagram);
            }

            return testData;
        }

        [Fact]
        public void TestEmptyInput()
        {
            var emptyMessage = new ProtocolMessage();
            var instance = new ProtocolDatagramFragmenter(emptyMessage, 110, null);
            Assert.Equal(new List<ProtocolDatagram> { new ProtocolDatagram() }, instance.Next(), 
                ProtocolDatagramComparer.Default);
            Assert.Equal(new List<ProtocolDatagram>(), instance.Next());
        }

        [Fact]
        public void TestOptionsTakingAllSpace()
        {
            var attsOnlyMsg = new ProtocolMessage
            {
                Attributes = new Dictionary<string, List<string>>
                {
                    { "k1", new List<string>{ "12", "34" } }
                }
            };
            var opts1 = new ProtocolDatagramOptions();
            opts1.AllOptions.Add("k1", new List<string> { "12" });
            var opts2 = new ProtocolDatagramOptions();
            opts2.AllOptions.Add("k1", new List<string> { "34" });
            var expected = new List<ProtocolDatagram>
            { 
                new ProtocolDatagram { Options = opts1 },
                new ProtocolDatagram { Options = opts2 }
            };
            var instance = new ProtocolDatagramFragmenter(attsOnlyMsg, 110, null, 150, 18);
            Assert.Equal(expected, instance.Next(),
                ProtocolDatagramComparer.Default);
            Assert.Equal(new List<ProtocolDatagram>(), instance.Next());
        }

        [Theory]
        [MemberData(nameof(CreateTestNext2Data))]
        public void TestNext2(ProtocolMessage message, int maxFragmentSize, List<string> extraOptionsToSkip,
            int maxFragmentOptionsSize, int maxFragmentBatchSize, List<List<ProtocolDatagram>> expected)
        {
            var instance = new ProtocolDatagramFragmenter(message, maxFragmentSize, extraOptionsToSkip,
                maxFragmentOptionsSize, maxFragmentBatchSize);
            int index = 0;
            while (true)
            {
                var actual = instance.Next();
                if (index >= expected.Count)
                {
                    throw new Exception("expected index not found at " + index);
                }
                Assert.Equal(expected[index++], actual, ProtocolDatagramComparer.Default);
                if (actual.Count == 0)
                {
                    break;
                }
            }
        }

        public static List<object[]> CreateTestNext2Data()
        {
            var testData = new List<object[]>();

            // empty input test
            ProtocolMessage message = new ProtocolMessage
            {
                SessionId = "x"
            };
            int maxFragmentSize = 110;
            List<string> extraOptionsToSkip = null;
            int maxFragmentOptionsSize = 150;
            int maxFragmentBatchSize = 200;
            List<List<ProtocolDatagram>> expected = new List<List<ProtocolDatagram>>
            {
                new List<ProtocolDatagram>
                {
                    new ProtocolDatagram
                    {
                        SessionId = "x"
                    }
                },
                new List<ProtocolDatagram>()
            };

            testData.Add(new object[] { message, maxFragmentSize, extraOptionsToSkip,
                maxFragmentOptionsSize, maxFragmentBatchSize, expected });

            // 1 batch
            message = new ProtocolMessage
            {
                DataLength = 25
            };
            expected = new List<List<ProtocolDatagram>>
            {
                new List<ProtocolDatagram>
                {
                    new ProtocolDatagram
                    {
                        DataLength = 10
                    },
                    new ProtocolDatagram
                    {
                        DataOffset = 10,
                        DataLength = 10
                    },
                    new ProtocolDatagram
                    {
                        DataOffset = 20,
                        DataLength = 5
                    }
                },
                new List<ProtocolDatagram>()
            };

            testData.Add(new object[] { message, maxFragmentSize, extraOptionsToSkip,
                maxFragmentOptionsSize, maxFragmentBatchSize, expected });

            // 2 batches
            maxFragmentSize = 140;
            maxFragmentBatchSize = 200;
            message = new ProtocolMessage
            {
                DataLength = 300
            };
            expected = new List<List<ProtocolDatagram>>
            {
                new List<ProtocolDatagram>
                {
                    new ProtocolDatagram
                    {
                        DataLength = 40
                    },
                    new ProtocolDatagram
                    {
                        DataOffset = 40,
                        DataLength = 40
                    },
                    new ProtocolDatagram
                    {
                        DataOffset = 80,
                        DataLength = 40
                    },
                    new ProtocolDatagram
                    {
                        DataOffset = 120,
                        DataLength = 40
                    },
                    new ProtocolDatagram
                    {
                        DataOffset = 160,
                        DataLength = 40
                    }
                },
                new List<ProtocolDatagram>
                {
                    new ProtocolDatagram
                    {
                        DataOffset = 200,
                        DataLength = 40,
                    },
                    new ProtocolDatagram
                    {
                        DataOffset = 240,
                        DataLength = 40
                    },
                    new ProtocolDatagram
                    {
                        DataOffset = 280,
                        DataLength = 20
                    }
                },
                new List<ProtocolDatagram>()
            };

            testData.Add(new object[] { message, maxFragmentSize, extraOptionsToSkip,
                maxFragmentOptionsSize, maxFragmentBatchSize, expected });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateTestNextErrorData))]
        public void TestNextError(ProtocolMessage message, int maxFragmentSize, List<string> extraOptionsToSkip,
            int maxFragmentOptionsSize, int maxFragmentBatchSize, string expected)
        {
            var instance = new ProtocolDatagramFragmenter(message, maxFragmentSize, extraOptionsToSkip,
                maxFragmentOptionsSize, maxFragmentBatchSize);
            var actual = Assert.ThrowsAny<Exception>(() => instance.Next());
            if (expected != null)
            {
                Assert.Contains(expected, actual.Message);
            }
        }

        public static List<object[]> CreateTestNextErrorData()
        {
            var testData = new List<object[]>();

            // fragment size too small
            ProtocolMessage message = new ProtocolMessage
            {
                SessionId = "x"
            };
            int maxFragmentSize = 10;
            List<string> extraOptionsToSkip = null;
            int maxFragmentOptionsSize = 150;
            int maxFragmentBatchSize = 200;

            testData.Add(new object[] { message, maxFragmentSize, extraOptionsToSkip,
                maxFragmentOptionsSize, maxFragmentBatchSize, "98130f24-fae0-4d1b-bacc-6344aa2f6113" });

            // all attributes fit into fragment size, leaving no room for data.
            maxFragmentSize = 110;
            maxFragmentBatchSize = 18;
            message = new ProtocolMessage
            {
                Attributes = new Dictionary<string, List<string>>
                {
                    { "k1", new List<string>{ "12", "34" } }
                },
                DataLength = 25
            };

            testData.Add(new object[] { message, maxFragmentSize, extraOptionsToSkip,
                maxFragmentOptionsSize, maxFragmentBatchSize, "122cc165-f7df-4985-8167-bc84fd25752d" });

            // attributes exceed fragment batch size.
            maxFragmentSize = 120;
            maxFragmentBatchSize = 30;
            message = new ProtocolMessage
            {
                Attributes = new Dictionary<string, List<string>>
                {
                    { "abcdef", new List<string>{ "1", "22", "33"} },
                    { "ghijkl", new List<string>{ "1", "22", "33"} }
                },
                DataLength = 300
            };

            testData.Add(new object[] { message, maxFragmentSize, extraOptionsToSkip,
                maxFragmentOptionsSize, maxFragmentBatchSize, "84017b75-4533-4e40-8541-85f12e9410d3" });

            return testData;
        }
    }
}