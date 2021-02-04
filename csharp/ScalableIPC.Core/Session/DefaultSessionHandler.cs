using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using ScalableIPC.Core.Helpers;
using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    /// <summary>
    /// This implementation is intended to be interoperable with counterparts on other programming platforms.
    /// As such it makes certain assumptions and is closed for modification. 
    /// </summary>
    /// <remarks>
    /// <para> 
    /// It assumes
    /// </para>
    /// <list type="number">
    /// <item>Limited non-extensible number of states.</item>
    /// <item>The only opcodes around are open, data, close, enquire link and their acks.</item>
    /// <item>constant default idle timeout value throughout its operation. Can handle overrides
    ///    from remote peer.</item>
    /// <item>constant max retry count throughout its operation.</item>
    /// </list>
    /// </remarks>
    public class DefaultSessionHandler : IStandardSessionHandler
    {
        public static readonly int StateOpening = 1;
        public static readonly int StateOpen = 3;
        public static readonly int StateClosing = 5;
        public static readonly int StateDisposeAwaiting = 7;
        public static readonly int StateDisposed = 9;

        private AbstractEventLoopApi _taskExecutor;
        private List<ISessionStateHandler> _stateHandlers;
        private CloseHandler _closeHandler;

        private object _lastIdleTimeoutId;
        private object _lastAckTimeoutId;
        private object _lastEnquireLinkTimeoutId;
        private int _enquireLinkCount = 0;
        private ProtocolOperationException _disposalCause;

        public DefaultSessionHandler()
        { }

        public void CompleteInit(string sessionId, bool configureForSendOpen,
            AbstractNetworkApi networkApi, GenericNetworkIdentifier remoteEndpoint)
        {
            NetworkApi = networkApi;
            RemoteEndpoint = remoteEndpoint;
            SessionId = sessionId;
            ConfiguredForSendOpen = configureForSendOpen;

            _taskExecutor = CreateEventLoop();

            _stateHandlers = new List<ISessionStateHandler>();
            _stateHandlers.Add(new ReceiveDataHandler(this));
            _stateHandlers.Add(new SendDataHandler(this));
            _stateHandlers.Add(new SendDataWithoutAckHandler(this));
            _closeHandler = new CloseHandler(this);
            _stateHandlers.Add(_closeHandler);
            if (configureForSendOpen)
            {
                _stateHandlers.Add(new SendOpenHandler(this));
            }
            else
            {
                _stateHandlers.Add(new ReceiveOpenHandler(this));
            }
        }

        public AbstractEventLoopApi CreateEventLoop()
        {
            return new DefaultEventLoopApi(NetworkApi.SessionTaskExecutorGroup);
        }

        public ISendHandlerAssistant CreateSendHandlerAssistant()
        {
            return new SendHandlerAssistant(this);
        }

        public IRetrySendHandlerAssistant CreateRetrySendHandlerAssistant()
        {
            return new RetrySendHandlerAssistant(this);
        }

        public IReceiveHandlerAssistant CreateReceiveHandlerAssistant()
        {
            return new ReceiveHandlerAssistant(this);
        }

        public IReceiveOpenHandlerAssistant CreateReceiveOpenHandlerAssistant()
        {
            return new ReceiveOpenHandlerAssistant(this);
        }

        public IFireAndForgetSendHandlerAssistant CreateFireAndForgetSendHandlerAssistant()
        {
            return new FireAndForgetSendHandlerAssistant(this);
        }

        public AbstractNetworkApi NetworkApi { get; private set; }
        public GenericNetworkIdentifier RemoteEndpoint { get; private set; }
        public string SessionId { get; private set; }
        public bool ConfiguredForSendOpen { get; private set; }
        public int SessionState { get; set; } = StateOpening;

        public int MaxWindowSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int IdleTimeout { get; set; }
        public int MinRemoteIdleTimeout { get; set; }
        public int MaxRemoteIdleTimeout { get; set; }
        public double FireAndForgetSendProbability { get; set; }
        public int EnquireLinkInterval { get; set; }
        public Func<int, int> EnquireLinkIntervalAlgorithm { get; set; }

        // Protocol requires initial value for window id to be 0,
        // and hence last window id should be negative to trigger
        // validation logic to expect 0.
        public long NextWindowIdToSend { get; set; } = 0;
        public long LastWindowIdReceived { get; set; } = -1;

        public ProtocolDatagram LastAck { get; set; }
        public int? RemoteIdleTimeout { get; set; }
        public int? RemoteMaxWindowSize { get; set; }

        public virtual void IncrementNextWindowIdToSend()
        {
            NextWindowIdToSend = ProtocolDatagram.ComputeNextWindowIdToSend(NextWindowIdToSend);
        }

        public bool IsSendInProgress()
        {
            foreach (var handler in _stateHandlers)
            {
                if (handler.SendInProgress)
                {
                    return true;
                }
            }
            return false;
        }

        public void EnsureSendNotInProgress()
        {
            if (IsSendInProgress())
            {
                throw new Exception("Send is in progress");
            }
        }

        public AbstractPromise<VoidType> ProcessOpenAsync()
        {
            PromiseCompletionSource<VoidType> promiseCb = NetworkApi.PromiseApi.CreateCallback<VoidType>(_taskExecutor);
            PostEventLoopCallback(() =>
            {
                if (SessionState >= StateDisposeAwaiting)
                {
                    promiseCb.CompleteExceptionally(_disposalCause);
                }                
                else
                {
                    EnsureSendNotInProgress();
                    ResetIdleTimeout();
                    ScheduleEnquireLinkEvent(true);
                    bool handled = false;
                    foreach (ISessionStateHandler stateHandler in _stateHandlers)
                    {
                        handled = stateHandler.ProcessOpen(promiseCb);
                        if (handled)
                        {
                            break;
                        }
                    }
                    if (!handled)
                    {
                        promiseCb.CompleteExceptionally(new Exception("No state handler found to process open"));
                    }
                }
            }, promiseCb);
            return promiseCb.RelatedPromise;
        }

        public AbstractPromise<VoidType> ProcessReceiveAsync(ProtocolDatagram datagram)
        {
            PostEventLoopCallback(() =>
            {
                bool handled = false;
                if (SessionState < StateDisposeAwaiting)
                {
                    ResetIdleTimeout();
                    ScheduleEnquireLinkEvent(true);
                    foreach (ISessionStateHandler stateHandler in _stateHandlers)
                    {
                        handled = stateHandler.ProcessReceive(datagram);
                        if (handled)
                        {
                            break;
                        }
                    }
                }
                if (!handled)
                {
                    OnDatagramDiscarded(datagram);
                }
            }, null);
            return NetworkApi.PromiseApi.CompletedPromise();
        }

        public AbstractPromise<VoidType> SendAsync(ProtocolMessage message)
        {
            PromiseCompletionSource<VoidType> promiseCb = NetworkApi.PromiseApi.CreateCallback<VoidType>(_taskExecutor);
            PostEventLoopCallback(() =>
            {
                if (SessionState >= StateDisposeAwaiting)
                {
                    promiseCb.CompleteExceptionally(_disposalCause);
                }
                else
                {
                    EnsureSendNotInProgress();
                    ResetIdleTimeout();
                    ScheduleEnquireLinkEvent(true);
                    bool handled = false;
                    foreach (ISessionStateHandler stateHandler in _stateHandlers)
                    {
                        handled = stateHandler.ProcessSend(message, promiseCb);
                        if (handled)
                        {
                            break;
                        }
                    }
                    if (!handled)
                    {
                        promiseCb.CompleteExceptionally(new Exception("No state handler found to process send"));
                    }
                }
            }, promiseCb);
            return promiseCb.RelatedPromise;
        }

        public AbstractPromise<bool> SendWithoutAckAsync(ProtocolMessage message)
        {
            PromiseCompletionSource<bool> promiseCb = NetworkApi.PromiseApi.CreateCallback<bool>(_taskExecutor);
            PostEventLoopCallback(() =>
            {
                if (SessionState >= StateDisposeAwaiting)
                {
                    promiseCb.CompleteExceptionally(_disposalCause);
                }
                else
                {
                    EnsureSendNotInProgress();
                    ResetIdleTimeout();
                    ScheduleEnquireLinkEvent(true);
                    bool handled = false;
                    foreach (ISessionStateHandler stateHandler in _stateHandlers)
                    {
                        handled = stateHandler.ProcessSendWithoutAck(message, promiseCb);
                        if (handled)
                        {
                            break;
                        }
                    }
                    if (!handled)
                    {
                        promiseCb.CompleteExceptionally(new Exception("No state handler found to process send without ack"));
                    }
                }
            }, WrapPromiseCbForEventLoopPost(promiseCb));
            return promiseCb.RelatedPromise;
        }

        /// <summary>
        /// Work around reified generics by converting argument to one with VoidType
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="wrapped"></param>
        /// <returns></returns>
        private PromiseCompletionSource<VoidType> WrapPromiseCbForEventLoopPost<T>(PromiseCompletionSource<T> wrapped)
        {
            var promiseCb = NetworkApi.PromiseApi.CreateCallback<VoidType>(null);
            promiseCb.RelatedPromise.CatchCompose(ex =>
            {
                wrapped.CompleteExceptionally(ex);
                return NetworkApi.PromiseApi.CompletedPromise();
            });
            // prevent promise cb from hanging by completing it no matter what.
            wrapped.RelatedPromise.Finally(() => promiseCb.CompleteSuccessfully(VoidType.Instance));
            return promiseCb;
        }

        public AbstractPromise<VoidType> CloseAsync()
        {
            return CloseAsync(ProtocolOperationException.ErrorCodeNormalClose);
        }

        public AbstractPromise<VoidType> CloseAsync(int errorCode)
        {
            PromiseCompletionSource<VoidType> promiseCb = NetworkApi.PromiseApi.CreateCallback<VoidType>(_taskExecutor);
            PostEventLoopCallback(() =>
            {
                var closeGracefully = errorCode == ProtocolOperationException.ErrorCodeNormalClose;
                if (SessionState == StateDisposed)
                {
                    promiseCb.CompleteSuccessfully(VoidType.Instance);
                }
                else if (SessionState >= StateClosing)
                {
                    _closeHandler.QueueCallback(promiseCb);
                }
                else
                {
                    if (closeGracefully)
                    {
                        EnsureSendNotInProgress();
                    }
                    ResetIdleTimeout();
                    ScheduleEnquireLinkEvent(true);
                    var cause = new ProtocolOperationException(false, errorCode);
                    if (closeGracefully)
                    {
                        InitiateDispose(cause, promiseCb);
                    }
                    else
                    {
                        ContinueDispose(cause);
                    }
                }
            }, promiseCb);
            return promiseCb.RelatedPromise;
        }

        public void ResetAckTimeout(int timeout, Action cb)
        {
            CancelAckTimeout();

            // interpret non positive timeout as disable ack timeout.
            if (timeout > 0)
            {
                _lastAckTimeoutId = ScheduleEventLoopTimeout(timeout,
                    () => ProcessAckTimeout(cb));
            }
        }

        public void ResetIdleTimeout()
        {
            CancelIdleTimeout();

            int effectiveIdleTimeout = IdleTimeout;
            if (RemoteIdleTimeout.HasValue)
            {
                // accept remote idle timeout only if it is within bounds of min and max.
                if (RemoteIdleTimeout >= MinRemoteIdleTimeout && RemoteIdleTimeout <= MaxRemoteIdleTimeout)
                {
                    effectiveIdleTimeout = RemoteIdleTimeout.Value;
                }
            }

            // In the end, only positive values result in idle timeouts.
            if (effectiveIdleTimeout > 0)
            {
                _lastIdleTimeoutId = ScheduleEventLoopTimeout(IdleTimeout, ProcessIdleTimeout);
            }
        }

        public void ScheduleEnquireLinkEvent(bool reset)
        {
            CancelEnquireLinkTimer();

            if (reset)
            {
                _enquireLinkCount = 0;
            }

            int effectiveEnquireLinkInterval = EnquireLinkInterval;
            if (EnquireLinkIntervalAlgorithm != null)
            {
                try
                {
                    effectiveEnquireLinkInterval = EnquireLinkIntervalAlgorithm.Invoke(_enquireLinkCount);
                }
                catch (Exception ex)
                {
                    CustomLoggerFacade.Log(() =>
                        new CustomLogEvent(GetType(), 
                            $"Enquire link algorithm failed at count {_enquireLinkCount}", ex)
                        .AddProperty(CustomLogEvent.LogDataKeySessionId, SessionId)
                        .AddProperty(CustomLogEvent.LogDataKeyLogPositionId, "773ad098-ff60-480c-b9c2-fcf8be4c1f34"));
                }
            }
            if (effectiveEnquireLinkInterval > 0)
            {
                _lastEnquireLinkTimeoutId = ScheduleEventLoopTimeout(effectiveEnquireLinkInterval,
                    ProcessEnquireLinkTimerEvent);
                _enquireLinkCount++;
            }
        }

        private void CancelIdleTimeout()
        {
            if (_lastIdleTimeoutId != null)
            {
                _taskExecutor.CancelTimeout(_lastIdleTimeoutId);
                _lastIdleTimeoutId = null;
            }
        }

        public void CancelAckTimeout()
        {
            if (_lastAckTimeoutId != null)
            {
                _taskExecutor.CancelTimeout(_lastAckTimeoutId);
                _lastAckTimeoutId = null;
            }
        }

        private void CancelEnquireLinkTimer()
        {
            if (_lastEnquireLinkTimeoutId != null)
            {
                _taskExecutor.CancelTimeout(_lastEnquireLinkTimeoutId);
                _lastEnquireLinkTimeoutId = null;
            }
        }

        private void ProcessIdleTimeout()
        {
            _lastIdleTimeoutId = null;
            InitiateDispose(new ProtocolOperationException(false, 
                ProtocolOperationException.ErrorCodeIdleTimeout), null);
        }

        private void ProcessAckTimeout(Action cb)
        {
            _lastAckTimeoutId = null;
            if (SessionState < StateDisposeAwaiting)
            {
                cb.Invoke();
            }
        }

        private void ProcessEnquireLinkTimerEvent()
        {
            if (SessionState != StateOpen)
            {
                // stop timer.
                return;
            }

            OnEnquireLinkTimerFired();

            var enquireLinkDatagram = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeEnquireLink,
                SessionId = SessionId
            };
            NetworkApi.RequestSend(RemoteEndpoint, enquireLinkDatagram, null, null);
            ScheduleEnquireLinkEvent(false);
        }

        public void InitiateDispose(ProtocolOperationException cause, PromiseCompletionSource<VoidType> cb)
        {
            if (SessionState >= StateClosing)
            {
                return;
            }

            SessionState = StateClosing;
            // check if null. may be timeout triggered.
            if (cb != null)
            {
                _closeHandler.QueueCallback(cb);
            }
            _closeHandler.ProcessSendClose(cause);
        }

        public void ContinueDispose(ProtocolOperationException cause)
        {
            _disposalCause = cause;

            // cancel all data handling activities.
            foreach (ISessionStateHandler stateHandler in _stateHandlers)
            {
                stateHandler.PrepareForDispose(cause);
            }

            SessionState = StateDisposeAwaiting;

            OnSessionDisposing(cause);

            NetworkApi.RequestSessionDispose(RemoteEndpoint, SessionId, cause);
        }

        public AbstractPromise<VoidType> FinaliseDisposeAsync(ProtocolOperationException cause)
        {
            PromiseCompletionSource<VoidType> promiseCb = NetworkApi.PromiseApi.CreateCallback<VoidType>(_taskExecutor);
            PostEventLoopCallback(() =>
            {
                if (SessionState == StateDisposed)
                {
                    promiseCb.CompleteSuccessfully(VoidType.Instance);
                }
                else
                {
                    CancelIdleTimeout();
                    CancelAckTimeout();
                    CancelEnquireLinkTimer();

                    // just in case this method was called abruptly, e.g. in the
                    // case of a shutdown, record dispose cause again.
                    _disposalCause = cause;

                    foreach (ISessionStateHandler stateHandler in _stateHandlers)
                    {
                        stateHandler.Dispose(cause);
                    }

                    SessionState = StateDisposed;

                    promiseCb.CompleteSuccessfully(VoidType.Instance);

                    // pass on to application layer.
                    OnSessionDisposed(cause);
                }
            }, promiseCb);
            return promiseCb.RelatedPromise;
        }

        // event loop methods
        public void PostEventLoopCallback(Action cb, PromiseCompletionSource<VoidType> promiseCb)
        {
            _taskExecutor.PostCallback(() =>
            {
                try
                {
                    cb();
                }
                catch (Exception ex)
                {
                    if (promiseCb != null)
                    {
                        promiseCb.CompleteExceptionally(ex);
                    }
                    else
                    {
                        RecordEventLoopCallbackException("c67258ee-0014-4a96-b3d3-3d340486b0ba", ex);
                    }
                }
            });
        }

        private object ScheduleEventLoopTimeout(int millis, Action cb)
        {
            return _taskExecutor.ScheduleTimeout(millis, () =>
            {
                try
                {
                    cb();
                }
                catch (Exception ex)
                {
                    RecordEventLoopCallbackException("909b8db5-e52a-47f9-91e7-4233a852efd0", ex);
                }
            });
        }

        private void RecordEventLoopCallbackException(string logPosition, Exception ex)
        {
            CustomLoggerFacade.Log(() => new CustomLogEvent(GetType(),
                "Error occured on event loop during callback processing", ex)
                   .AddProperty(CustomLogEvent.LogDataKeySessionId, SessionId)
                   .AddProperty(CustomLogEvent.LogDataKeyLogPositionId, logPosition));
        }

        // calls to application layer.
        // Contract here is that these calls should behave like notifications, and
        // hence these once invoked, should continue execution outside event loop if possible, but after current
        // event in event loop has been processed.
        public Action<ISessionHandler, ProtocolDatagram> DatagramDiscardedHandler { get; set; }
        public Action<ISessionHandler> OpenSuccessHandler { get; set; }
        public Action<ISessionHandler, ProtocolMessage> MessageReceivedHandler { get; set; }
        public Action<ISessionHandler, ProtocolOperationException> SessionDisposingHandler { get; set; }
        public Action<ISessionHandler, ProtocolOperationException> SessionDisposedHandler { get; set; }
        public Action<ISessionHandler, ProtocolOperationException> ReceiveErrorHandler { get; set; }
        public Action<ISessionHandler, ProtocolOperationException> SendErrorHandler { get; set; }
        public Action<ISessionHandler, int> EnquireLinkTimerFiredHandler { get; set; }

        public void OnDatagramDiscarded(ProtocolDatagram datagram)
        {
            if (DatagramDiscardedHandler != null)
            {
                PostEventLoopCallback(() => DatagramDiscardedHandler?.Invoke(this, datagram), null);
            }
        }

        public void OnOpenSuccess()
        {
            SessionState = StateOpen;
            if (OpenSuccessHandler != null)
            {
                PostEventLoopCallback(() => OpenSuccessHandler?.Invoke(this), null);
            }
        }

        public void OnMessageReceived(ProtocolMessage message)
        {
            if (MessageReceivedHandler != null)
            {
                PostEventLoopCallback(() => MessageReceivedHandler?.Invoke(this, message), null);
            }
        }

        public void OnSessionDisposing(ProtocolOperationException cause)
        {
            if (SessionDisposingHandler != null)
            {
                PostEventLoopCallback(() => SessionDisposingHandler?.Invoke(this, cause), null);
            }
        }

        public void OnSessionDisposed(ProtocolOperationException cause)
        {
            if (SessionDisposedHandler != null)
            {
                PostEventLoopCallback(() => SessionDisposedHandler?.Invoke(this, cause), null);
            }
        }

        public void OnSendError(ProtocolOperationException cause)
        {
            if (SendErrorHandler != null)
            {
                PostEventLoopCallback(() => SendErrorHandler?.Invoke(this, cause), null);
            }
        }

        public void OnReceiveError(ProtocolOperationException cause)
        {
            if (ReceiveErrorHandler != null)
            {
                PostEventLoopCallback(() => ReceiveErrorHandler?.Invoke(this, cause), null);
            }
        }

        public void OnEnquireLinkTimerFired()
        {
            if (EnquireLinkTimerFiredHandler != null)
            {
                PostEventLoopCallback(() => EnquireLinkTimerFiredHandler?.Invoke(this, _enquireLinkCount), null);
            }
        }
    }
}
