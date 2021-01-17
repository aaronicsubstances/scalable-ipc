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
    public class MemoryNetworkApiTest
    {
        internal static readonly string LogDataKeyConfiguredForSend = "configuredForSend";

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
            foreach (var testLog in testLogs)
            {
                if (testLog.ParsedProperties.ContainsKey(CustomLogEvent.LogDataKeyNewLogicalThreadId))
                {
                    newLogicalThreadIds.Add((string)testLog.ParsedProperties[
                        CustomLogEvent.LogDataKeyNewLogicalThreadId]);
                    lastId = testLog.Id;
                }
            }
            if (newLogicalThreadIds.Count == 0)
            {
                return GetValidatedTestLogs().Where(x => x.ParsedProperties.ContainsKey(
                    CustomLogEvent.LogDataKeyNewLogicalThreadId))
                    .Select(x => (string)x.ParsedProperties[CustomLogEvent.LogDataKeyNewLogicalThreadId])
                    .ToList();
            }
            await Awaitility.WaitAsync(TimeSpan.FromSeconds(waitTimeSecs), () =>
            {
                var testLogs = GetValidatedTestLogs().Where(x => x.Id > lastId);
                foreach (var testLog in testLogs)
                {
                    if (testLog.ParsedProperties.ContainsKey(CustomLogEvent.LogDataKeyEndingLogicalThreadId))
                    {
                        newLogicalThreadIds.Remove((string)testLog.ParsedProperties[
                            CustomLogEvent.LogDataKeyEndingLogicalThreadId]);
                    }
                }
                return newLogicalThreadIds.Count == 0;
            });
            return await WaitForNextRoundOfLogicalThreads(waitTimeSecs, lastId);
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
            Assert.Equal(true, record.ParsedProperties[LogDataKeyConfiguredForSend]);

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
            Assert.Equal(true, record.ParsedProperties[LogDataKeyConfiguredForSend]);

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

            await Assert.ThrowsAnyAsync<Exception>(() =>
            {
                var promise = _accraEndpoint.OpenSessionAsync(_kumasiAddr, null,
                    null);
                return ((DefaultPromise<ISessionHandler>)promise).WrappedTask;
            });

            /*var dataToSend = ProtocolDatagram.ConvertStringToBytes("Hello");
            var message = new ProtocolDatagram
            {
                DataBytes = dataToSend,
                DataLength = dataToSend.Length
            };
            _accraEndpoint.RequestSend(_kumasiAddr, message, null);

            */
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
                new SessionDisposedException(false,ProtocolDatagram.AbortCodeNormalClose));
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
                new SessionDisposedException(false, ProtocolDatagram.AbortCodeNormalClose));
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
                new SessionDisposedException(false, ProtocolDatagram.AbortCodeNormalClose));
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

            public void CompleteInit(string sessionId, bool configureForInitialSend,
                AbstractNetworkApi networkApi, GenericNetworkIdentifier remoteEndpoint)
            {
                CustomLoggerFacade.TestLog(() => new CustomLogEvent(GetType(), "CompleteInit() called")
                    .AddProperty(CustomLogEvent.LogDataKeyLogPositionId,
                        "3f4f66e2-dafc-4c79-aa42-6f988a337d78")
                    .AddProperty(LogDataKeyConfiguredForSend, configureForInitialSend));
                NetworkApi = networkApi;
                RemoteEndpoint = remoteEndpoint;
                SessionId = sessionId;
            }

            public AbstractNetworkApi NetworkApi { get; private set; }

            public GenericNetworkIdentifier RemoteEndpoint { get; private set; }

            public string SessionId { get; private set; }

            public ISessionTaskExecutor TaskExecutor => throw new NotImplementedException();

            public int MaxReceiveWindowSize { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int MaxSendWindowSize { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int MaximumTransferUnitSize { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int MaxRetryCount { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int AckTimeout { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int IdleTimeout { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int MinRemoteIdleTimeout { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int MaxRemoteIdleTimeout { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public AbstractPromise<VoidType> CloseAsync()
            {
                throw new NotImplementedException();
            }

            public AbstractPromise<VoidType> CloseAsync(bool closeGracefully)
            {
                throw new NotImplementedException();
            }

            public AbstractPromise<VoidType> FinaliseDisposeAsync(SessionDisposedException cause)
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

            public AbstractPromise<VoidType> ProcessReceiveAsync(ProtocolDatagram datagram)
            {
                throw new NotImplementedException();
            }

            public AbstractPromise<VoidType> ProcessSendAsync(ProtocolMessage message)
            {
                throw new NotImplementedException();
            }
        }
    }
}
