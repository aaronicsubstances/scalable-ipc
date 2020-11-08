using ScalableIPC.Core.ConcreteComponents;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.Tests.ConcreteComponents
{
    public class DefaultEventLoopApiTest
    {
        [Fact]
        public async Task TestUsedThreadCount()
        {
            var eventLoop = new DefaultEventLoopApi();
            var threadSafeList = new ConcurrentQueue<Thread>();
            const int cbCount = 1_000_000;
            for (int i = 0; i < cbCount; i++)
            {
                eventLoop.PostCallback(() =>
                {
                    threadSafeList.Enqueue(Thread.CurrentThread);
                });
            }
            // wait for 1 sec for callbacks to be executed.
            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Equal(cbCount, threadSafeList.Count);
            Assert.Single(threadSafeList.Distinct());
            Assert.NotEqual(Thread.CurrentThread, threadSafeList.ElementAt(0));
        }

        [Theory]
        [MemberData(nameof(CreateTestTimeoutData))]
        public async Task TestTimeout(int delaySecs, bool cancel)
        {
            var eventLoop = new DefaultEventLoopApi();
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
                // allow 1 sec precision in comparison.
                Assert.InRange(stopTime.Value, expectedStopTime, expectedStopTime.AddSeconds(1));
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
