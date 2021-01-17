using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ScalableIPC.Core.Abstractions
{
    /// <summary>
    /// Promise API design is to take common functionality of NodeJS Promises, C#.NET Core Tasks, and
    /// Java 8 CompletableFuture.
    /// 
    /// 1. Promises automatically unwrap in NodeJs. Equivalent are
    ///  - c# task.unwrap
    ///  - java 8 completablefuture.thenComposeAsync
    ///  Conclusion: don't automatically unwrap, instead be explicit about it.
    ///  
    /// 2. Task cancellation. NodeJS Promises doesn't have Cancel API, but Java and C# do
    ///  - fortunately cancellation is needed only for timeout
    ///  Conclusion: Don't expose a Cancel API.
    /// 
    /// 3. Rejection handlers in NodeJS can return values and continue like no error happened.
    ///  - not so in C#. an error in async-await keyword usage results in an exception 
    ///  Conclusion: only accept exceptions in rejection handlers, but allow them to return values.
    /// </summary>
    public interface AbstractPromiseApi
    {
        PromiseCompletionSource<T> CreateCallback<T>(ISessionTaskExecutor sessionTaskExecutor);
        AbstractPromise<T> Resolve<T>(T value);
        AbstractPromise<T> Reject<T>(Exception reason);
        AbstractPromise<VoidType> CompletedPromise();
        AbstractPromise<VoidType> Delay(int millis);
        AbstractPromise<T> Poll<T>(Func<PollCallbackArg<T>, PollCallbackRet<T>> cb,
            int intervalMillis, long totalDurationMillis, T initialValue); 
        AbstractPromise<List<PromiseResult<T>>> WhenAll<T>(params AbstractPromise<T>[] promises);
        AbstractPromise<List<T>> WhenAllSucceed<T>(params AbstractPromise<T>[] promises);
        AbstractPromise<int> WhenAny<T>(params AbstractPromise<T>[] promises);
        AbstractPromise<int> WhenAnySucceed<T>(params AbstractPromise<T>[] promises);

        // The following properties/methods and _ILogicalThreadMember properties/methods 
        // are for identifying logical threads of control with
        // unique ids. This is meant to enable parallel testing of the core classes of
        // this project during the protocol's operation.
        Guid? CurrentLogicalThreadId { get; }
        Guid? _CurrentLogicalThreadId { set; }
        AbstractPromise<VoidType> StartLogicalThread(Guid newLogicalThreadId);
        void EndLogicalThread(Guid logicalThreadId);
        void EndCurrentLogicalThread();
        Guid? _GetUpToDateLogicalThreadId(Guid? logicalThreadMemberId);
    }

    public class PollCallbackArg<T>
    {
        public T Value { get; set; }
        public long UptimeMillis { get; set; }
        public bool LastCall { get; set; }
    }

    public class PollCallbackRet<T>
    {
        public T NextValue { get; set; }
        public bool Stop { get; set; }
    }

    public class PromiseResult<T>
    {
        public static PromiseResult<T> CreateSuccess(T value)
        {
            return new PromiseResult<T>(true, false, value, null);
        }

        public static PromiseResult<T> CreateFailure(AggregateException ex)
        {
            return new PromiseResult<T>(false, false, default, ex);
        }

        public static PromiseResult<T> CreateCancellation(Task<T> t)
        {
            return new PromiseResult<T>(false, true, default,
                new AggregateException(new TaskCanceledException(t)));
        }

        private PromiseResult(bool successful, bool cancelled, 
            T successValue, AggregateException failureReason)
        {
            Successful = successful;
            Cancelled = cancelled;
            SuccessValue = successValue;
            FailureReason = failureReason;
        }

        public bool Successful { get; }
        public bool Cancelled { get; }
        public T SuccessValue { get; }
        public AggregateException FailureReason { get; }
    }

    /// <summary>
    /// Needed due to reified generics in csharp, which won't allow fetching these
    /// properties on any AbstractPromise instance regardless of its type parameter.
    /// </summary>
    public interface _ILogicalThreadMember
    {
        AbstractPromiseApi _PromiseApi { get; }
        Guid? LogicalThreadId { get; }
        void EndLogicalThread();
    }

    public interface AbstractPromise<T>: _ILogicalThreadMember
    {
        AbstractPromise<U> Then<U>(Func<T, U> onFulfilled);
        AbstractPromise<T> Catch(Action<AggregateException> onRejected);
        AbstractPromise<T> Finally(Action onFinally);
        AbstractPromise<U> ThenCompose<U>(Func<T, AbstractPromise<U>> onFulfilled);

        // In prescence of generics, CatchCompose has to return a result which is a supertype
        // of both the type of the current promise, and the type of the promise returned by onRejected.
        // Also couldn't cast AbstractPromise of VoidType to DefaultPromise of object
        // at runtime, even though object is a supertype of VoidType.
        // Hence these constraints forced us to this design of the method in which the type returned by
        // onRejected is the same as that of this one.
        AbstractPromise<T> CatchCompose(Func<AggregateException, AbstractPromise<T>> onRejected);
        AbstractPromise<U> ThenOrCatchCompose<U>(Func<T, AbstractPromise<U>> onFulfilled,
            Func<AggregateException, AbstractPromise<U>> onRejected);
    }

    public interface PromiseCompletionSource<T>
    {
        AbstractPromise<T> RelatedPromise { get; }
        void CompleteSuccessfully(T value);
        void CompleteExceptionally(Exception error);
    }
}
