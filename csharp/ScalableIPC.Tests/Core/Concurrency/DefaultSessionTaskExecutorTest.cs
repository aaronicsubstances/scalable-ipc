﻿using ScalableIPC.Core.Concurrency;
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
        //[InlineData(2, false)] // results in flakiness, so sometimes yield correct results.
        [InlineData(2, true)]
        [InlineData(10, false)]
        [InlineData(10, true)]
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
                    // using this logic, multithreaded execution
                    // will definitely fail to increment up to
                    // expected total.
                    if (actualCbCount < 0)
                    {
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
                    Assert.NotEqual(eventualExpected, actualCbCount);
                }
            }
        }

        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(10)]
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
            object timeoutId = eventLoop.ScheduleTimeout(delaySecs, () =>
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
    }
}
