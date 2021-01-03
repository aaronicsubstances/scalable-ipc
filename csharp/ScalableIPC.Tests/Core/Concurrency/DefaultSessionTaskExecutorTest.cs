using ScalableIPC.Core.Concurrency;
using ScalableIPC.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.Tests.Core.Concurrency
{
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
                eventLoop = new DefaultSessionTaskExecutor();
            }
            else
            {
                eventLoop = new DefaultSessionTaskExecutor(maxDegreeOfParallelism, runCallbacksUnderMutex);
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
                    // currently flaky.
                    Assert.NotEqual(eventualExpected, actualCbCount);
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
                eventLoop = new DefaultSessionTaskExecutor();
            }
            else
            {
                eventLoop = new DefaultSessionTaskExecutor(maxDegreeOfParallelism, true);
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
                // currently flaky.
                Assert.NotEqual(expectedCollection, actualCollection);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestTimeoutData))]
        public async Task TestTimeout(int delaySecs, bool cancel)
        {
            var eventLoop = new DefaultSessionTaskExecutor();
            var startTime = DateTime.UtcNow;
            DateTime? stopTime = null;
            object timeoutId = eventLoop.ScheduleTimeout(delaySecs * 1000, () =>
            {
                stopTime = DateTime.UtcNow;
            });
            if (delaySecs > 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySecs - 1));
            }
            if (cancel)
            {
                eventLoop.CancelTimeout(timeoutId);
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (cancel)
            {
                Assert.Null(stopTime);
            }
            else
            {
                Assert.NotNull(stopTime);
                var expectedStopTime = startTime.AddSeconds(delaySecs);
                // allow half sec precision in comparison.
                Assert.InRange(stopTime.Value, expectedStopTime.AddSeconds(-0.5), expectedStopTime.AddSeconds(0.5));
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
                new object[]{ 3, false },
                new object[]{ 3, true }
            };
        }

        [Fact]
        public Task TestPromiseCallbackSuccess()
        {
            var instance = new DefaultSessionTaskExecutor();
            return GenericTestPromiseCallbackSuccess(instance);
        }

        [Fact]
        public Task TestPromiseCallbackError()
        {
            var instance = new DefaultSessionTaskExecutor();
            return GenericTestPromiseCallbackError(instance);
        }

        internal static async Task GenericTestPromiseCallbackSuccess(DefaultSessionTaskExecutor instance)
        {
            var relatedInstance = new DefaultPromiseApi();
            var promiseCb = relatedInstance.CreateCallback<int>();
            var nativePromise = ((DefaultPromise<int>)promiseCb.RelatedPromise).WrappedTask;

            // test that nothing happens with callback after waiting
            var delayTask = Task.Delay(2000);
            Task firstCompletedTask = await Task.WhenAny(delayTask, nativePromise);
            Assert.Equal(delayTask, firstCompletedTask);

            // now test success completion of task
            delayTask = Task.Delay(2000);
            instance.CompletePromiseCallbackSuccessfully(promiseCb, 10);
            firstCompletedTask = await Task.WhenAny(delayTask, nativePromise);
            Assert.Equal(nativePromise, firstCompletedTask);
            
            // test that a Then chain callback can get what was promised.
            var continuationPromise = promiseCb.RelatedPromise.Then(v => v * v);
            var result = await ((DefaultPromise<int>)continuationPromise).WrappedTask;
            Assert.Equal(100, result);
        }

        internal static async Task GenericTestPromiseCallbackError(DefaultSessionTaskExecutor instance)
        {
            var relatedInstance = new DefaultPromiseApi();
            var promiseCb = relatedInstance.CreateCallback<int>();
            var nativePromise = ((DefaultPromise<int>)promiseCb.RelatedPromise).WrappedTask;

            // test that nothing happens with callback after waiting
            var delayTask = Task.Delay(2000);
            Task firstCompletedTask = await Task.WhenAny(delayTask, nativePromise);
            Assert.Equal(delayTask, firstCompletedTask);

            // now test success completion of task
            delayTask = Task.Delay(2000);
            instance.CompletePromiseCallbackExceptionally(promiseCb, new ArgumentOutOfRangeException());
            firstCompletedTask = await Task.WhenAny(delayTask, nativePromise);
            Assert.Equal(nativePromise, firstCompletedTask);

            // test that a CatchCompose chain callback can get exception from promise.
            var continuationPromise = promiseCb.RelatedPromise.Then(v => v * v)
                .CatchCompose(ex =>
                {
                    Assert.Equal(typeof(ArgumentOutOfRangeException),
                        ((AggregateException)ex).InnerExceptions[0].GetType());
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
                            ((AggregateException)ex).InnerExceptions[0].GetType());
                    });
                return ((DefaultPromise<int>)continuationPromise2).WrappedTask;
            });
        }
    }
}
