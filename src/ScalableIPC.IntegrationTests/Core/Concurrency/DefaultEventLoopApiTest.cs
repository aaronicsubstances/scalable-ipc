using ScalableIPC.Core.Concurrency;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.IntegrationTests.Core.Concurrency
{
    public class DefaultEventLoopApiTest
    {
        [Fact]
        public async Task TestSerialLikeCallbackExecution()
        {
            var instance = new DefaultEventLoopApi();
            const int expectedCbCount = 1_000;
            int actualCbCount = 0;
            for (int i = 0; i < expectedCbCount; i++)
            {
                instance.PostCallback(() =>
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

            // wait for callbacks to be executed.
            await Task.Delay(TimeSpan.FromSeconds(2));

            int eventualExpected = expectedCbCount * (expectedCbCount % 2 == 0 ? 1 : -1);
            Assert.Equal(eventualExpected, actualCbCount);
        }

        [Fact]
        public async Task TestExpectedOrderOfCallbackProcessing()
        {
            var instance = new DefaultEventLoopApi();
            const int expectedCbCount = 1_000;
            var expectedCollection = Enumerable.Range(0, expectedCbCount);
            List<int> actualCollection = new List<int>();
            for (int i = 0; i < expectedCbCount; i++)
            {
                int captured = i;
                instance.PostCallback(() =>
                {
                    if (captured % 2 > 0)
                    {
                        // by forcing current thread to sleep in every other iteration,
                        // we increase the likelihood that other threads would have
                        // picked tasks from task queue, but will be blocked by
                        // runUnderMutex lock, waiting for this current one to finish.
                        // Hence when current one finishes, the OS thread scheduler
                        // can pick any of the waiters to run, and thus once in a while
                        // out of order addition to actualCollection will occur.
                        Thread.Sleep(10);
                    }
                    actualCollection.Add(captured);
                });
            }

            // wait for callbacks to be executed.
            await Task.Delay(TimeSpan.FromSeconds(10));

            Assert.Equal(expectedCbCount, actualCollection.Count);
            Assert.Equal(expectedCollection, actualCollection);
        }

        [Theory]
        [MemberData(nameof(CreateTestTimeoutData))]
        public async Task TestTimeout(int delaySecs, bool cancel)
        {
            // contract to test is that during a PostCallback, cancel works regardless of
            // how long we spend inside it.
            var instance = new DefaultEventLoopApi();
            var startTime = DateTime.UtcNow;
            DateTime? stopTime = null;
            instance.PostCallback(() =>
            {
                object timeoutId = instance.ScheduleTimeout(delaySecs * 1000, () =>
                {
                    stopTime = DateTime.UtcNow;
                });

                // test that even sleeping past schedule timeout delay AND cancelling
                // will still achieve cancellation.
                Thread.Sleep(TimeSpan.FromSeconds(Math.Max(0, delaySecs + (delaySecs % 2 == 0 ? 1 : -1))));
                if (cancel)
                {
                    instance.CancelTimeout(timeoutId);
                }
            });

            // wait for any pending callback to execute.
            await Task.Delay(TimeSpan.FromSeconds(5));

            if (cancel)
            {
                Assert.Null(stopTime);
            }
            else
            {
                Assert.NotNull(stopTime);
                var expectedStopTime = startTime.AddSeconds(delaySecs);
                // allow some secs tolerance in comparison using 
                // time differences observed in failed/flaky test results.
                Assert.Equal(expectedStopTime, stopTime.Value, TimeSpan.FromSeconds(1.5));
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
    }
}
