using ScalableIPC.Core;
using ScalableIPC.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ScalableIPC.Tests.Core
{
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
            var instance = new ProtocolDatagramFragmenter(emptyMessage, 0, null);
            Assert.Equal(new List<ProtocolDatagram> { new ProtocolDatagram() }, instance.Next(), 
                ProtocolDatagramComparer.Default);
            Assert.Equal(new List<ProtocolDatagram>(), instance.Next());
        }
    }
}