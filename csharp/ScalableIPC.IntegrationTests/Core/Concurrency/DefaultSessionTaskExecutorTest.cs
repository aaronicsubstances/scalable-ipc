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
        private static readonly string LogDataKeyWorkIndex = "workIndex";
        private static readonly string LogDataKeyWorkLogIndex = "workLogIndex";
        
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
                // Although in practice maxDegreeOfParallelism = 1 has not failed before
                // even without lock,
                // according to http://www.albahari.com/threading/part4.aspx,
                // don't rely on memory consistency without the use of ordinary locks even
                // if there's no thread interference due to single degree of parallelism.

                // could still pass, especially on CPUs with low core count.
                try
                {
                    Assert.NotEqual(eventualExpected, actualCbCount);
                }
                catch (Exception)
                {
                    CustomLoggerFacade.WriteToStdOut(
                        $"TestSerialLikeCallbackExecution({maxDegreeOfParallelism}, " +
                        $"{runCallbacksUnderMutex}) passed.", null);
                }
            }
        }

        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [Theory]
        public async Task TestExpectedOrderOfCallbackProcessing(int maxDegreeOfParallelism)
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
                    if (captured < 5)
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
                // could still pass, especially on CPUs with low core count.
                try
                {
                    Assert.NotEqual(expectedCollection, actualCollection);
                }
                catch (Exception)
                {
                    CustomLoggerFacade.WriteToStdOut(
                        $"TestExpectedOrderOfCallbackProcessing({maxDegreeOfParallelism}) passed.", null);
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
            var cbExecutionIds = await WaitForSessionTaskExecutions(2);
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
            cbExecutionIds = await WaitForSessionTaskExecutions(4);
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
            await Assert.ThrowsAnyAsync<Exception>(() => WaitForSessionTaskExecutions(4));
            logNavigator = new LogNavigator<TestLogRecord>(GetValidatedTestLogs());
            record = logNavigator.Next(
                rec => rec.Properties.Contains("5394ab18-fb91-4ea3-b07a-e9a1aa150dd6"));
            Assert.Null(record);
        }

        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        [Theory]
        public async Task TestLimitedGroupConcurrencyLevel(int taskExecutorCount)
        {
            TestDatabase.ResetDb();

            var expectedGroupConcurrencyLevel = Math.Max(1, taskExecutorCount / 2);
            var group = new DefaultSessionTaskExecutorGroup(expectedGroupConcurrencyLevel);
            var taskExecutors = new List<DefaultSessionTaskExecutor>();
            for (int i = 0; i < taskExecutorCount; i++)
            {
                taskExecutors.Add(new DefaultSessionTaskExecutor($"session-{i}", group));
            }
            int workCount = 1000, sleepTime = 10;
            var randSelector = new Random();
            for (int i = 0; i < workCount; i++)
            {
                var captured = i;
                var randExecutorIdx = randSelector.Next(taskExecutorCount);
                taskExecutors[randExecutorIdx].PostCallback(() =>
                {
                    CustomLoggerFacade.TestLog(() => new CustomLogEvent(GetType())
                        .AddProperty(LogDataKeyWorkIndex, captured)
                        .AddProperty(LogDataKeyWorkLogIndex, 0));
                    Thread.Sleep(sleepTime);
                    CustomLoggerFacade.TestLog(() => new CustomLogEvent(GetType())
                        .AddProperty(LogDataKeyWorkIndex, captured)
                        .AddProperty(LogDataKeyWorkLogIndex, 1));
                });
            }
            var cbExecutionIds = await WaitForSessionTaskExecutions(workCount * sleepTime / 1000 + 10);
            Assert.Equal(workCount, cbExecutionIds.Count);
            AssertExpectedEventLoopBehaviour(taskExecutorCount, expectedGroupConcurrencyLevel);
        }

        private void AssertExpectedEventLoopBehaviour(int taskExecutorCount, int expectedGroupConcurrencyLevel)
        {
            var concurrencyLevelMap = new Dictionary<string, int>();

            // ordering of work logs is required only if there is only 1 task executor around.
            bool assertOrder = taskExecutorCount == 1;

            int expectedWorkIndex = 0;
            bool startWorkLogSeen = false;

            var testLogs = GetValidatedTestLogs();
            foreach (var testLog in testLogs)
            {
                if (testLog.ParsedProperties.ContainsKey(CustomLogEvent.ThrottledTaskSchedulerId))
                {
                    // NB: newtonsoft.json deserializes integers into objects as longs
                    var schedulerId = (string)testLog.ParsedProperties[CustomLogEvent.ThrottledTaskSchedulerId];
                    var concurrencyLevel = (long)testLog.ParsedProperties[
                        CustomLogEvent.ThrottledTaskSchedulerConcurrencyLevel];
                    if (concurrencyLevelMap.ContainsKey(schedulerId))
                    {
                        concurrencyLevelMap[schedulerId] = (int)concurrencyLevel;
                    }
                    else
                    {
                        concurrencyLevelMap.Add(schedulerId, (int)concurrencyLevel);
                    }
                    int actualConcurrencyLevel = concurrencyLevelMap.Values.Sum();
                    Assert.True(actualConcurrencyLevel <= expectedGroupConcurrencyLevel,
                        $"Group concurrency level of {expectedGroupConcurrencyLevel} is not being respected. " +
                        $"Observed {actualConcurrencyLevel}");
                }
                else if (assertOrder && testLog.ParsedProperties.ContainsKey(LogDataKeyWorkIndex))
                {
                    // NB: newtonsoft.json deserializes integers into objects as longs
                    var actualWorkIndex = (long)testLog.ParsedProperties[LogDataKeyWorkIndex];
                    var actualWorkLogIndex = (long)testLog.ParsedProperties[LogDataKeyWorkLogIndex];

                    Assert.Equal(expectedWorkIndex, (int)actualWorkIndex);

                    // also ensure there is no interleaving.
                    if (startWorkLogSeen)
                    {
                        Assert.Equal(1, actualWorkLogIndex);
                        expectedWorkIndex++;
                        startWorkLogSeen = false;
                    }
                    else
                    {
                        Assert.Equal(0, actualWorkLogIndex);
                        startWorkLogSeen = true;
                    }
                }
            }
        }

        private List<TestLogRecord> GetValidatedTestLogs()
        {
            return TestDatabase.GetTestLogs(record =>
            {
                if (record.Logger.Contains(typeof(DefaultSessionTaskExecutor).FullName) ||
                    record.Logger.Contains(typeof(DefaultSessionTaskExecutorTest).FullName) ||
                    record.Logger.Contains(typeof(LimitedConcurrencyLevelTaskScheduler).FullName))
                {
                    return true;
                }
                throw new Exception($"Unexpected logger found in records: {record.Logger}");
            });
        }

        private async Task<List<string>> WaitForSessionTaskExecutions(int waitSecs)
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
                await Awaitility.WaitAsync(TimeSpan.FromSeconds(waitSecs), lastCall =>
                {
                    var startRemCount = newCallbackExecutionIds.Count;
                    CustomLoggerFacade.WriteToStdOut($"Start rem count = {startRemCount}", null);
                    var testLogs = GetValidatedTestLogs();
                    CustomLoggerFacade.WriteToStdOut($"Fetch count = {testLogs.Count}", null);
                    foreach (var testLog in testLogs)
                    {
                        if (testLog.ParsedProperties.ContainsKey(CustomLogEvent.LogDataKeyEndingSessionTaskExecutionId))
                        {
                            newCallbackExecutionIds.Remove((string)testLog.ParsedProperties[
                                CustomLogEvent.LogDataKeyEndingSessionTaskExecutionId]);
                        }
                    }
                    CustomLoggerFacade.WriteToStdOut($"End rem count = {newCallbackExecutionIds.Count}", null);
                    if (lastCall && startRemCount == newCallbackExecutionIds.Count)
                    {
                        Assert.Empty(newCallbackExecutionIds);
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
