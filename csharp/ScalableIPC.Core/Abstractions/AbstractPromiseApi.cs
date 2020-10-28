using System;

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
    ///  Conclusion: Have a Cancel API which works only for timeouts
    /// 
    /// 3. Rejection handlers in NodeJS can return values and continue like no error happened.
    ///  - not so in C#. an error in async-await keyword usage results in an exception 
    ///  Conclusion: only accept exceptions in rejection handlers, but allow them to return values.
    /// </summary>
    public interface AbstractPromiseApi
    {
        PromiseCompletionSource<T> CreateCallback<T>();
        AbstractPromise<T> Resolve<T>(T value);
        AbstractPromise<VoidType> Reject(Exception reason);
    }

    public interface AbstractPromise<out T>
    {
        AbstractPromise<U> Then<U>(Func<T, U> onFulfilled, Action<Exception> onRejected = null);
        AbstractPromise<U> ThenCompose<U>(Func<T, AbstractPromise<U>> onFulfilled,
            Func<Exception, AbstractPromise<U>> onRejected = null);
    }

    public interface PromiseCompletionSource<T>
    {
        AbstractPromise<T> Extract();

        // Contract here is that both Complete* methods should behave like notifications, and
        // hence these should be called from event loop.
        void CompleteSuccessfully(T value);
        void CompleteExceptionally(Exception error);
    }
}
