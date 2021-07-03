using ScalableIPC.Core.Concurrency;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.IntegrationTests.Core.Concurrency
{
    public class UnsynchronizedEventLoopApiTest
    {
        [Fact]
        public async Task TestPostCallback()
        {
            var instance = new UnsynchronizedEventLoopApi();
            const bool expected = true;
            var actual = false;
            instance.PostCallback(() =>
            {
                actual = true;
            });

            // wait for callbacks to be executed.
            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Equal(expected, actual);
        }

        [Theory]
        [MemberData(nameof(CreateTestTimeoutData))]
        public async Task TestTimeout(int delaySecs, bool cancel)
        {
            // contract to test is that during a PostCallback, cancel works as long as it is called
            // before timeout.
            var instance = new UnsynchronizedEventLoopApi();
            var startTime = DateTime.UtcNow;
            DateTime? stopTime = null;
            instance.PostCallback(() =>
            {
                object timeoutId = instance.ScheduleTimeout(delaySecs * 1000, () =>
                {
                    stopTime = DateTime.UtcNow;
                });

                // test that sleeping before schedule timeout delay AND cancelling
                // will definitely achieve cancellation.
                Thread.Sleep(TimeSpan.FromSeconds(Math.Max(0, delaySecs - 1)));
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
