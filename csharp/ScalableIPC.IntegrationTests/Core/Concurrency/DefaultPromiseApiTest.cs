using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.IntegrationTests.Core.Concurrency
{
    public class DefaultPromiseApiTest
    {
        [Fact]
        public async Task TestPromiseCompositions()
        {
            AbstractPromiseApi instance = new DefaultPromiseApi();
            var errors = new List<string>();
            var finallyRuns = new List<string>();
            var promise = instance.Resolve(10)
                .Then(num => {
                    Assert.Equal(10, num);
                    return "nice";
                })
                .Finally(() =>
                {
                    finallyRuns.Add("19d3881b-3335-45a0-b89f-1a91618a21a1");
                })
                .Then<bool>(str =>
                {
                    Assert.Equal("nice", str);
                    throw new ArgumentException();
                })
                .Finally(() =>
                {
                    finallyRuns.Add("605d037f-f007-4176-8920-083fc0b7b4c3");
                })
                .Then(v =>
                {
                    errors.Add("d4c49ed8-1e62-4ea9-87c9-62a6a131d2de");
                    return v;
                })
                .Catch(e =>
                {
                    // very good.
                    AssertRelevantExceptionTypePrescence(typeof(ArgumentException), e);
                })
                .Finally(() =>
                {
                    finallyRuns.Add("6ea8871f-4953-4e42-a4f6-66e7037da5c5");
                })
                .Then(v =>
                {
                    errors.Add("7e47fb69-c037-4336-ab01-a1eb016cc7a2");
                    return v;
                })
                .CatchCompose(e =>
                {
                    // very good again.
                    AssertRelevantExceptionTypePrescence(typeof(ArgumentException), e);
                    return instance.Resolve(false);
                })
                .ThenCompose(v =>
                {
                    Assert.False(v);
                    return instance.Resolve(20);
                })
                .CatchCompose(e =>
                {
                    errors.Add("af58f464-0647-4fc4-af87-5b3520fee7c6");
                    return instance.Resolve(-11);
                })
                .ThenCompose(v =>
                {
                    // yes, CatchCompose didn't prevent Then from getting it.
                    return instance.Reject<bool>(new ArgumentNullException());
                })
                .ThenCompose(v =>
                {
                    errors.Add("badfa2ae-f06f-49d3-96bb-6ee0839ff8da");
                    return instance.Resolve(v);
                })
                .Catch(e =>
                {
                    AssertRelevantExceptionTypePrescence(typeof(ArgumentNullException), e);
                    // raise a different kind of error.
                    throw new ArgumentOutOfRangeException();
                })
                .Then(v =>
                {
                    errors.Add("911f876a-474f-4de1-9087-f7acd474f258");
                    return "";
                })
                .CatchCompose(e =>
                {
                    AssertRelevantExceptionTypePrescence(typeof(ArgumentOutOfRangeException), e);
                    return instance.Resolve("shoe");
                })
                .ThenOrCatchCompose(s =>
                {
                    Assert.Equal("shoe", s);
                    return instance.Resolve(90);
                }, e =>
                {
                    errors.Add("2ff71633-bff7-454d-96eb-f9e1ffb60ecb");
                    return instance.Resolve(-17);
                });
            var finalValue = await ((DefaultPromise<int>)promise).WrappedTask;
            Assert.Empty(errors); // only after await will errors be fully populated if any.
            Assert.Equal(new List<string> {
                "19d3881b-3335-45a0-b89f-1a91618a21a1",
                "605d037f-f007-4176-8920-083fc0b7b4c3",
                "6ea8871f-4953-4e42-a4f6-66e7037da5c5" }, finallyRuns);
            Assert.Equal(90, finalValue);

            finallyRuns.Clear();
            var promise2 = instance.Resolve("")
                .ThenCompose<int>(_ =>
                {
                    throw new ArgumentException();
                })
                .Finally(() =>
                {
                    finallyRuns.Add("6e7d542a-ed51-49fc-8584-813d23b19598");
                })
                .ThenCompose(_ =>
                {
                    errors.Add("te");
                    return instance.Resolve(-9);
                })
                .ThenCompose(_ =>
                {
                    errors.Add("yo");
                    return instance.Resolve(-9);
                })
                .CatchCompose(e =>
                {
                    AssertRelevantExceptionTypePrescence(typeof(ArgumentException), e);
                    throw new ArgumentOutOfRangeException();
                })
                .ThenOrCatchCompose(s =>
                {
                    errors.Add("1f5a3b26-063b-4f1c-a468-88a6a7bd7bc8");
                    return instance.Resolve(-9);
                }, e =>
                {
                    AssertRelevantExceptionTypePrescence(typeof(ArgumentOutOfRangeException), e);
                    return instance.Resolve(20);
                })
                .ThenCompose<bool>(_ =>
                {
                    throw new ArgumentNullException();
                });

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                ((DefaultPromise<bool>)promise2).WrappedTask
            );
            Assert.Empty(errors);
            Assert.Equal(new List<string> { "6e7d542a-ed51-49fc-8584-813d23b19598" }, finallyRuns);

            finallyRuns.Clear();
            var promise3 = instance.Resolve("")
                .ThenOrCatchCompose(s =>
                {
                    throw new ArgumentException();
                }, e =>
                {
                    errors.Add("8212c45f-8b3b-4035-9b7e-99378e17df27");
                    return instance.Resolve(20);
                })
                .ThenOrCatchCompose(s =>
                {
                    errors.Add("a2aae64d-5a94-4d6d-8c47-60a375ae5ed4");
                    return instance.Resolve(-2);
                }, e =>
                {
                    AssertRelevantExceptionTypePrescence(typeof(ArgumentException), e);
                    throw new ArgumentOutOfRangeException();
                })
                .ThenOrCatchCompose(s =>
                {
                    errors.Add("aea457a9-249a-44f9-8eb7-315afad22eb5");
                    return instance.Resolve(-2);
                }, e =>
                {
                    AssertRelevantExceptionTypePrescence(typeof(ArgumentOutOfRangeException), e);
                    return instance.Resolve(9);
                })
                .Then(n =>
                {
                    Assert.Equal(9, n);
                    return "done";
                })
                .Catch(e =>
                {
                    errors.Add("c910013e-590c-4c10-9018-8e55fffc48d1");
                });

            var finalValue3 = await ((DefaultPromise<string>)promise3).WrappedTask;
            Assert.Empty(errors);
            Assert.Empty(finallyRuns);
            Assert.Equal("done", finalValue3);

            finallyRuns.Clear();
            var promise4 = instance.Resolve("")
                .Finally(() =>
                {
                    throw new ArgumentException();
                })
                .Then(s =>
                {
                    errors.Add("ce7c4312-ef20-4725-8fad-1c72521b3fed");
                    return "-1";
                })
                .Catch(ex =>
                {
                    AssertRelevantExceptionTypePrescence(typeof(ArgumentException), ex);
                });

            await Assert.ThrowsAsync<ArgumentException>(() =>
                ((DefaultPromise<string>)promise4).WrappedTask
            );
            Assert.Empty(errors);
            Assert.Empty(finallyRuns);
        }

        private static void AssertRelevantExceptionTypePrescence(Type expected, AggregateException actual)
        {
            var relevantActualExceptionTypes = new List<Type>();
            FetchRelevantExceptionTypes(actual, relevantActualExceptionTypes);
            Assert.Contains(expected, relevantActualExceptionTypes);
        }

        private static void FetchRelevantExceptionTypes(Exception ex, List<Type> collector)
        {
            collector.Add(ex.GetType());
            if (ex is AggregateException aggEx)
            {
                foreach (var nested in aggEx.InnerExceptions)
                {
                    collector.Add(nested.GetType());
                    //FetchRelevantExceptionTypes(nested, collector);
                }
            }
        }

        [Fact]
        public async Task TestDelay()
        {
            int executionCount = 0;
            var instance = new DefaultPromiseApi();
            instance.Delay(2000).Then(_ =>
            {
                Interlocked.Increment(ref executionCount);
                return VoidType.Instance;
            });
            await Task.Delay(1000);
            Assert.Equal(0, executionCount);
            await Task.Delay(1500);
            Assert.Equal(1, executionCount);

            Interlocked.Exchange(ref executionCount, 0);
            instance.Delay(0).Then(_ =>
            {
                Interlocked.Increment(ref executionCount);
                return VoidType.Instance;
            });
            await Task.Delay(500);
            Assert.Equal(1, executionCount);
        }

        [Fact]
        public async Task TestNativeTaskCancellationInteroperability()
        {
            var instance = new DefaultPromiseApi();
            var cancelledTaskSource = new TaskCompletionSource<int>();
            var promise = new DefaultPromise<int>(instance, cancelledTaskSource.Task);
            cancelledTaskSource.SetCanceled();
            await Assert.ThrowsAsync<TaskCanceledException>(() => promise.WrappedTask);

            var errors = new List<string>();
            var finallyRuns = new List<string>();
            var promiseContinua = promise.Catch(ex =>
                {
                    AssertRelevantExceptionTypePrescence(typeof(TaskCanceledException), ex);
                })
                .Then(_ =>
                {
                    errors.Add("4a54f82c-37c9-4b68-9d7c-0748e2de2390");
                    return -1;
                }).
                Catch(ex =>
                {
                    AssertRelevantExceptionTypePrescence(typeof(TaskCanceledException), ex);
                })
                .Finally(() =>
                {
                    finallyRuns.Add("2138150b-adc6-4bb9-b0f2-311a0c836d71");
                })
                .ThenOrCatchCompose(_ =>
                {
                    errors.Add("49887065-31c4-4914-9531-05d5eac4b23b");
                    return instance.Resolve("err");
                }, ex =>
                {
                    AssertRelevantExceptionTypePrescence(typeof(TaskCanceledException), ex);
                    return instance.Resolve("ok");
                });
            var finalValue = await ((DefaultPromise<string>)promiseContinua).WrappedTask;
            Assert.Empty(errors);
            Assert.Equal(new List<string> { "2138150b-adc6-4bb9-b0f2-311a0c836d71" }, finallyRuns);
            Assert.Equal("ok", finalValue);
        }
    }
}
