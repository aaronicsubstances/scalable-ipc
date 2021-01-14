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
    public class MemoryNetworkApiTest
    {
        internal static readonly string LogDataKeySessionId = "sessionId";
        internal static readonly string LogDataKeyNetworkApi = "networkApi";
        internal static readonly string LogDataKeyRemoteEndpoint = "remoteEndpoint";
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
                IdleTimeout = 5000,
                AckTimeout = 3000
            };

            _kumasiAddr = new GenericNetworkIdentifier { HostName = "kumasi" };
            _kumasiEndpoint = new MemoryNetworkApi
            {
                LocalEndpoint = _kumasiAddr,
                SessionHandlerFactory = new DefaultSessionHandlerFactory(typeof(TestSessionHandler)),
                IdleTimeout = 5000,
                AckTimeout = 3000
            };
            _accraEndpoint.ConnectedNetworks.Add(_kumasiAddr, _kumasiEndpoint);
            _kumasiEndpoint.ConnectedNetworks.Add(_accraAddr, _accraEndpoint);
        }

        private List<TestLogRecord> GetValidatedTestLogs()
        {
            return TestAssemblyEntryPoint.GetTestLogs(record =>
            {
                if (record.Logger.Contains(typeof(MemoryNetworkApi).FullName) ||
                    record.Logger.Contains(typeof(MemoryNetworkApiTest).FullName))
                {
                    return true;
                }
                throw new Exception($"Unexpected logger found in records: {record.Logger}");
            });
        }

        private Dictionary<string, object> ParseLogRecordProperties(TestLogRecord logRecord)
        {
            return JsonConvert.DeserializeObject<Dictionary<string, object>>(logRecord.Properties);
        }

        [Fact]
        public async Task TestOpenSessionAsync()
        {
            Assert.False(_accraEndpoint.IsShuttingDown());

            TestAssemblyEntryPoint.ResetDb();
            var sessionId = Guid.NewGuid().ToString("n");
            var handlerArg = new TestSessionHandler(false);
            var openPromise = _accraEndpoint.OpenSessionAsync(_kumasiAddr, sessionId,
                handlerArg);
            var sessionHandler = await ((DefaultPromise<ISessionHandler>)openPromise).WrappedTask;

            Assert.Equal(handlerArg, sessionHandler);

            var testLogs = GetValidatedTestLogs();
            var logNavigator = new LogNavigator<TestLogRecord>(testLogs);
            // test that no new session handler is created but CompleteInit is called on what was passed.
            var record = logNavigator.Next(
                rec => rec.Properties.Contains("f8f6c939-09e2-4f8d-9b2b-fd25ee4ead9e"));
            Assert.Null(record);
            record = logNavigator.Next(
                rec => rec.Properties.Contains("3f4f66e2-dafc-4c79-aa42-6f988a337d78"));
            var parsed = ParseLogRecordProperties(record);
            Assert.Equal(sessionId, parsed[LogDataKeySessionId]);
            Assert.Equal(_kumasiAddr.ToString(), parsed[LogDataKeyRemoteEndpoint]);
            Assert.Equal(_accraEndpoint.ToString(), parsed[LogDataKeyNetworkApi]);
            Assert.Equal(true, parsed[LogDataKeyConfiguredForSend]);

            // test that session id can't be reused. 
            await Assert.ThrowsAnyAsync<Exception>(() =>
            {
                var promise = _accraEndpoint.OpenSessionAsync(_kumasiAddr, sessionId,
                    null);
                return ((DefaultPromise<ISessionHandler>)promise).WrappedTask;
            });

            // test without prespecified session id or handler.
            TestAssemblyEntryPoint.ResetDb();
            openPromise = _accraEndpoint.OpenSessionAsync(_kumasiAddr, null,
                null);
            var sessionHandler2 = await ((DefaultPromise<ISessionHandler>)openPromise).WrappedTask;

            Assert.Equal(typeof(TestSessionHandler), sessionHandler2.GetType());

            testLogs = GetValidatedTestLogs();
            logNavigator = new LogNavigator<TestLogRecord>(testLogs);
            // test that a new session handler is created and CompleteInit is called on it.
            record = logNavigator.Next(
                rec => rec.Properties.Contains("f8f6c939-09e2-4f8d-9b2b-fd25ee4ead9e"));
            Assert.NotNull(record);
            record = logNavigator.Next(
                rec => rec.Properties.Contains("3f4f66e2-dafc-4c79-aa42-6f988a337d78"));
            parsed = ParseLogRecordProperties(record);
            var sessionId2 = (string)parsed[LogDataKeySessionId];
            Assert.NotNull(sessionId2);
            Assert.Equal(_kumasiAddr.ToString(), parsed[LogDataKeyRemoteEndpoint]);
            Assert.Equal(_accraEndpoint.ToString(), parsed[LogDataKeyNetworkApi]);
            Assert.Equal(true, parsed[LogDataKeyConfiguredForSend]);

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

            _accraEndpoint.RequestSessionDispose(_kumasiAddr, sessionId,
                null);*/
        }

        class TestSessionHandler : ISessionHandler
        {
            public TestSessionHandler():
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
                    .AddProperty(LogDataKeySessionId, sessionId)
                    .AddProperty(LogDataKeyNetworkApi, networkApi.ToString())
                    .AddProperty(LogDataKeyRemoteEndpoint, remoteEndpoint.ToString())
                    .AddProperty(LogDataKeyConfiguredForSend, configureForInitialSend));
            }

            public AbstractNetworkApi NetworkApi => throw new NotImplementedException();

            public GenericNetworkIdentifier RemoteEndpoint => throw new NotImplementedException();

            public ISessionTaskExecutor TaskExecutor => throw new NotImplementedException();

            public string SessionId => throw new NotImplementedException();

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
                throw new NotImplementedException();
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
