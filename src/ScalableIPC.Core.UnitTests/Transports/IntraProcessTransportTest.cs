using ScalableIPC.Core.Concurrency;
using ScalableIPC.Core.Transports;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ScalableIPC.Core.UnitTests.Transports
{
    public class IntraProcessTransportTest
    {
        [Fact]
        public void TestRealisticTwoWayTransmission()
        {
            var logs = new List<string>();
            var testEventLoop = new FakeEventLoopApi();
            var accraAddr = new GenericNetworkIdentifier { HostName = "accra" };
            var accraEndpoint = new IntraProcessTransport
            {
                LocalEndpoint = accraAddr,
                Callbacks = new TestTransportProcessor(logs, testEventLoop),
                EventLoop = testEventLoop
            };
            var kumasiAddr = new GenericNetworkIdentifier { HostName = "kumasi" };
            var kumasiEndpoint = new IntraProcessTransport
            {
                LocalEndpoint = kumasiAddr,
                Callbacks = new TestTransportProcessor(logs, testEventLoop),
                EventLoop = testEventLoop

            };
            accraEndpoint.Connections.Add(kumasiAddr, new IntraProcessTransport.Connection
            {
                ConnectedTransport = kumasiEndpoint,
                SendBehaviour = () =>
                {
                    return new IntraProcessTransport.SendConfig
                    {
                        DuplicateTransmissionDelays = new int[] { 3, 5 }
                    };
                }
            });
            kumasiEndpoint.Connections.Add(accraAddr, new IntraProcessTransport.Connection
            {
                ConnectedTransport = accraEndpoint,
                SendBehaviour = () =>
                {
                    return new IntraProcessTransport.SendConfig
                    {
                        SendDelay = 7,
                        SendError = new ProtocolOperationException(0, "error c6cf2870-6c61-4b96-ac69-636fec271321", null)
                    };
                }
            });

            // TestAccraToKumasiTransmission
            var msg = "hello";
            var msgBytes = Encoding.UTF8.GetBytes(msg);
            accraEndpoint.BeginSend(kumasiAddr, msgBytes, 0, msgBytes.Length, ex =>
            {
                logs.Add($"{testEventLoop.CurrentTimestamp}:received send cb:{ex?.Message ?? "success"}");
            });
            // advance time severally up to 20 ms
            testEventLoop.AdvanceTimeIndefinitely();
            testEventLoop.AdvanceTimeTo(20);
            var expectedLogs = new List<string>
            {
                "0:received send cb:success",
                $"3:received from accra:{msg}",
                $"5:received from accra:{msg}"
            };
            Assert.Equal(expectedLogs, logs);
        
            // TestKumasiToAccraTransmission
            logs.Clear();
            msg = "yes";
            msgBytes = Encoding.UTF8.GetBytes(msg);
            kumasiEndpoint.BeginSend(accraAddr, msgBytes, 0, msgBytes.Length, ex =>
            {
                logs.Add($"{testEventLoop.CurrentTimestamp}:received send cb:{ex?.Message ?? "success"}");
            });
            // advance time severally
            testEventLoop.AdvanceTimeIndefinitely();
            expectedLogs = new List<string>
            {
                $"20:received from kumasi:{msg}",
                "27:received send cb:error c6cf2870-6c61-4b96-ac69-636fec271321",
            };
            Assert.Equal(expectedLogs, logs);
        }

        [Theory]
        [MemberData(nameof(CreateBeginSendData))]
        public void TestBeginSend(string messageToSend, IntraProcessTransport.SendConfig sendConfig,
            bool supplyCb, List<string> expected)
        {
            var eventLoop = new FakeEventLoopApi();
            var actual = new List<string>();
            var msgBytes = Encoding.UTF8.GetBytes(messageToSend);
            Action<ProtocolOperationException> cb = null;
            if (supplyCb)
            {
                cb = ex =>
                {
                    actual.Add($"{eventLoop.CurrentTimestamp}:received send cb:{ex?.Message ?? "success"}");
                };
            }


            var addrA = new GenericNetworkIdentifier { HostName = "A" };
            var transportA = new IntraProcessTransport
            {
                LocalEndpoint = addrA,
                Callbacks = new TestTransportProcessor(actual, eventLoop),
                EventLoop = eventLoop

            };
            var addrB = new GenericNetworkIdentifier { HostName = "B" };
            var transportB = new IntraProcessTransport
            {
                LocalEndpoint = addrB,
                Callbacks = new TestTransportProcessor(actual, eventLoop),
                EventLoop = eventLoop

            };
            transportA.Connections.Add(addrB, new IntraProcessTransport.Connection
            {
                ConnectedTransport = transportB,
                SendBehaviour = () =>
                {
                    return sendConfig;
                }
            });

            // Now send.
            transportA.BeginSend(addrB, msgBytes, 0, msgBytes.Length, cb);

            // advance time severally
            eventLoop.AdvanceTimeIndefinitely();

            Assert.Equal(expected, actual);
        }

        public static List<object[]> CreateBeginSendData()
        {
            return new List<object[]>
            {
                new object[]{ "hi", null, false, new List<string>{ "0:received from A:hi" } },
                new object[]{ "hi", null, true,
                    new List<string>
                    {
                        "0:received send cb:success",
                        "0:received from A:hi"
                    }
                },
                new object[]{ "hi", new IntraProcessTransport.SendConfig { SendDelay = 12, }, true,
                    new List<string>
                    {
                        "0:received from A:hi",
                        "12:received send cb:success",
                    }
                },
                new object[]{ "hi",
                    new IntraProcessTransport.SendConfig
                    {
                        SendDelay = 12,
                        SendError = new ProtocolOperationException(0, "error tx", null)
                    }, 
                    true,
                    new List<string>
                    {
                        "0:received from A:hi",
                        "12:received send cb:error tx",
                    }
                },
                new object[]{ "hi",
                    new IntraProcessTransport.SendConfig
                    {
                        DuplicateTransmissionDelays = new int[0]
                    },
                    false,
                    new List<string>()
                },
                new object[]{ "hi",
                    new IntraProcessTransport.SendConfig
                    {
                        SendDelay = 3,
                        DuplicateTransmissionDelays = new int[]{ 8 }
                    },
                    true,
                    new List<string>
                    {
                        "3:received send cb:success",
                        "8:received from A:hi",
                    }
                },
                new object[]{ "hi",
                    new IntraProcessTransport.SendConfig
                    {
                        SendDelay = 3,
                        DuplicateTransmissionDelays = new int[]{ 1, 8, 11 }
                    },
                    true,
                    new List<string>
                    {
                        "1:received from A:hi",
                        "3:received send cb:success",
                        "8:received from A:hi",
                        "11:received from A:hi",
                    }
                },
                // test permissible error cases with transmission.
                new object[]{ "hi",
                    new IntraProcessTransport.SendConfig
                    {
                        DuplicateTransmissionDelays = new int[]{ -10 }
                    },
                    false,
                    new List<string>()
                },
                new object[]{ "hi",
                    new IntraProcessTransport.SendConfig
                    {
                        SendDelay = -10,
                        DuplicateTransmissionDelays = new int[0]
                    },
                    false,
                    new List<string>()
                },
            };
        }

        [Fact]
        public void TestBeginSendForErrors()
        {
            var eventLoop = new FakeEventLoopApi();
            var logs = new List<string>();
            var msgBytes = Encoding.UTF8.GetBytes("hi");

            var addrA = new GenericNetworkIdentifier { HostName = "A" };
            var transportA = new IntraProcessTransport
            {
                LocalEndpoint = addrA,
                Callbacks = new TestTransportProcessor(logs, eventLoop),
                EventLoop = eventLoop

            };
            var addrB = new GenericNetworkIdentifier { HostName = "B" };
            var transportB = new IntraProcessTransport
            {
                LocalEndpoint = addrB,
                Callbacks = new TestTransportProcessor(logs, eventLoop),
                EventLoop = eventLoop
            };
            transportA.Connections.Add(addrB, new IntraProcessTransport.Connection
            {
                ConnectedTransport = transportB,
                SendBehaviour = () =>
                {
                    return new IntraProcessTransport.SendConfig
                    {
                        SendDelay = -3
                    };
                }
            });

            // Now test error cases

            Assert.ThrowsAny<Exception>(() => transportA.BeginSend(addrB, msgBytes, 0, msgBytes.Length, ex => { }));

            Assert.ThrowsAny<Exception>(() => transportB.BeginSend(addrA, msgBytes, 0, msgBytes.Length, null));
        }
    }
}
