using ScalableIPC.Core.Concurrency;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.Tests.Core.Concurrency
{
    public class DefaultSessionTaskExecutorTest
    {
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
