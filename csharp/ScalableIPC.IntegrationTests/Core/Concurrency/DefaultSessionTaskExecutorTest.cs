using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using ScalableIPC.Core.Helpers;
using ScalableIPC.IntegrationTests.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.IntegrationTests.Core.Concurrency
{
    [Collection("SequentialTests")]
    public class DefaultSessionTaskExecutorTest
    {
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(2, true)]
        [Theory]
        public async Task TestSerialLikeCallbackExecution(int maxDegreeOfParallelism, bool runCallbacksUnderMutex)
        {
            DefaultSessionTaskExecutor eventLoop;
            if (maxDegreeOfParallelism < 1)
            {
                eventLoop = new DefaultSessionTaskExecutor(null, null);
            }
            else
            {
                eventLoop = new DefaultSessionTaskExecutor(null, null,
                    maxDegreeOfParallelism, runCallbacksUnderMutex);
            }
            const int expectedCbCount = 1_000;
            int actualCbCount = 0;
            for (int i = 0; i < expectedCbCount; i++)
            {
                eventLoop.PostCallback(() =>
                {
                    if (actualCbCount < 0)
                    {
                        if (actualCbCount > -10)
                        {
                            // by forcing current thread to sleep, another thread will definitely take
                            // over while current thread hasn't completed. In multithreaded execution,
                            // this will definitely fail to increment up to
                            // expected total.
                            Thread.Sleep(10);
                        }
                        actualCbCount = -actualCbCount + 1;
                    }
                    else
                    {
                        actualCbCount = -(actualCbCount + 1);
                    }
                });
            }
            // wait for 1 sec for callbacks to be executed.
            await Task.Delay(TimeSpan.FromSeconds(1));

            int eventualExpected = expectedCbCount * (expectedCbCount % 2 == 0 ? 1 : -1);
            if (runCallbacksUnderMutex)
            {
                Assert.Equal(eventualExpected, actualCbCount);
            }
            else
            {
                // According to http://www.albahari.com/threading/part4.aspx,
                // don't rely on memory consistency without the use of ordinary locks even
                // if there's no thread interference due to single degree of parallelism.
                if (maxDegreeOfParallelism > 1)
                {
                    // currently flaky, especially on CPUs with low core count.
                    if (Environment.ProcessorCount > 4)
                    {
                        Assert.NotEqual(eventualExpected, actualCbCount);
                    }
                }
            }
        }

        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [Theory]
        public async Task TestGuaranteedFairnessOfCallbackProcessing(int maxDegreeOfParallelism)
        {
            DefaultSessionTaskExecutor eventLoop;
            if (maxDegreeOfParallelism < 1)
            {
                eventLoop = new DefaultSessionTaskExecutor(null, null);
            }
            else
            {
                eventLoop = new DefaultSessionTaskExecutor(null, null,
                    maxDegreeOfParallelism, true);
            }
            const int expectedCbCount = 1_000;
            var expectedCollection = Enumerable.Range(0, expectedCbCount);
            List<int> actualCollection = new List<int>();
            for (int i = 0; i < expectedCbCount; i++)
            {
                int captured = i;
                eventLoop.PostCallback(() =>
                {
                    if (captured == 0)
                    {
                        // by forcing current thread to sleep, another thread will definitely
                        // add to collection before the first item, breaking guarantee.
                        Thread.Sleep(10);
                    }
                    actualCollection.Add(captured);
                });
            }
            // wait for 1 sec for callbacks to be executed.
            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Equal(expectedCbCount, actualCollection.Count);

            if (maxDegreeOfParallelism < 2)
            {
                Assert.Equal(expectedCollection, actualCollection);
            }
            else
            {
                // currently flaky, especially on CPUs with low core count.
                if (Environment.ProcessorCount > 4)
                {
                    Assert.NotEqual(expectedCollection, actualCollection);
                }
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestTimeoutData))]
        public async Task TestTimeout(int delaySecs, bool cancel)
        {
            // contract to test is that during a PostCallback, cancel works regardless of
            // how long we spend inside it.
            var eventLoop = new DefaultSessionTaskExecutor(null, null);
            var promiseCb = new DefaultPromiseCompletionSource<VoidType>(DefaultPromiseApi.Instance,
                null);
            var startTime = DateTime.UtcNow;
            DateTime? stopTime = null;
            eventLoop.PostCallback(() =>
            {
                try
                {
                    object timeoutId = eventLoop.ScheduleTimeout(delaySecs * 1000, () =>
                    {
                        stopTime = DateTime.UtcNow;
                    });

                    // test that even sleeping past schedule timeout delay AND cancelling
                    // will still achieve cancellation.
                    Thread.Sleep(TimeSpan.FromSeconds(Math.Max(0, delaySecs + (delaySecs % 2 == 0 ? 1 : -1))));
                    if (cancel)
                    {
                        eventLoop.CancelTimeout(timeoutId);
                    }
                }
                catch (Exception ex)
                {
                    promiseCb.CompleteExceptionally(ex);
                }
                finally
                {
                    // will be ignored if exception is raised first.
                    promiseCb.CompleteSuccessfully(VoidType.Instance);
                }
            });

            await ((DefaultPromise<VoidType>)promiseCb.RelatedPromise).WrappedTask;

            // wait for any pending callback to execute.
            await Task.Delay(2000);

            if (cancel)
            {
                Assert.Null(stopTime);
            }
            else
            {
                Assert.NotNull(stopTime);
                var expectedStopTime = startTime.AddSeconds(delaySecs);
                // allow some secs tolerance in comparison
                Assert.InRange(stopTime.Value, expectedStopTime, 
                    expectedStopTime.AddSeconds(1.5));
            }
        }

        public static List<object[]> CreateTestTimeoutData()
        {
            return new List<object[]>
            {
                new object[]{ 0, false },
                new object[]{ 0, true },
                new object[]{ 1, false },
                new object[]{ 1, true },
                new object[]{ 2, false },
                new object[]{ 2, true },
                new object[]{ 3, false },
                new object[]{ 3, true }
            };
        }

        [Fact]
        public Task TestPromiseCallbackSuccess()
        {
            var instance = new DefaultSessionTaskExecutor(null, null);
            return GenericTestPromiseCallbackSuccess(instance);
        }

        [Fact]
        public Task TestPromiseCallbackError()
        {
            var instance = new DefaultSessionTaskExecutor(null, null);
            return GenericTestPromiseCallbackError(instance);
        }

        internal static async Task GenericTestPromiseCallbackSuccess(DefaultSessionTaskExecutor instance)
        {
            AbstractPromiseApi relatedInstance = DefaultPromiseApi.Instance;
            var promiseCb = relatedInstance.CreateCallback<int>(instance);
            var nativePromise = ((DefaultPromise<int>)promiseCb.RelatedPromise).WrappedTask;

            // test that nothing happens with callback after waiting
            var delayTask = Task.Delay(2000);
            Task firstCompletedTask = await Task.WhenAny(delayTask, nativePromise);
            Assert.Equal(delayTask, firstCompletedTask);

            // now test success completion of task
            delayTask = Task.Delay(2000);
            promiseCb.CompleteSuccessfully(10);
            firstCompletedTask = await Task.WhenAny(delayTask, nativePromise);
            Assert.Equal(nativePromise, firstCompletedTask);
            
            // test that a Then chain callback can get what was promised.
            var continuationPromise = promiseCb.RelatedPromise.Then(v => v * v);
            var result = await ((DefaultPromise<int>)continuationPromise).WrappedTask;
            Assert.Equal(100, result);
        }

        internal static async Task GenericTestPromiseCallbackError(DefaultSessionTaskExecutor instance)
        {
            AbstractPromiseApi relatedInstance = DefaultPromiseApi.Instance;
            var promiseCb = relatedInstance.CreateCallback<int>(instance);
            var nativePromise = ((DefaultPromise<int>)promiseCb.RelatedPromise).WrappedTask;

            // test that nothing happens with callback after waiting
            var delayTask = Task.Delay(2000);
            Task firstCompletedTask = await Task.WhenAny(delayTask, nativePromise);
            Assert.Equal(delayTask, firstCompletedTask);

            // now test success completion of task
            delayTask = Task.Delay(2000);
            promiseCb.CompleteExceptionally(new ArgumentOutOfRangeException());
            firstCompletedTask = await Task.WhenAny(delayTask, nativePromise);
            Assert.Equal(nativePromise, firstCompletedTask);

            // test that a CatchCompose chain callback can get exception from promise.
            var continuationPromise = promiseCb.RelatedPromise.Then(v => v * v)
                .CatchCompose(ex =>
                {
                    Assert.Equal(typeof(ArgumentOutOfRangeException),
                        ex.InnerExceptions[0].GetType());
                    return relatedInstance.Resolve(-11);
                });
            var result = await ((DefaultPromise<int>)continuationPromise).WrappedTask;
            Assert.Equal(-11, result);

            // test that a Catch chain callback can forward exception
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            {
                var continuationPromise2 = promiseCb.RelatedPromise.Then(v => v * v)
                    .Catch(ex =>
                    {
                        Assert.Equal(typeof(ArgumentOutOfRangeException),
                            ex.InnerExceptions[0].GetType());
                    });
                return ((DefaultPromise<int>)continuationPromise2).WrappedTask;
            });
        }

        [Fact]
        public async Task TestCallbackExceptionRecording()
        {
            var sessionTaskExecutor = new DefaultSessionTaskExecutor("test", null);

            // test postCallback
            TestDatabase.ResetDb();
            sessionTaskExecutor.PostCallback(() =>
            {
                throw new NotImplementedException("testing cb");
            });
            var cbExecutionIds = await WaitForSessionTaskExecution(2);
            Assert.Single(cbExecutionIds);
            var logNavigator = new LogNavigator<TestLogRecord>(GetValidatedTestLogs());
            var record = logNavigator.Next(
                rec => rec.Properties.Contains("1e934595-0dcb-423a-966c-5786d1925e3d"));
            Assert.NotNull(record);
            Assert.Equal("test", record.ParsedProperties[CustomLogEvent.LogDataKeySessionId]);
            Assert.Equal(cbExecutionIds[0],
                record.ParsedProperties[CustomLogEvent.LogDataKeySessionTaskExecutionId]);
            Assert.Contains("testing cb", record.Exception);

            // test setTimeout
            TestDatabase.ResetDb();
            sessionTaskExecutor.ScheduleTimeout(2000, () =>
            {
                throw new NotImplementedException("testing ex");
            });
            cbExecutionIds = await WaitForSessionTaskExecution(4);
            Assert.Single(cbExecutionIds);
            logNavigator = new LogNavigator<TestLogRecord>(GetValidatedTestLogs());
            record = logNavigator.Next(
                rec => rec.Properties.Contains("5394ab18-fb91-4ea3-b07a-e9a1aa150dd6"));
            Assert.NotNull(record);
            Assert.Equal("test", record.ParsedProperties[CustomLogEvent.LogDataKeySessionId]);
            Assert.Equal(cbExecutionIds[0],
                record.ParsedProperties[CustomLogEvent.LogDataKeySessionTaskExecutionId]);
            Assert.Contains("testing ex", record.Exception);

            // test cancellation.
            TestDatabase.ResetDb();
            sessionTaskExecutor.CancelTimeout(sessionTaskExecutor.ScheduleTimeout(2000, () =>
            {
                throw new NotImplementedException("testing cancelled");
            }));            
            await Assert.ThrowsAnyAsync<Exception>(() => WaitForSessionTaskExecution(4));
            logNavigator = new LogNavigator<TestLogRecord>(GetValidatedTestLogs());
            record = logNavigator.Next(
                rec => rec.Properties.Contains("5394ab18-fb91-4ea3-b07a-e9a1aa150dd6"));
            Assert.Null(record);
        }

        private List<TestLogRecord> GetValidatedTestLogs()
        {
            return TestDatabase.GetTestLogs(record =>
            {
                if (record.Logger.Contains(typeof(DefaultSessionTaskExecutor).FullName))
                {
                    return true;
                }
                throw new Exception($"Unexpected logger found in records: {record.Logger}");
            });
        }

        private async Task<List<string>> WaitForSessionTaskExecution(int waitSecs)
        {
            var testLogs = GetValidatedTestLogs();
            var newCallbackExecutionIds = new List<string>();
            int lastId = -1;
            foreach (var testLog in testLogs)
            {
                if (testLog.ParsedProperties.ContainsKey(CustomLogEvent.LogDataKeyNewSessionTaskId))
                {
                    newCallbackExecutionIds.Add((string)testLog.ParsedProperties[
                        CustomLogEvent.LogDataKeyNewSessionTaskId]);
                    lastId = testLog.Id;
                }
            }
            if (newCallbackExecutionIds.Count > 0)
            {
                await Awaitility.WaitAsync(TimeSpan.FromSeconds(waitSecs), () =>
                {
                    var testLogs = GetValidatedTestLogs();
                    foreach (var testLog in testLogs)
                    {
                        if (testLog.ParsedProperties.ContainsKey(CustomLogEvent.LogDataKeyEndingSessionTaskExecutionId))
                        {
                            newCallbackExecutionIds.Remove((string)testLog.ParsedProperties[
                                CustomLogEvent.LogDataKeyEndingSessionTaskExecutionId]);
                        }
                    }
                    return newCallbackExecutionIds.Count == 0;
                });
            }
            testLogs = GetValidatedTestLogs();
            Assert.Empty(testLogs.Where(testLog => testLog.Id > lastId &&
                testLog.ParsedProperties.ContainsKey(CustomLogEvent.LogDataKeyNewSessionTaskId)));
            return testLogs.Where(x => x.ParsedProperties.ContainsKey(
                CustomLogEvent.LogDataKeyNewSessionTaskId))
                .Select(x => (string)x.ParsedProperties[CustomLogEvent.LogDataKeyNewSessionTaskId])
                .ToList();
        }
    }
}
