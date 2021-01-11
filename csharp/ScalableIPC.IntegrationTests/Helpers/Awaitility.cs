using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.IntegrationTests.Helpers
{
    public class Awaitility
    {
        internal static async Task AssertAsync(TimeSpan duration, Func<bool> conditionAssertion)
        {
            var instance = DefaultPromiseApi.Instance;
            var promise = instance.Poll<VoidType>(arg =>
            {
                Assert.True(conditionAssertion.Invoke(), $"condition being asserted is false after " +
                    $"{arg.UptimeMillis} ms");
                return null;
            }, 1000, (long)duration.TotalMilliseconds);
            await ((DefaultPromise<VoidType>)promise).WrappedTask;
        }

        internal static async Task WaitAsync(TimeSpan duration, Func<bool> conditionAwaiting)
        {
            var instance = DefaultPromiseApi.Instance;
            var promise = instance.Poll<VoidType>(arg =>
            {
                if (conditionAwaiting.Invoke())
                {
                    return new PollCallbackRet<VoidType>
                    {
                        Stop = true
                    };
                }
                Assert.False(arg.LastCall, $"Condition being awaited is still false after " +
                    $"{duration.TotalMilliseconds} ms");
                return null;
            }, 1000, (long)duration.TotalMilliseconds);
            await ((DefaultPromise<VoidType>)promise).WrappedTask;
        }


        [Fact]
        public async Task TestAssertAsync()
        {
            await AssertAsync(TimeSpan.FromSeconds(3), () => true);
            await Assert.ThrowsAnyAsync<Exception>(() => AssertAsync(TimeSpan.FromSeconds(3), () => false));
        }


        [Fact]
        public async Task TestWaitAsync()
        {
            var startTime = DateTime.UtcNow;
            await WaitAsync(TimeSpan.FromSeconds(3), () =>
            {
                return (DateTime.UtcNow - startTime).TotalSeconds > 2;
            });
            startTime = DateTime.UtcNow;
            await Assert.ThrowsAnyAsync<Exception>(() => WaitAsync(TimeSpan.FromSeconds(3), () =>
            {
                return (DateTime.UtcNow - startTime).TotalSeconds > 5;
            }));
        }
    }
}
