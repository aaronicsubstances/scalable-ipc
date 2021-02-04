using Newtonsoft.Json;
using ScalableIPC.Core;
using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using ScalableIPC.Core.Helpers;
using ScalableIPC.Core.Networks;
using ScalableIPC.Core.Networks.Common;
using ScalableIPC.Core.Session;
using ScalableIPC.IntegrationTests.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.IntegrationTests.Core.Networks
{
    [Collection("SequentialTests")]
    public class MemoryNetworkApiTest
    {
        internal static readonly string LogDataKeyConfiguredForSendOpen = "configuredForSend";
        internal static readonly string LogDataKeySendException = "sendException";
        internal static readonly string LogDataKeySerializedDatagram = "serializedDatagram";
        internal static readonly string LogDataKeyDatagramHashCode = "datagramHashCode";
        internal static readonly string LogDataKeyTimestamp = "timestamp";
        internal static readonly string LogDataKeyRemoteEndpoint = "remoteEndpoint";

        internal static readonly string ErrorTraceValue = "f3157968-128f-4520-8448-ab0a0cef2eee"; 

        private readonly MemoryNetworkApi _accraEndpoint, _kumasiEndpoint;
        private readonly GenericNetworkIdentifier _accraAddr, _kumasiAddr;

        public MemoryNetworkApiTest()
        {
            _accraAddr = new GenericNetworkIdentifier { HostName = "accra" };
            _accraEndpoint = new MemoryNetworkApi
            {
                LocalEndpoint = _accraAddr,
                SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(TestSessionHandler)),
            };

            _kumasiAddr = new GenericNetworkIdentifier { HostName = "kumasi" };
            _kumasiEndpoint = new MemoryNetworkApi
            {
                LocalEndpoint = _kumasiAddr,
                SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(TestSessionHandler)),
            };
            _accraEndpoint.ConnectedNetworks.Add(_kumasiAddr, _kumasiEndpoint);
            _kumasiEndpoint.ConnectedNetworks.Add(_accraAddr, _accraEndpoint);
        }

        private List<TestLogRecord> GetValidatedTestLogs()
        {
            return TestDatabase.GetTestLogs(record =>
            {
                if (record.Logger.Contains(typeof(MemoryNetworkApi).FullName) ||
                    record.Logger.Contains(typeof(MemoryNetworkApiTest).FullName))
                {
                    return true;
                }
                throw new Exception($"Unexpected logger found in records: {record.Logger}");
            });
        }

        private Task<List<string>> WaitForAllLogicalThreads(int waitTimeSecs)
        {
            return WaitForNextRoundOfLogicalThreads(waitTimeSecs, -1);
        }

        private async Task<List<string>> WaitForNextRoundOfLogicalThreads(int waitTimeSecs, int lastId)
        {
            var testLogs = GetValidatedTestLogs().Where(x => x.Id > lastId);
            var newLogicalThreadIds = new List<string>();
            int nextLastId = lastId;
            foreach (var testLog in testLogs)
            {
                if (testLog.ParsedProperties.ContainsKey(CustomLogEvent.LogDataKeyNewLogicalThreadId))
                {
                    newLogicalThreadIds.Add((string)testLog.ParsedProperties[
                        CustomLogEvent.LogDataKeyNewLogicalThreadId]);
                    nextLastId = testLog.Id;
                }
            }
            if (newLogicalThreadIds.Count == 0)
            {
                return GetValidatedTestLogs().Where(x => x.ParsedProperties.ContainsKey(
                    CustomLogEvent.LogDataKeyNewLogicalThreadId))
                    .Select(x => (string)x.ParsedProperties[CustomLogEvent.LogDataKeyNewLogicalThreadId])
                    .ToList();
            }
            await Awaitility.WaitAsync(TimeSpan.FromSeconds(waitTimeSecs), lastCall =>
            {
                var startRemCount = newLogicalThreadIds.Count;
                CustomLoggerFacade.WriteToStdOut(false, $"Start rem count = {startRemCount}", null);
                var testLogs = GetValidatedTestLogs().Where(x => x.Id > lastId).ToList();
                CustomLoggerFacade.WriteToStdOut(false, $"Fetch count = {testLogs.Count}", null);
                foreach (var testLog in testLogs)
                {
                    if (testLog.ParsedProperties.ContainsKey(CustomLogEvent.LogDataKeyEndingLogicalThreadId))
                    {
                        newLogicalThreadIds.Remove((string)testLog.ParsedProperties[
                            CustomLogEvent.LogDataKeyEndingLogicalThreadId]);
                    }
                }
                CustomLoggerFacade.WriteToStdOut(false, $"End rem count = {newLogicalThreadIds.Count}", null);
                if (lastCall && startRemCount == newLogicalThreadIds.Count)
                {
                    Assert.Empty(newLogicalThreadIds);
                }
                return newLogicalThreadIds.Count == 0;
            });
            return await WaitForNextRoundOfLogicalThreads(waitTimeSecs, nextLastId);
        }

        private List<string> GetRelatedLogicalThreadIds(List<TestLogRecord> testLogs, Guid initId)
        {
            var relatedIds = new List<string> { initId.ToString() };
            foreach (var testLog in testLogs)
            {
                var newLogicalThreadId = testLog.GetStrProp(CustomLogEvent.LogDataKeyNewLogicalThreadId);
                if (newLogicalThreadId != null)
                {
                    var parentLogicalThreadId = testLog.GetCurrentLogicalThreadId();
                    if (relatedIds.Contains(parentLogicalThreadId))
                    {
                        relatedIds.Add(newLogicalThreadId);
                    }
                }
            }
            return relatedIds;
        }

        private void AssertAbsenceOfSendReceiveErrors(List<TestLogRecord> testLogs,
            params string[] errorPositionsToSkip)
        {
            var allErrorPositionIds = new string[] { "1b554af7-6b87-448a-af9c-103d9c676030" ,
                "bb741504-3a4b-4ea3-a749-21fc8aec347f", "74566405-9d14-489f-9dbd-0c9b3e0e3e67",
                "823df166-6430-4bcf-ae8f-2cb4c5e77cb1", "86a662a4-c098-4053-ac26-32b984079419",
                "c42673d8-a1d1-4cb1-b9c1-a5369bedff64" };
            foreach (var testLog in testLogs)
            {
                var logPositionId = testLog.GetLogPositionId();
                if (logPositionId != null)
                {
                    if (errorPositionsToSkip != null && errorPositionsToSkip.Contains(logPositionId))
                    {
                        // skip.
                    }
                    else
                    {
                        Assert.DoesNotContain(logPositionId, allErrorPositionIds);
                    }
                }
            }
        }

        [Fact]
        public async Task TestOpenSessionAsync()
        {
            Assert.False(_accraEndpoint.IsShuttingDown());

            TestDatabase.ResetDb();
            var sessionId = Guid.NewGuid().ToString("n");
            var handlerArg = new TestSessionHandler(false);
            var openPromise = _accraEndpoint.OpenSessionAsync(_kumasiAddr, sessionId,
                handlerArg);
            var sessionHandler = await ((DefaultPromise<ISessionHandler>)openPromise).WrappedTask;

            Assert.Equal(handlerArg, sessionHandler);
            Assert.Equal(_accraEndpoint, sessionHandler.NetworkApi);
            Assert.Equal(sessionId, sessionHandler.SessionId);
            Assert.Equal(_kumasiAddr, sessionHandler.RemoteEndpoint);

            var testLogs = GetValidatedTestLogs();
            var logNavigator = new LogNavigator<TestLogRecord>(testLogs);
            // test that no new session handler is created but CompleteInit is called on what was passed.
            var record = logNavigator.Next(
                rec => rec.Properties.Contains("f8f6c939-09e2-4f8d-9b2b-fd25ee4ead9e"));
            Assert.Null(record);
            record = logNavigator.Next(
                rec => rec.Properties.Contains("3f4f66e2-dafc-4c79-aa42-6f988a337d78"));
            Assert.Equal(true, record.ParsedProperties[LogDataKeyConfiguredForSendOpen]);
            // Test that ProcessOpen is called.
            record = logNavigator.Next(
                rec => rec.Properties.Contains("12f2f4e3-af83-460f-8d46-0ac0fc9d95fe"));
            Assert.NotNull(record);

            // test that session id can't be reused. 
            await Assert.ThrowsAnyAsync<Exception>(() =>
            {
                var promise = _accraEndpoint.OpenSessionAsync(_kumasiAddr, sessionId,
                    null);
                return ((DefaultPromise<ISessionHandler>)promise).WrappedTask;
            });

            // test without prespecified session id or handler.
            TestDatabase.ResetDb();
            openPromise = _accraEndpoint.OpenSessionAsync(_kumasiAddr, null,
                null);
            var sessionHandler2 = await ((DefaultPromise<ISessionHandler>)openPromise).WrappedTask;

            Assert.Equal(typeof(TestSessionHandler), sessionHandler2.GetType());
            Assert.Equal(_accraEndpoint, sessionHandler2.NetworkApi);
            Assert.Equal(_kumasiAddr, sessionHandler2.RemoteEndpoint);

            testLogs = GetValidatedTestLogs();
            logNavigator = new LogNavigator<TestLogRecord>(testLogs);
            // test that a new session handler is created and CompleteInit is called on it.
            record = logNavigator.Next(
                rec => rec.Properties.Contains("f8f6c939-09e2-4f8d-9b2b-fd25ee4ead9e"));
            Assert.NotNull(record);
            record = logNavigator.Next(
                rec => rec.Properties.Contains("3f4f66e2-dafc-4c79-aa42-6f988a337d78"));
            var sessionId2 = sessionHandler2.SessionId;
            Assert.NotNull(sessionId2);
            Assert.Equal(true, record.ParsedProperties[LogDataKeyConfiguredForSendOpen]);
            // Test that ProcessOpen is called.
            record = logNavigator.Next(
                rec => rec.Properties.Contains("12f2f4e3-af83-460f-8d46-0ac0fc9d95fe"));
            Assert.NotNull(record);

            // test that session id can't be reused. 
            await Assert.ThrowsAnyAsync<Exception>(() =>
            {
                var promise = _accraEndpoint.OpenSessionAsync(_kumasiAddr, sessionId2,
                    null);
                return ((DefaultPromise<ISessionHandler>)promise).WrappedTask;
            });

            // test that open session fails if remote endpoint hasn't already been set up. 
            await Assert.ThrowsAnyAsync<Exception>(() =>
            {
                var promise = _accraEndpoint.OpenSessionAsync(
                    new GenericNetworkIdentifier { Port = 34 }, null, null);
                return ((DefaultPromise<ISessionHandler>)promise).WrappedTask;
            });

            // test that creation fails if session handler factory is required
            // but isn't set.
            var prevHandlerFactory = _accraEndpoint.SessionHandlerFactory;
            _accraEndpoint.SessionHandlerFactory = null;
            await Assert.ThrowsAnyAsync<Exception>(() =>
            {
                var promise = _accraEndpoint.OpenSessionAsync(_kumasiAddr, null,
                    null);
                return ((DefaultPromise<ISessionHandler>)promise).WrappedTask;
            });

            // reset
            _accraEndpoint.SessionHandlerFactory = prevHandlerFactory;

            // test that creation fails if shutdown has been started.
            var voidRetPromise = _accraEndpoint.ShutdownAsync(0);
            await ((DefaultPromise<VoidType>)voidRetPromise).WrappedTask;

            Assert.True(_accraEndpoint.IsShuttingDown());

            await Assert.ThrowsAnyAsync<Exception>(() =>
            {
                var promise = _accraEndpoint.OpenSessionAsync(_kumasiAddr, null,
                    null);
                return ((DefaultPromise<ISessionHandler>)promise).WrappedTask;
            });
        }

        [Theory]
        [MemberData(nameof(CreateTestSendReceiveData))]
        public async Task TestSendReceive(Action<MemoryNetworkApi, ProtocolDatagram> customizer, 
            MemoryNetworkApi.SendConfig sendConfig, MemoryNetworkApi.TransmissionConfig transmissionConfig,
            string message, string sessionId,
            bool expectCallback, string expectedCallbackEx,
            bool expectReceives, bool expectCompleteInitCall, bool skipReceiveCallExpectation,
            string[] expectedErrorLogPositions)
        {
            TestDatabase.ResetDb();

            _accraEndpoint.SendBehaviour = new MemoryNetworkApi.DefaultSendBehaviour
            {
                Config = sendConfig
            };
            _accraEndpoint.TransmissionBehaviour = new MemoryNetworkApi.DefaultTransmissionBehaviour
            {
                Config = transmissionConfig
            };

            var dataToSend = ProtocolDatagram.ConvertStringToBytes(message);
            var datagram = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                DataBytes = dataToSend,
                DataLength = dataToSend.Length,
                SessionId = sessionId
            };

            if (customizer != null)
            {
                customizer.Invoke(_kumasiEndpoint, datagram);
            }

            var startTime = DateTime.UtcNow;
            var initId = _accraEndpoint.RequestSend(_kumasiAddr, datagram, null,
                expectCallback ? CreateSendCallback(null) : null);
            var logicalThreadIds = await WaitForAllLogicalThreads(20);

            // Now run assertions on logs
            var testLogs = GetValidatedTestLogs();

            // test that getting related logical threads will give same result as Wait did.
            var actualLogicalThreadIds = GetRelatedLogicalThreadIds(testLogs, initId);
            Assert.Equal(logicalThreadIds, actualLogicalThreadIds);

            AssertSendReceiveLogs(true, sendConfig, transmissionConfig,
                datagram, expectCallback, expectedCallbackEx,
                expectReceives, expectCompleteInitCall, skipReceiveCallExpectation,
                expectedErrorLogPositions, _accraAddr, testLogs, logicalThreadIds, startTime);
        }

        private void AssertSendReceiveLogs(bool expectSends, MemoryNetworkApi.SendConfig sendConfig,
            MemoryNetworkApi.TransmissionConfig transmissionConfig,
            ProtocolDatagram datagram, bool expectCallback,
            string expectedCallbackEx, bool expectReceives,
            bool expectCompleteInitCall, bool skipReceiveCallExpectation,
            string[] expectedErrorLogPositions, GenericNetworkIdentifier expectedRemoteEndpoint, List<TestLogRecord> testLogs,
            List<string> logicalThreadIds, DateTime startTime)
        {
            AssertAbsenceOfSendReceiveErrors(testLogs, expectedErrorLogPositions);
            if (expectedErrorLogPositions != null && expectedErrorLogPositions.Length > 0)
            {
                Assert.NotEmpty(testLogs.Where(x => expectedErrorLogPositions.Contains(x.GetLogPositionId())));
            }

            LogNavigator<TestLogRecord> logNavigator;
            TestLogRecord result;
            long sendDelayMillis = 0;

            Assert.True(logicalThreadIds.Count > 0);

            if (expectSends)
            {
                var sendThreadId = logicalThreadIds[0];

                logNavigator = new LogNavigator<TestLogRecord>(testLogs);

                if (sendConfig != null)
                {
                    sendDelayMillis = sendConfig.Delay;
                }
                if (sendDelayMillis < 0) sendDelayMillis = 0;

                // test send callback invocation
                if (expectCallback)
                {
                    result = logNavigator.Next(x => x.GetLogPositionId() == "e870904c-f035-4c5d-99e7-2c4534f4c152" &&
                        x.GetCurrentLogicalThreadId() == sendThreadId);
                    Assert.NotNull(result);
                    var actualEx = result.GetStrProp(LogDataKeySendException);
                    if (expectedCallbackEx == null)
                    {
                        Assert.Null(actualEx);
                    }
                    else
                    {
                        Assert.Contains(expectedCallbackEx, actualEx);
                    }

                    // check time taken for callback to be called
                    var stopTimeEpoch = result.GetIntProp(LogDataKeyTimestamp).Value;
                    var actualStopTime = DateTimeOffset.FromUnixTimeMilliseconds(stopTimeEpoch);
                    var expectedStopTime = startTime.AddMilliseconds(sendDelayMillis);
                    // allow some secs tolerance in comparison using 
                    // time differences observed in failed/flaky test results.
                    Assert.Equal(expectedStopTime, actualStopTime.UtcDateTime, TimeSpan.FromSeconds(2));
                }
                result = logNavigator.Next(x => x.GetLogPositionId() == "e870904c-f035-4c5d-99e7-2c4534f4c152" &&
                    x.GetCurrentLogicalThreadId() == sendThreadId);
                Assert.Null(result);

            }

            if (expectReceives)
            {
                // reset log navigator since ordering of callback and complete init is not deterministic
                logNavigator = new LogNavigator<TestLogRecord>(testLogs);

                List<string> receiveThreadIds;
                if (expectSends)
                {
                    var sendThreadId = logicalThreadIds[0];
                    var firstLevelIds = testLogs.Where(
                                x => x.GetStrProp(CustomLogEvent.LogDataKeyNewLogicalThreadId) != null &&
                                x.GetCurrentLogicalThreadId() == sendThreadId)
                        .Select(x => x.GetStrProp(CustomLogEvent.LogDataKeyNewLogicalThreadId)).ToList();
                    // leave out all but the terminal threads.
                    receiveThreadIds = logicalThreadIds.Skip(1).Where(x => !firstLevelIds.Contains(x)).ToList();
                }
                else
                {
                    receiveThreadIds = logicalThreadIds;
                }
                var expectedReceiveThreadCount = 1;
                if (transmissionConfig?.Delays != null)
                {
                    expectedReceiveThreadCount = transmissionConfig.Delays.Length;
                }
                Assert.Equal(expectedReceiveThreadCount, receiveThreadIds.Count);

                // test session handler factory usage.
                if (expectCompleteInitCall)
                {
                    result = logNavigator.Next(x => x.GetLogPositionId() == "3f4f66e2-dafc-4c79-aa42-6f988a337d78" &&
                        receiveThreadIds.Contains(x.GetCurrentLogicalThreadId()));
                    Assert.NotNull(result);
                    var actualConfiguredForSend = (bool)result.ParsedProperties[LogDataKeyConfiguredForSendOpen];
                    Assert.False(actualConfiguredForSend);
                }
                result = logNavigator.Next(x => x.GetLogPositionId() == "3f4f66e2-dafc-4c79-aa42-6f988a337d78" &&
                    receiveThreadIds.Contains(x.GetCurrentLogicalThreadId()));
                Assert.Null(result);

                // test calls to session handler receive method.
                if (!skipReceiveCallExpectation)
                {
                    var seenReceivedThreadIds = new List<string>();
                    while (seenReceivedThreadIds.Count < receiveThreadIds.Count)
                    {
                        result = logNavigator.Next(x => x.GetLogPositionId() == "30d12d3a-11b1-4761-b4b7-8a4261b1dd9c" &&
                            receiveThreadIds.Contains(x.GetCurrentLogicalThreadId()));
                        Assert.NotNull(result);
                        var newReceivedThreadId = result.GetCurrentLogicalThreadId();
                        Assert.DoesNotContain(newReceivedThreadId, seenReceivedThreadIds);
                        seenReceivedThreadIds.Add(newReceivedThreadId);

                        // assert datagram is received as expected.
                        string expectedDatagramRepr = "" + datagram.GetHashCode();
                        var actualDatagramRepr = result.GetStrProp(LogDataKeyDatagramHashCode);
                        if (sendConfig != null && sendConfig.SerializeDatagram)
                        {
                            var datagramBytes = datagram.ToRawDatagram();
                            expectedDatagramRepr = ProtocolDatagram.ConvertBytesToHex(datagramBytes, 0,
                                datagramBytes.Length);
                            actualDatagramRepr = result.GetStrProp(LogDataKeySerializedDatagram);
                        }
                        Assert.Equal(expectedDatagramRepr, actualDatagramRepr);
                        var actualSessionHandlerId = result.GetStrProp(CustomLogEvent.LogDataKeySessionId);
                        Assert.Equal(datagram.SessionId, actualSessionHandlerId);
                        var actualRemoteEndpoint = result.GetStrProp(LogDataKeyRemoteEndpoint);
                        Assert.Equal(expectedRemoteEndpoint.ToString(), actualRemoteEndpoint);

                        // test that delay is as expected.
                        if (expectSends)
                        {
                            var stopTimeEpoch = result.GetIntProp(LogDataKeyTimestamp).Value;
                            var actualStopTime = DateTimeOffset.FromUnixTimeMilliseconds(stopTimeEpoch);
                            
                            // fetch delay from grandparent of received thread.
                            var resultWithDelay = testLogs.Single(
                                x => x.GetStrProp(CustomLogEvent.LogDataKeyNewLogicalThreadId) == newReceivedThreadId);
                            resultWithDelay = testLogs.Single(
                                x => x.GetStrProp(CustomLogEvent.LogDataKeyNewLogicalThreadId) == 
                                    resultWithDelay.GetCurrentLogicalThreadId());

                            long delay = resultWithDelay.GetIntProp(MemoryNetworkApi.LogDataKeyDelay).Value;
                            if (delay < 0) delay = 0;
                            var expectedStopTime = startTime.AddMilliseconds(sendDelayMillis + delay);
                            // allow some secs tolerance in comparison using 
                            // time differences observed in failed/flaky test results.
                            Assert.Equal(expectedStopTime, actualStopTime.UtcDateTime, TimeSpan.FromSeconds(2));
                        }
                    }
                }

                result = logNavigator.Next(x => x.GetLogPositionId() == "30d12d3a-11b1-4761-b4b7-8a4261b1dd9c" &&
                    receiveThreadIds.Contains(x.GetCurrentLogicalThreadId()));
                Assert.Null(result);
            }
        }

        public static List<object[]> CreateTestSendReceiveData()
        {
            var testData = new List<object[]>();

            // test that null being passed for callback works alright.
            Action<MemoryNetworkApi, ProtocolDatagram> customizer = null;
            MemoryNetworkApi.SendConfig sendConfig = new MemoryNetworkApi.SendConfig
            {
                Delay = 1500
            };
            MemoryNetworkApi.TransmissionConfig transmissionConfig = null;
            string message = "hello";
            string sessionId = ProtocolDatagram.GenerateSessionId();
            bool expectCallback = false;
            string expectedCallbackEx = null;
            bool expectReceives = true;
            bool expectCompleteInitCall = true;
            bool skipReceiveCallExpectation = false;
            string[] errorLogPositionsToSkip = null;
            testData.Add(new object[] { customizer, sendConfig, transmissionConfig, message, sessionId,
                expectCallback, expectedCallbackEx, expectReceives, expectCompleteInitCall,
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            // test error in sending
            customizer = null;
            sendConfig = new MemoryNetworkApi.SendConfig
            {
                Delay = 1500,
                Error = new ArgumentOutOfRangeException()
            };
            transmissionConfig = null;
            message = "see";
            sessionId = ProtocolDatagram.GenerateSessionId();
            expectCallback = true;
            expectedCallbackEx = sendConfig.Error.GetType().Name;
            expectReceives = false;
            expectCompleteInitCall = false;
            skipReceiveCallExpectation = false;
            errorLogPositionsToSkip = new string[] { "1b554af7-6b87-448a-af9c-103d9c676030" };
            testData.Add(new object[] { customizer, sendConfig, transmissionConfig, message, sessionId,
                expectCallback, expectedCallbackEx, expectReceives, expectCompleteInitCall,
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            // test normal send
            customizer = null;
            sendConfig = null;
            transmissionConfig = null;
            message = "success";
            sessionId = ProtocolDatagram.GenerateSessionId();
            expectCallback = true;
            expectedCallbackEx = null;
            expectReceives = true;
            expectCompleteInitCall = true;
            skipReceiveCallExpectation = false;
            errorLogPositionsToSkip = null;
            testData.Add(new object[] { customizer, sendConfig, transmissionConfig, message, sessionId,
                expectCallback, expectedCallbackEx, expectReceives, expectCompleteInitCall,
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            // test detection of serialization error
            customizer = null;
            sendConfig = new MemoryNetworkApi.SendConfig
            {
                SerializeDatagram = true
            };
            transmissionConfig = null;
            message = "expect fail";
            sessionId = null; // should make datagram serialization fail.
            expectCallback = true;
            expectedCallbackEx = typeof(Exception).Name;
            expectReceives = false;
            expectCompleteInitCall = false;
            skipReceiveCallExpectation = false;
            errorLogPositionsToSkip = new string[] { "1b554af7-6b87-448a-af9c-103d9c676030" };
            testData.Add(new object[] { customizer, sendConfig, transmissionConfig, message, sessionId,
                expectCallback, expectedCallbackEx, expectReceives, expectCompleteInitCall,
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            // test transmission config.
            customizer = null;
            sendConfig = new MemoryNetworkApi.SendConfig
            {
                SerializeDatagram = true,
                Delay = 2000,
            };
            transmissionConfig = new MemoryNetworkApi.TransmissionConfig
            {
                Delays = new int[] { 4000, 0, -1, 2500 }
            };
            message = "transmitting...";
            sessionId = ProtocolDatagram.GenerateSessionId();
            expectCallback = true;
            expectedCallbackEx = null;
            expectReceives = true;
            expectCompleteInitCall = true;
            skipReceiveCallExpectation = false;
            errorLogPositionsToSkip = null;
            testData.Add(new object[] { customizer, sendConfig, transmissionConfig, message, sessionId,
                expectCallback, expectedCallbackEx, expectReceives, expectCompleteInitCall, 
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            // test shutdown.
            customizer = (networkApi, _) =>
            {
                networkApi.ShutdownAsync(0);
            };
            sendConfig = null;
            transmissionConfig = null;
            message = "...";
            sessionId = ProtocolDatagram.GenerateSessionId();
            expectCallback = true;
            expectedCallbackEx = null;
            expectReceives = true;
            expectCompleteInitCall = false;
            skipReceiveCallExpectation = true;
            errorLogPositionsToSkip = new string[] { "823df166-6430-4bcf-ae8f-2cb4c5e77cb1" };
            testData.Add(new object[] { customizer, sendConfig, transmissionConfig, message, sessionId,
                expectCallback, expectedCallbackEx, expectReceives, expectCompleteInitCall,
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            // test invalid datagram opcode.
            customizer = (_, datagram) =>
            {
                datagram.OpCode = ProtocolDatagram.OpCodeRestart;
            };
            sendConfig = null;
            transmissionConfig = null;
            message = "...";
            sessionId = ProtocolDatagram.GenerateSessionId();
            expectCallback = true;
            expectedCallbackEx = null;
            expectReceives = true;
            expectCompleteInitCall = false;
            skipReceiveCallExpectation = true;
            errorLogPositionsToSkip = new string[] { "74566405-9d14-489f-9dbd-0c9b3e0e3e67" };
            testData.Add(new object[] { customizer, sendConfig, transmissionConfig, message, sessionId,
                expectCallback, expectedCallbackEx, expectReceives, expectCompleteInitCall,
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            // test that exceptions are caught during receive processing.
            customizer = (networkApi, _) =>
            {
                networkApi.SessionHandlerFactory = null;
            };
            sendConfig = null;
            transmissionConfig = null;
            message = "...";
            sessionId = ProtocolDatagram.GenerateSessionId();
            expectCallback = true;
            expectedCallbackEx = null;
            expectReceives = true;
            expectCompleteInitCall = false;
            skipReceiveCallExpectation = true;
            errorLogPositionsToSkip = new string[] { "c42673d8-a1d1-4cb1-b9c1-a5369bedff64" };
            testData.Add(new object[] { customizer, sendConfig, transmissionConfig, message, sessionId,
                expectCallback, expectedCallbackEx, expectReceives, expectCompleteInitCall,
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            // test empty transmission config delays
            customizer = null;
            sendConfig = null;
            transmissionConfig = new MemoryNetworkApi.TransmissionConfig
            {
                Delays = null
            };
            message = "...";
            sessionId = ProtocolDatagram.GenerateSessionId();
            expectCallback = true;
            expectedCallbackEx = null;
            expectReceives = false;
            expectCompleteInitCall = false;
            skipReceiveCallExpectation = true;
            errorLogPositionsToSkip = null;
            testData.Add(new object[] { customizer, sendConfig, transmissionConfig, message, sessionId,
                expectCallback, expectedCallbackEx, expectReceives, expectCompleteInitCall,
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            // test empty transmission config delays again
            customizer = null;
            sendConfig = null;
            transmissionConfig = new MemoryNetworkApi.TransmissionConfig
            {
                Delays = new int[0]
            };
            message = "...";
            sessionId = ProtocolDatagram.GenerateSessionId();
            expectCallback = true;
            expectedCallbackEx = null;
            expectReceives = false;
            expectCompleteInitCall = false;
            skipReceiveCallExpectation = true;
            errorLogPositionsToSkip = null;
            testData.Add(new object[] { customizer, sendConfig, transmissionConfig, message, sessionId,
                expectCallback, expectedCallbackEx, expectReceives, expectCompleteInitCall,
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            return testData;
        }

        [Theory]
        [MemberData(nameof(CreateTestSendToSelfData))]
        public async Task TestSendToSelf(Action<MemoryNetworkApi, ProtocolDatagram> customizer,
            string message, string sessionId, bool expectCompleteInitCall, bool skipReceiveCallExpectation,
            string[] expectedErrorLogPositions)
        {
            TestDatabase.ResetDb();

            var dataToSend = ProtocolDatagram.ConvertStringToBytes(message);
            var datagram = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeData,
                DataBytes = dataToSend,
                DataLength = dataToSend.Length,
                SessionId = sessionId
            };

            if (customizer != null)
            {
                customizer.Invoke(_kumasiEndpoint, datagram);
            }

            var startTime = DateTime.UtcNow;
            var initId = _kumasiEndpoint.RequestSendToSelf(_accraAddr, datagram);
            var logicalThreadIds = await WaitForAllLogicalThreads(20);

            // Now run assertions on logs
            var testLogs = GetValidatedTestLogs();

            // test that getting related logical threads will give same result as Wait did.
            var actualLogicalThreadIds = GetRelatedLogicalThreadIds(testLogs, initId);
            Assert.Equal(logicalThreadIds, actualLogicalThreadIds);

            AssertSendReceiveLogs(false, null, null,
                datagram, false, null, true, expectCompleteInitCall, skipReceiveCallExpectation,
                expectedErrorLogPositions, _accraAddr, testLogs, logicalThreadIds, startTime);
        }

        public static List<object[]> CreateTestSendToSelfData()
        {
            var testData = new List<object[]>();

            Action<MemoryNetworkApi, ProtocolDatagram> customizer = null;
            string message = "hello";
            string sessionId = ProtocolDatagram.GenerateSessionId();
            bool expectCompleteInitCall = true;
            bool skipReceiveCallExpectation = false;
            string[] errorLogPositionsToSkip = null;
            testData.Add(new object[] { customizer, message, sessionId, expectCompleteInitCall,
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            customizer = null;
            message = "success";
            sessionId = ProtocolDatagram.GenerateSessionId();
            expectCompleteInitCall = true;
            skipReceiveCallExpectation = false;
            errorLogPositionsToSkip = null;
            testData.Add(new object[] { customizer, message, sessionId, expectCompleteInitCall,
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            // test shutdown.
            customizer = (networkApi, _) =>
            {
                networkApi.ShutdownAsync(0);
            };
            message = "...";
            sessionId = ProtocolDatagram.GenerateSessionId();
            expectCompleteInitCall = false;
            skipReceiveCallExpectation = true;
            errorLogPositionsToSkip = new string[] { "823df166-6430-4bcf-ae8f-2cb4c5e77cb1" };
            testData.Add(new object[] { customizer, message, sessionId, expectCompleteInitCall,
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            // test invalid datagram opcode.
            customizer = (_, datagram) =>
            {
                datagram.OpCode = ProtocolDatagram.OpCodeRestart;
            };
            message = "...";
            sessionId = ProtocolDatagram.GenerateSessionId();
            expectCompleteInitCall = false;
            skipReceiveCallExpectation = true;
            errorLogPositionsToSkip = new string[] { "74566405-9d14-489f-9dbd-0c9b3e0e3e67" };
            testData.Add(new object[] { customizer, message, sessionId, expectCompleteInitCall,
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            // test that exceptions are caught during receive processing.
            customizer = (networkApi, _) =>
            {
                networkApi.SessionHandlerFactory = null;
            };
            message = "...";
            sessionId = ProtocolDatagram.GenerateSessionId();
            expectCompleteInitCall = false;
            skipReceiveCallExpectation = true;
            errorLogPositionsToSkip = new string[] { "c42673d8-a1d1-4cb1-b9c1-a5369bedff64" };
            testData.Add(new object[] { customizer, message, sessionId, expectCompleteInitCall,
                skipReceiveCallExpectation, errorLogPositionsToSkip });

            return testData;
        }

        [Fact]
        public async Task TestSendReceiveInParallel()
        {
            var addrA = new GenericNetworkIdentifier { HostName = "DestA" };
            var endpointA = new MemoryNetworkApi
            {
                LocalEndpoint = addrA,
                SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(TestSessionHandler)),
            };
            endpointA.TransmissionBehaviour = new MemoryNetworkApi.DefaultTransmissionBehaviour
            {
                Config = new MemoryNetworkApi.TransmissionConfig
                {
                    Delays = new int[] { 700 }
                }
            };

            var addrB = new GenericNetworkIdentifier { HostName = "DestB" };
            var endpointB = new MemoryNetworkApi
            {
                LocalEndpoint = addrB,
                SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(TestSessionHandler)),
            };
            endpointB.SendBehaviour = new MemoryNetworkApi.DefaultSendBehaviour
            {
                Config = new MemoryNetworkApi.SendConfig
                {
                    Delay = 1600
                }
            };

            var addrC = new GenericNetworkIdentifier { HostName = "DestC" };
            var endpointC = new MemoryNetworkApi
            {
                LocalEndpoint = addrC,
                SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(TestSessionHandler)),
            };
            endpointC.SendBehaviour = new MemoryNetworkApi.DefaultSendBehaviour
            {
                Config = new MemoryNetworkApi.SendConfig
                {
                    SerializeDatagram = true,
                    Delay = 1000
                }
            };
            endpointC.TransmissionBehaviour = new MemoryNetworkApi.DefaultTransmissionBehaviour
            {
                Config = new MemoryNetworkApi.TransmissionConfig
                {
                    Delays = new int[] { 1500, 3500 }
                }
            };

            // now connect all to the other, except that A is not connected to C.
            endpointA.ConnectedNetworks.Add(addrB, endpointB);
            endpointB.ConnectedNetworks.Add(addrA, endpointA);
            endpointB.ConnectedNetworks.Add(addrC, endpointC);
            endpointC.ConnectedNetworks.Add(addrB, endpointB);

            TestDatabase.ResetDb();

            var randGen = new Random();

            // perform only success cases right now, to get at least 1 session id for each connection.
            var sendRequests = new List<SendRequestContext>();
            var existingSessionIds = new List<Tuple<MemoryNetworkApi, GenericNetworkIdentifier, string>>();
            int initialSuccessCount = 10;
            for (int i = 0; i < initialSuccessCount; i++)
            {
                var randMessage = randGen.Next().ToString();
                var dataToSend = ProtocolDatagram.ConvertStringToBytes(randMessage);
                var sessionId = ProtocolDatagram.GenerateSessionId();
                var datagram = new ProtocolDatagram
                {
                    OpCode = ProtocolDatagram.OpCodeData,
                    DataBytes = dataToSend,
                    DataLength = dataToSend.Length,
                    SessionId = sessionId
                };

                MemoryNetworkApi srcEndpoint;
                GenericNetworkIdentifier destAddr;
                switch (i % 4)
                {
                    case 0:
                        srcEndpoint = endpointA;
                        destAddr = addrB;
                        break;
                    case 1:
                        srcEndpoint = endpointB;
                        destAddr = addrA;
                        break;
                    case 2:
                        srcEndpoint = endpointB;
                        destAddr = addrC;
                        break;
                    default:
                        srcEndpoint = endpointC;
                        destAddr = addrB;
                        break;
                }

                existingSessionIds.Add(Tuple.Create(srcEndpoint, destAddr, datagram.SessionId));

                var sendConfig = (MemoryNetworkApi.DefaultSendBehaviour)srcEndpoint.SendBehaviour;
                var transmissionConfig = (MemoryNetworkApi.DefaultTransmissionBehaviour)srcEndpoint.TransmissionBehaviour;
                string expectedCallbackEx = null;
                var expectReceives = true;
                var expectCompleteInitCall = true;
                var skipReceiveCallExpectation = false;
                string[] expectedErrorLogPositions = null;

                var startTime = DateTime.UtcNow;
                var initId = srcEndpoint.RequestSend(destAddr, datagram, null, CreateSendCallback(null));
                var requestContext = new SendRequestContext
                {
                    InitId = initId,
                    SendConfig = sendConfig?.Config,
                    TransmissionConfig = transmissionConfig?.Config,
                    Datagram = datagram,
                    ExpectedCallbackEx = expectedCallbackEx,
                    ExpectReceives = expectReceives,
                    ExpectCompleteInitCall = expectCompleteInitCall,
                    SkipReceiveCallExpectation = skipReceiveCallExpectation,
                    ExpectedErrorLogPositions = expectedErrorLogPositions,
                    ExpectedRemoteEndpoint = srcEndpoint.LocalEndpoint,
                    StartTime = startTime
                };
                sendRequests.Add(requestContext);
            }

            await WaitForAllLogicalThreads(10 * initialSuccessCount);
            var testLogs = GetValidatedTestLogs();

            foreach (var sendRequest in sendRequests)
            {
                var specificLogicalThreadIds = GetRelatedLogicalThreadIds(testLogs, sendRequest.InitId);
                var specificLogs = testLogs.Where(x => 
                    specificLogicalThreadIds.Contains(x.GetCurrentLogicalThreadId())).ToList();

                try
                {
                    AssertSendReceiveLogs(true, sendRequest.SendConfig, sendRequest.TransmissionConfig,
                        sendRequest.Datagram, true,
                        sendRequest.ExpectedCallbackEx,
                        sendRequest.ExpectReceives, sendRequest.ExpectCompleteInitCall,
                        sendRequest.SkipReceiveCallExpectation,
                        sendRequest.ExpectedErrorLogPositions, sendRequest.ExpectedRemoteEndpoint, specificLogs,
                        specificLogicalThreadIds, sendRequest.StartTime);
                }
                catch (Exception ex)
                {
                    CustomLoggerFacade.WriteToStdOut(true,
                        $"Initial parallel send/receive testing in MemoryNetworkApiTest failed for: " +
                        $"{JsonConvert.SerializeObject(sendRequest)}", ex);
                    throw;
                }
            }

            // now perform parallel tests of more variety.

            // test mainly success scenarios.
            // test reuse session id case.
            // test invalid op code case
            // test send exception case - with A not connected to C.
            // test receive exception case - with known value for trace id option.

            TestDatabase.ResetDb();

            // extra test endpoint to test empty delay list.
            var addrD = new GenericNetworkIdentifier { HostName = "DestD" };
            var endpointD = new MemoryNetworkApi
            {
                LocalEndpoint = addrD,
                SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(TestSessionHandler)),
            };
            endpointD.TransmissionBehaviour = new MemoryNetworkApi.DefaultTransmissionBehaviour
            {
                Config = new MemoryNetworkApi.TransmissionConfig
                {
                    Delays = new int[0]
                }
            };
            endpointD.ConnectedNetworks.Add(addrC, endpointC);

            var parallelTestCount = 40;
            sendRequests.Clear();
            for (int i = 0; i < parallelTestCount; i++)
            {
                var randMessage = randGen.Next().ToString();
                var dataToSend = ProtocolDatagram.ConvertStringToBytes(randMessage);
                var sessionId = ProtocolDatagram.GenerateSessionId();
                var datagram = new ProtocolDatagram
                {
                    OpCode = ProtocolDatagram.OpCodeData,
                    DataBytes = dataToSend,
                    DataLength = dataToSend.Length,
                    SessionId = sessionId
                };

                MemoryNetworkApi srcEndpoint;
                switch (randGen.Next(4))
                {
                    case 0:
                        srcEndpoint = endpointA;
                        break;
                    case 1:
                        srcEndpoint = endpointB;
                        break;
                    case 3:
                        srcEndpoint = endpointD;
                        break;
                    default:
                        srcEndpoint = endpointC;
                        break;
                }
                GenericNetworkIdentifier destAddr;
                if (srcEndpoint == endpointA)
                {
                    destAddr = randGen.NextDouble() < 0.5 ? addrB : addrC;
                }
                else if (srcEndpoint == endpointB)
                {
                    destAddr = randGen.NextDouble() < 0.5 ? addrA : addrC;
                }
                else if (srcEndpoint == endpointD)
                {
                    destAddr = addrC;
                }
                else
                {
                    // assume C.
                    destAddr = randGen.NextDouble() < 0.5 ? addrA : addrB;
                }
                
                bool isSendExceptionCase = false, isReceiveExceptionCase = false;
                bool isInvalidOpcodeCase = false, callbackExceptionCase = false;
                if (srcEndpoint == endpointA && destAddr == addrC ||
                    srcEndpoint == endpointC && destAddr == addrA)
                {
                    isSendExceptionCase = true;
                }
                else if (srcEndpoint == endpointD)
                {
                    callbackExceptionCase = randGen.NextDouble() < 0.5;
                }
                else
                {
                    switch (randGen.Next(6))
                    {
                        case 0:
                            isReceiveExceptionCase = true;
                            break;
                        case 1:
                            isInvalidOpcodeCase = true;
                            break;
                        case 2:
                            callbackExceptionCase = true;
                            break;
                        default:
                            break;
                    }
                }

                bool reuseSessionId = randGen.NextDouble() < 0.5;

                var sendConfig = (MemoryNetworkApi.DefaultSendBehaviour)srcEndpoint.SendBehaviour;
                var transmissionConfig = (MemoryNetworkApi.DefaultTransmissionBehaviour)srcEndpoint.TransmissionBehaviour;
                string expectedCallbackEx = null;
                var expectReceives = srcEndpoint != endpointD;
                var expectCompleteInitCall = true;
                var skipReceiveCallExpectation = false;
                string[] expectedErrorLogPositions = null;

                if (reuseSessionId)
                {
                    foreach (var existingSessionId in existingSessionIds)
                    {
                        if (existingSessionId.Item1 == srcEndpoint && existingSessionId.Item2 == destAddr)
                        {
                            datagram.SessionId = existingSessionId.Item3;
                            expectCompleteInitCall = false;
                            break;
                        }
                    }
                }

                if (isInvalidOpcodeCase)
                {
                    datagram.OpCode = randGen.NextDouble() < 0.5 ? ProtocolDatagram.OpCodeRestart :
                        ProtocolDatagram.OpCodeShutdown;
                    expectedErrorLogPositions = new string[] { "74566405-9d14-489f-9dbd-0c9b3e0e3e67" };
                    expectCompleteInitCall = false;
                    skipReceiveCallExpectation = true;
                }
                else if (isSendExceptionCase)
                {
                    expectedCallbackEx = typeof(Exception).Name;
                    expectReceives = false;
                    expectedErrorLogPositions = new string[] { "1b554af7-6b87-448a-af9c-103d9c676030" };
                }
                else if (isReceiveExceptionCase)
                {
                    datagram.Options = new ProtocolDatagramOptions();
                    datagram.Options.TraceId = ErrorTraceValue;
                    expectedErrorLogPositions = new string[] { "c42673d8-a1d1-4cb1-b9c1-a5369bedff64" };
                }
                else if (callbackExceptionCase)
                {
                    expectedErrorLogPositions = new string[] { "1b554af7-6b87-448a-af9c-103d9c676030" };
                }

                var startTime = DateTime.UtcNow;
                var initId = srcEndpoint.RequestSend(destAddr, datagram, null, 
                    CreateSendCallback(callbackExceptionCase ? new Exception("test") : null));
                var requestContext = new SendRequestContext
                {
                    InitId = initId,
                    SendConfig = sendConfig?.Config,
                    TransmissionConfig = transmissionConfig?.Config,
                    Datagram = datagram,
                    ExpectedCallbackEx = expectedCallbackEx,
                    ExpectReceives = expectReceives,
                    ExpectCompleteInitCall = expectCompleteInitCall,
                    SkipReceiveCallExpectation = skipReceiveCallExpectation,
                    ExpectedErrorLogPositions = expectedErrorLogPositions,
                    ExpectedRemoteEndpoint = srcEndpoint.LocalEndpoint,
                    StartTime = startTime
                };
                sendRequests.Add(requestContext);
            }

            await WaitForAllLogicalThreads(10 * parallelTestCount);
            testLogs = GetValidatedTestLogs();

            foreach (var sendRequest in sendRequests)
            {
                var specificLogicalThreadIds = GetRelatedLogicalThreadIds(testLogs, sendRequest.InitId);
                var specificLogs = testLogs.Where(x =>
                    specificLogicalThreadIds.Contains(x.GetCurrentLogicalThreadId())).ToList();

                try
                {
                    AssertSendReceiveLogs(true, sendRequest.SendConfig, sendRequest.TransmissionConfig,
                        sendRequest.Datagram, true, 
                        sendRequest.ExpectedCallbackEx,
                        sendRequest.ExpectReceives, sendRequest.ExpectCompleteInitCall,
                        sendRequest.SkipReceiveCallExpectation,
                        sendRequest.ExpectedErrorLogPositions, sendRequest.ExpectedRemoteEndpoint, specificLogs,
                        specificLogicalThreadIds, sendRequest.StartTime);
                }
                catch (Exception ex)
                {
                    CustomLoggerFacade.WriteToStdOut(true,
                        $"Extended parallel send/receive testing in MemoryNetworkApiTest failed for: " +
                        $"{JsonConvert.SerializeObject(sendRequest)}", ex);
                    throw;
                }
            }
        }

        [Fact]
        public async Task TestSessionDispose()
        {
            // Test normal dispose call in which endpoint and session id exist.
            var sessionId = Guid.NewGuid().ToString("n");
            var openPromise = _accraEndpoint.OpenSessionAsync(_kumasiAddr, sessionId,
                null);

            TestDatabase.ResetDb();
            _accraEndpoint.RequestSessionDispose(_kumasiAddr, sessionId,
                new ProtocolOperationException(false, ProtocolOperationException.ErrorCodeNormalClose));
            var logicalThreadIds = await WaitForAllLogicalThreads(2);
            Assert.Single(logicalThreadIds);

            var testLogs = GetValidatedTestLogs();
            var logNavigator = new LogNavigator<TestLogRecord>(testLogs);
            // check that FinalizeDisposeAsync is called on session handler.
            var record = logNavigator.Next(
                rec => rec.Properties.Contains("db8ca05b-5bd9-48e1-a74d-cb0542cd1587"));
            Assert.NotNull(record);
            // check that no exception is logged.
            record = logNavigator.Next(
                rec => rec.Properties.Contains("86a662a4-c098-4053-ac26-32b984079419"));
            Assert.Null(record);

            // test disposing non-existent session id
            TestDatabase.ResetDb();
            _accraEndpoint.RequestSessionDispose(_kumasiAddr, sessionId,
                new ProtocolOperationException(false, ProtocolOperationException.ErrorCodeNormalClose));
            logicalThreadIds = await WaitForAllLogicalThreads(2);
            Assert.Single(logicalThreadIds);

            testLogs = GetValidatedTestLogs();
            logNavigator = new LogNavigator<TestLogRecord>(testLogs);
            // check that FinalizeDisposeAsync is NOT called again on session handler.
            record = logNavigator.Next(
                rec => rec.Properties.Contains("db8ca05b-5bd9-48e1-a74d-cb0542cd1587"));
            Assert.Null(record);
            // check that no exception is logged.
            record = logNavigator.Next(
                rec => rec.Properties.Contains("86a662a4-c098-4053-ac26-32b984079419"));
            Assert.Null(record);

            // test that FinalizeDisposeAsync is not called in erroneous call.
            TestDatabase.ResetDb();
            _accraEndpoint.RequestSessionDispose(null, sessionId,
                new ProtocolOperationException(false, ProtocolOperationException.ErrorCodeNormalClose));
            logicalThreadIds = await WaitForAllLogicalThreads(2);
            Assert.Single(logicalThreadIds);

            testLogs = GetValidatedTestLogs();
            logNavigator = new LogNavigator<TestLogRecord>(testLogs);
            record = logNavigator.Next(
                rec => rec.Properties.Contains("db8ca05b-5bd9-48e1-a74d-cb0542cd1587"));
            Assert.Null(record);

            // test that FinalizeDisposeAsync swallows exceptions from session handler.
            sessionId = Guid.NewGuid().ToString("n");
            openPromise = _accraEndpoint.OpenSessionAsync(_kumasiAddr, sessionId,
                null);
            TestDatabase.ResetDb();
            _accraEndpoint.RequestSessionDispose(_kumasiAddr, sessionId, null);
            logicalThreadIds = await WaitForAllLogicalThreads(2);
            Assert.Single(logicalThreadIds);

            testLogs = GetValidatedTestLogs();
            logNavigator = new LogNavigator<TestLogRecord>(testLogs);
            record = logNavigator.Next(
                rec => rec.Properties.Contains("db8ca05b-5bd9-48e1-a74d-cb0542cd1587"));
            Assert.NotNull(record);
            // check that exception is logged.
            record = logNavigator.Next(
                rec => rec.Properties.Contains("86a662a4-c098-4053-ac26-32b984079419"));
            Assert.NotNull(record);
        }

        class TestSessionHandler : ISessionHandler
        {
            public TestSessionHandler() :
                this(true)
            { }

            public TestSessionHandler(bool issueLog)
            {
                if (!issueLog) return;

                CustomLoggerFacade.TestLog(() => new CustomLogEvent(GetType(), "Creating new instance")
                    .AddProperty(CustomLogEvent.LogDataKeyLogPositionId,
                        "f8f6c939-09e2-4f8d-9b2b-fd25ee4ead9e"));
            }

            public void CompleteInit(string sessionId, bool configureForSendOpen,
                AbstractNetworkApi networkApi, GenericNetworkIdentifier remoteEndpoint)
            {
                CustomLoggerFacade.TestLog(() => new CustomLogEvent(GetType(), "CompleteInit() called")
                    .AddProperty(CustomLogEvent.LogDataKeyLogPositionId,
                        "3f4f66e2-dafc-4c79-aa42-6f988a337d78")
                    .AddProperty(CustomLogEvent.LogDataKeyCurrentLogicalThreadId,
                        networkApi.PromiseApi.CurrentLogicalThreadId)
                    .AddProperty(LogDataKeyConfiguredForSendOpen, configureForSendOpen));
                NetworkApi = networkApi;
                RemoteEndpoint = remoteEndpoint;
                SessionId = sessionId;
                ConfiguredForSendOpen = configureForSendOpen;
            }

            public AbstractNetworkApi NetworkApi { get; private set; }

            public GenericNetworkIdentifier RemoteEndpoint { get; private set; }

            public string SessionId { get; private set; }
            public bool ConfiguredForSendOpen { get; private set; }

            public int MaxWindowSize { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int MaxRemoteWindowSize { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int MaxRetryCount { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int MaxRetryPeriod { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int OpenTimeout { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int IdleTimeout { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int MinRemoteIdleTimeout { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int MaxRemoteIdleTimeout { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public double FireAndForgetSendProbability { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int EnquireLinkInterval { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public Func<int, int> EnquireLinkIntervalAlgorithm { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public Action<ISessionHandler, ProtocolDatagram> DatagramDiscardedHandler { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public Action<ISessionHandler, ProtocolMessage> MessageReceivedHandler { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public Action<ISessionHandler, ProtocolOperationException> SessionDisposingHandler { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public Action<ISessionHandler, ProtocolOperationException> SessionDisposedHandler { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public Action<ISessionHandler, ProtocolOperationException> ReceiveErrorHandler { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public Action<ISessionHandler, ProtocolOperationException> SendErrorHandler { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public Action<ISessionHandler, int> EnquireLinkTimerFiredHandler { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public Action<ISessionHandler> OpenSuccessHandler { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public AbstractPromise<VoidType> CloseAsync()
            {
                throw new NotImplementedException();
            }

            public AbstractPromise<VoidType> CloseAsync(int errorCode)
            {
                throw new NotImplementedException();
            }

            public AbstractPromise<VoidType> FinaliseDisposeAsync(ProtocolOperationException cause)
            {
                CustomLoggerFacade.TestLog(() => new CustomLogEvent(GetType(), "FinaliseDisposeAsync() called")
                       .AddProperty(CustomLogEvent.LogDataKeyLogPositionId,
                           "db8ca05b-5bd9-48e1-a74d-cb0542cd1587"));
                if (cause == null)
                {
                    throw new ArgumentNullException(nameof(cause));
                }
                return NetworkApi.PromiseApi.CompletedPromise();
            }

            public AbstractPromise<VoidType> ProcessOpenAsync()
            {
                CustomLoggerFacade.TestLog(() => new CustomLogEvent(GetType(), "ProcessOpenAsync() called")
                        .AddProperty(CustomLogEvent.LogDataKeyLogPositionId,
                           "12f2f4e3-af83-460f-8d46-0ac0fc9d95fe"));
                return NetworkApi.PromiseApi.CompletedPromise();
            }

            public AbstractPromise<VoidType> ProcessReceiveAsync(ProtocolDatagram datagram)
            {
                var datagramBytes = datagram.ToRawDatagram();
                var serializedDatagram = ProtocolDatagram.ConvertBytesToHex(datagramBytes, 0,
                    datagramBytes.Length);
                CustomLoggerFacade.TestLog(() => new CustomLogEvent(GetType(), "ProcessReceiveAsync() called")
                        .AddProperty(CustomLogEvent.LogDataKeyLogPositionId,
                           "30d12d3a-11b1-4761-b4b7-8a4261b1dd9c")
                        .AddProperty(CustomLogEvent.LogDataKeyCurrentLogicalThreadId,
                            NetworkApi.PromiseApi.CurrentLogicalThreadId)
                        .AddProperty(LogDataKeySerializedDatagram,
                            serializedDatagram)
                        .AddProperty(LogDataKeyDatagramHashCode, "" + datagram.GetHashCode())
                        .AddProperty(LogDataKeyTimestamp, ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds())
                        .AddProperty(CustomLogEvent.LogDataKeySessionId, SessionId)
                        .AddProperty(LogDataKeyRemoteEndpoint, RemoteEndpoint.ToString()));
                if (datagram.Options?.TraceId == ErrorTraceValue)
                {
                    throw new Exception(ErrorTraceValue);
                }
                return NetworkApi.PromiseApi.CompletedPromise();
            }

            public AbstractPromise<VoidType> SendAsync(ProtocolMessage message)
            {
                throw new NotImplementedException();
            }

            public AbstractPromise<bool> SendWithoutAckAsync(ProtocolMessage message)
            {
                throw new NotImplementedException();
            }
        }

        private Action<Exception> CreateSendCallback(Exception exToThrow)
        {
            Action<Exception> testCb = ex =>
            {
                CustomLoggerFacade.TestLog(() => new CustomLogEvent(GetType(), "RequestSend callback called")
                    .AddProperty(CustomLogEvent.LogDataKeyLogPositionId,
                        "e870904c-f035-4c5d-99e7-2c4534f4c152")
                    .AddProperty(CustomLogEvent.LogDataKeyCurrentLogicalThreadId,
                        _accraEndpoint.PromiseApi.CurrentLogicalThreadId)
                    .AddProperty(LogDataKeySendException, ex?.ToString())
                    .AddProperty(LogDataKeyTimestamp, ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds()));
                if (exToThrow != null) throw exToThrow;
            };
            return testCb;
        }

        private class SendRequestContext
        {
            public Guid InitId { get; set; }
            public MemoryNetworkApi.SendConfig SendConfig { get; set; }
            public MemoryNetworkApi.TransmissionConfig TransmissionConfig { get; set; }
            public ProtocolDatagram Datagram { get; set; }
            public string ExpectedCallbackEx { get; set; }
            public bool ExpectReceives { get; set; }
            public bool ExpectCompleteInitCall { get; set; }
            public bool SkipReceiveCallExpectation { get; set; }
            public string[] ExpectedErrorLogPositions { get; set; }
            public GenericNetworkIdentifier ExpectedRemoteEndpoint { get; set; }
            public DateTime StartTime { get; set; }
        }
    }
}
