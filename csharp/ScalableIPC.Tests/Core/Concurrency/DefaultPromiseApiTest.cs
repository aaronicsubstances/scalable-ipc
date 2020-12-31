using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ScalableIPC.Tests.Core.Concurrency
{
    public class DefaultPromiseApiTest
    {
        [Fact]
        public async Task TestPromiseCompositions()
        {
            AbstractPromiseApi instance = new DefaultPromiseApi();
            var errors = new List<string>();
            var promise = instance.Resolve(10)
                .Then(num => {
                    Assert.Equal(10, num);
                    return "nice";
                })
                .Then<bool>(str =>
                {
                    Assert.Equal("nice", str);
                    throw new ArgumentException();
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
            Assert.Equal(90, finalValue);

            var promise2 = instance.Resolve("")
                .ThenCompose<int>(_ =>
                {
                    throw new ArgumentException();
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
            Assert.Equal("done", finalValue3);
        }

        private static void AssertRelevantExceptionTypePrescence(Type expected, Exception actual)
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
    }
}
