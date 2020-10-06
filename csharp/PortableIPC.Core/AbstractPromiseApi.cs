using System;

namespace PortableIPC.Core
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
        AbstractPromise<T> Create<T>(PromiseExecutorCallback<T> code);
        AbstractPromise<T> Resolve<T>(T value);
        AbstractPromise<VoidType> Reject(Exception reason);

        object ScheduleTimeout(IStoredCallback<int> cb, long millis);
        void CancelTimeout(object id);
    }

    public interface AbstractPromiseOnHold<T>
    {
        AbstractPromise<T> Extract();
        void CompleteSuccessfully(T value);
        void CompleteExceptionally(Exception error);
    }

    public interface IStoredCallback<in T>
    {
        void Run();
    }

    public class DefaultStoredCallback<T> : IStoredCallback<T>
    {
        public DefaultStoredCallback(Action<T> callback, T arg = default)
        {
            Callback = callback;
            Arg = arg;
        }

        public Action<T> Callback { get; }
        public T Arg { get; }
        public void Run()
        {
            Callback.Invoke(Arg);
        }
    }

    public interface AbstractPromise<out T>
    {
        AbstractPromise<U> Then<U>(FulfilmentCallback<T, U> onFulfilled, RejectionCallback onRejected = null);
        AbstractPromise<U> ThenCompose<U>(FulfilmentCallback<T, AbstractPromise<U>> onFulfilled,
            FulfilmentCallback<Exception, AbstractPromise<U>> onRejected = null);
    }

    public delegate void PromiseExecutorCallback<out T>(Action<T> resolve, Action<Exception> reject);

    public delegate U FulfilmentCallback<in T, out U>(T value);
    public delegate void RejectionCallback(Exception reason);
}
