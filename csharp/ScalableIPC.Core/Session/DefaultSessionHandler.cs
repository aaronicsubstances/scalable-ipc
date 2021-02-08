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
        private AbstractEventLoopApi _taskExecutor;
        private List<ISessionStateHandler> _stateHandlers;
        private SendDataHandler _sendHandler;
        private CloseHandler _closeHandler;

        private object _lastOpenTimeoutId;
        private object _lastIdleTimeoutId;
        private object _lastAckTimeoutId;
        private object _lastEnquireLinkTimeoutId;
        private int _enquireLinkCount = 0;
        private ProtocolOperationException _disposalCause;

        public DefaultSessionHandler()
        { }

        public void CompleteInit(string sessionId, AbstractNetworkApi networkApi, GenericNetworkIdentifier remoteEndpoint)
        {
            NetworkApi = networkApi;
            RemoteEndpoint = remoteEndpoint;
            SessionId = sessionId;

            _taskExecutor = CreateEventLoop();
            _sendHandler = new SendDataHandler(this);
            _closeHandler = new CloseHandler(this);

            _stateHandlers = new List<ISessionStateHandler>();
            _stateHandlers.Add(new ReceiveDataHandler(this));
            _stateHandlers.Add(_sendHandler);
            _stateHandlers.Add(_closeHandler);
            _stateHandlers.Add(new EnquireLinkHandler(this));

            ScheduleOpenTimeout();
        }

        public AbstractEventLoopApi CreateEventLoop()
        {
            return new DefaultEventLoopApi(NetworkApi.SessionTaskExecutorGroup);
        }

        public ISendWindowAssistant CreateSendWindowAssistant()
        {
            return new SendWindowAssistant(this);
        }

        public ISendHandlerAssistant CreateSendHandlerAssistant()
        {
            return new SendHandlerAssistant(this);
        }

        public AbstractNetworkApi NetworkApi { get; private set; }
        public GenericNetworkIdentifier RemoteEndpoint { get; private set; }
        public string SessionId { get; private set; }
        public int State { get; set; } = SessionState.Opening;

        public int MaxWindowSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int OpenTimeout { get; set; }
        public int IdleTimeout { get; set; }
        public int MinRemoteIdleTimeout { get; set; }
        public int MaxRemoteIdleTimeout { get; set; }
        public int EnquireLinkInterval { get; set; }
        public Func<int, int> EnquireLinkIntervalAlgorithm { get; set; }

        public long NextWindowIdToSend { get; set; }
        public long LastWindowIdReceived { get; set; } = -1; // so 0 can be accepted as an initial valid window id. 

        public ProtocolDatagram LastAck { get; set; }
        public bool OpenSuccessHandlerCalled { get; set; }
        public bool OpenedStateConfirmedForSend { get; set; }
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

        public AbstractPromise<VoidType> ProcessReceiveAsync(ProtocolDatagram datagram)
        {
            PostEventLoopCallback(() =>
            {
                bool handled = false;
                if (State < SessionState.DisposeAwaiting)
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
                if (State >= SessionState.DisposeAwaiting)
                {
                    promiseCb.CompleteExceptionally(_disposalCause);
                }
                else
                {
                    EnsureSendNotInProgress();
                    ResetIdleTimeout();
                    ScheduleEnquireLinkEvent(true);
                    _sendHandler.ProcessSend(message, promiseCb);
                }
            }, promiseCb);
            return promiseCb.RelatedPromise;
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
                if (State == SessionState.Disposed)
                {
                    promiseCb.CompleteSuccessfully(VoidType.Instance);
                }
                else if (State >= SessionState.Closing)
                {
                    _closeHandler.QueueCallback(promiseCb);
                }
                else
                {
                    if (closeGracefully)
                    {
                        EnsureSendNotInProgress();
                    }
                    var cause = new ProtocolOperationException(errorCode);
                    if (closeGracefully)
                    {
                        InitiateDisposeGracefully(cause, promiseCb);
                    }
                    else
                    {
                        InitiateDispose(cause);
                    }
                }
            }, promiseCb);
            return promiseCb.RelatedPromise;
        }

        public void ScheduleOpenTimeout()
        {
            if (OpenTimeout > 0)
            {
                _lastOpenTimeoutId = ScheduleEventLoopTimeout(OpenTimeout, ProcessOpenTimeout);
            }
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

        public void CancelOpenTimeout()
        {
            if (_lastOpenTimeoutId != null)
            {
                _taskExecutor.CancelTimeout(_lastOpenTimeoutId);
                _lastOpenTimeoutId = null;
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

        private void ProcessOpenTimeout()
        {
            _lastOpenTimeoutId = null;
            InitiateDispose(new ProtocolOperationException(ProtocolOperationException.ErrorCodeOpenTimeout));
        }

        private void ProcessIdleTimeout()
        {
            _lastIdleTimeoutId = null;
            InitiateDisposeGracefully(new ProtocolOperationException(ProtocolOperationException.ErrorCodeIdleTimeout), null);
        }

        private void ProcessAckTimeout(Action cb)
        {
            _lastAckTimeoutId = null;
            if (State < SessionState.DisposeAwaiting)
            {
                cb.Invoke();
            }
        }

        private void ProcessEnquireLinkTimerEvent()
        {
            if (State != SessionState.Opened)
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

        public void InitiateDisposeGracefully(ProtocolOperationException cause, PromiseCompletionSource<VoidType> cb)
        {
            if (State >= SessionState.Closing)
            {
                return;
            }

            State = SessionState.Closing;
            // check if null. may be timeout triggered.
            if (cb != null)
            {
                _closeHandler.QueueCallback(cb);
            }
            _closeHandler.ProcessSendClose(cause);
        }

        public void InitiateDispose(ProtocolOperationException cause)
        {
            if (State > SessionState.Closing)
            {
                return;
            }

            _disposalCause = cause;

            // cancel all data handling activities.
            foreach (ISessionStateHandler stateHandler in _stateHandlers)
            {
                if (stateHandler == _closeHandler)
                {
                    // skip until final disposal.
                    continue;
                }
                stateHandler.Dispose(cause);
            }

            State = SessionState.DisposeAwaiting;

            OnSessionDisposing(cause);

            NetworkApi.RequestSessionDispose(RemoteEndpoint, SessionId, cause);
        }

        public AbstractPromise<VoidType> FinaliseDisposeAsync(ProtocolOperationException cause)
        {
            PromiseCompletionSource<VoidType> promiseCb = NetworkApi.PromiseApi.CreateCallback<VoidType>(_taskExecutor);
            PostEventLoopCallback(() =>
            {
                if (State == SessionState.Disposed)
                {
                    promiseCb.CompleteSuccessfully(VoidType.Instance);
                }
                else
                {
                    CancelOpenTimeout();
                    CancelIdleTimeout();
                    CancelAckTimeout();
                    CancelEnquireLinkTimer();

                    // just in case this method was called abruptly, e.g. in the
                    // case of a shutdown, record dispose cause and trigger disposal again.
                    _disposalCause = cause;

                    foreach (ISessionStateHandler stateHandler in _stateHandlers)
                    {
                        stateHandler.Dispose(cause);
                    }

                    State = SessionState.Disposed;

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
        public Action<ISessionHandler, bool> OpenSuccessHandler { get; set; }
        public Action<ISessionHandler, ProtocolMessage> MessageReceivedHandler { get; set; }
        public Action<ISessionHandler, ProtocolOperationException> SessionDisposingHandler { get; set; }
        public Action<ISessionHandler, ProtocolOperationException> SessionDisposedHandler { get; set; }
        public Action<ISessionHandler, ProtocolOperationException> ReceiveErrorHandler { get; set; }
        public Action<ISessionHandler, ProtocolOperationException> SendErrorHandler { get; set; }
        public Action<ISessionHandler, int> EnquireLinkTimerFiredHandler { get; set; }
        public Action<ISessionHandler, ProtocolDatagram> EnquireLinkSuccessHandler { get; set; }

        public void OnDatagramDiscarded(ProtocolDatagram datagram)
        {
            if (DatagramDiscardedHandler != null)
            {
                PostEventLoopCallback(() => DatagramDiscardedHandler?.Invoke(this, datagram), null);
            }
        }

        public void OnOpenSuccess(bool onReceive)
        {
            if (OpenSuccessHandler != null)
            {
                PostEventLoopCallback(() => OpenSuccessHandler?.Invoke(this, onReceive), null);
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

        public void OnEnquireLinkSuccess(ProtocolDatagram datagram)
        {
            if (EnquireLinkSuccessHandler != null)
            {
                PostEventLoopCallback(() => EnquireLinkSuccessHandler?.Invoke(this, datagram), null);
            }
        }
    }
}
