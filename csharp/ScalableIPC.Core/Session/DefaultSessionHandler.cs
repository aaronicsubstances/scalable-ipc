using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using ScalableIPC.Core.Helpers;
using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    /// <summary>
    /// So design of session handler default implementation is to hide acks, retries, window ids and
    /// sequence numbers from application layer. It incorporates 80% of data link layer design and
    /// 20% of transport design.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Specifically, these are the features:
    /// </para>
    /// <list type="number">
    /// <item>end to end assumption of communication endpoints</item>
    /// <item>end to end idle timeout specification</item>
    /// <item>packet integrity assumption</item>
    /// <item>guaranteed delivery via acknowlegements</item>
    /// <item>deal with out of order packet delivery</item>
    /// <item>deal with packet duplication - like in TCP</item>
    /// <item>retries upon timeouts via ARQ - go back N variant (designed for transport layer) 
    /// on sender side, selective repeat on receiver size.</item>
    /// <item>all send errors are transient by default. Fatal errors are the exception.</item>
    /// <item>flow control</item>
    /// <item>preservation of message boundaries</item>
    /// <item>no special startup or shutdown</item>
    /// <item>DOES NOT deal with congestion control and security</item>
    /// <item>deals only with unicast communications</item>
    /// </list>
    /// <para> 
    /// Also this implementation is intended to be interoperable with counterparts on other programming platforms.
    /// As such it makes certain assumptions and is closed for modification. It assumes
    /// </para>
    /// <list type="number">
    /// <item>Limited non-extensible number of states.</item>
    /// <item>The only opcodes around are data, close and their acks.</item>
    /// <item>constant default idle timeout value throughout its operation. Can handle overrides
    ///    from remote peer.</item>
    /// <item>constant max retry count and max retry time throughout its operation.</item>
    /// </list>
    /// </remarks>
    public class DefaultSessionHandler : IStandardSessionHandler
    {
        public static readonly int StateOpen = 1;
        public static readonly int StateClosing = 2;
        public static readonly int StateDisposeAwaiting = 3;
        public static readonly int StateDisposed = 4;

        private AbstractEventLoopApi _taskExecutor;
        private List<ISessionStateHandler> _stateHandlers;
        private CloseHandler _closeHandler;

        private object _lastIdleTimeoutId;
        private object _lastAckTimeoutId;
        private ProtocolOperationException _disposalCause;

        public DefaultSessionHandler()
        { }

        public void CompleteInit(string sessionId, bool configureForInitialSend,
            AbstractNetworkApi networkApi, GenericNetworkIdentifier remoteEndpoint)
        {
            NetworkApi = networkApi;
            RemoteEndpoint = remoteEndpoint;
            SessionId = sessionId;

            _taskExecutor = CreateEventLoop();

            _stateHandlers = new List<ISessionStateHandler>();
            _stateHandlers.Add(new ReceiveDataHandler(this));
            _stateHandlers.Add(new SendDataHandler(this));
            _stateHandlers.Add(new SendDataWithoutAckHandler(this));
            _closeHandler = new CloseHandler(this);
            _stateHandlers.Add(_closeHandler);
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

        public IFireAndForgetSendHandlerAssistant CreateFireAndForgetSendHandlerAssistant()
        {
            return new FireAndForgetSendHandlerAssistant(this);
        }

        public AbstractNetworkApi NetworkApi { get; private set; }
        public GenericNetworkIdentifier RemoteEndpoint { get; private set; }
        public string SessionId { get; private set; }

        public int SessionState { get; set; } = StateOpen;

        public int MaxRemoteWindowSize { get; set; }
        public int MaxWindowSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int MaxRetryPeriod { get; set; }
        public int IdleTimeout { get; set; }
        public int MinRemoteIdleTimeout { get; set; }
        public int MaxRemoteIdleTimeout { get; set; }
        public double FireAndForgetSendProbability { get; set; }

        // Protocol requires initial value for window id to be 0,
        // and hence last window id should be negative to trigger
        // validation logic to expect 0.
        public long NextWindowIdToSend { get; set; } = 0;
        public long LastWindowIdReceived { get; set; } = -1;

        public ProtocolDatagram LastAck { get; set; }
        public int? RemoteIdleTimeout { get; set; }

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

        public AbstractPromise<VoidType> ProcessReceiveAsync(ProtocolDatagram datagram)
        {
            PostEventLoopCallback(() =>
            {
                bool handled = false;
                if (SessionState < StateDisposeAwaiting)
                {
                    ResetIdleTimeout();
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
            });
            return NetworkApi.PromiseApi.CompletedPromise();
        }

        public AbstractPromise<VoidType> ProcessSendAsync(ProtocolMessage message)
        {
            PromiseCompletionSource<VoidType> promiseCb = NetworkApi.PromiseApi.CreateCallback<VoidType>(_taskExecutor);
            PostEventLoopCallback(() =>
            {
                if (SessionState >= StateDisposeAwaiting)
                {
                    promiseCb.CompleteExceptionally(_disposalCause);
                }
                else if (IsSendInProgress())
                {
                    promiseCb.CompleteExceptionally(new Exception("Send is in progress"));
                }
                else
                {
                    ResetIdleTimeout();
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
            });
            return promiseCb.RelatedPromise;
        }

        public AbstractPromise<bool> ProcessSendWithoutAckAsync(ProtocolMessage message)
        {
            PromiseCompletionSource<bool> promiseCb = NetworkApi.PromiseApi.CreateCallback<bool>(_taskExecutor);
            PostEventLoopCallback(() =>
            {
                if (SessionState >= StateDisposeAwaiting)
                {
                    promiseCb.CompleteExceptionally(_disposalCause);
                }
                else if (IsSendInProgress())
                {
                    promiseCb.CompleteExceptionally(new Exception("Send is in progress"));
                }
                else
                {
                    ResetIdleTimeout();
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
            });
            return promiseCb.RelatedPromise;
        }

        public AbstractPromise<VoidType> CloseAsync()
        {
            return CloseAsync(true);
        }

        public AbstractPromise<VoidType> CloseAsync(bool closeGracefully)
        {
            PromiseCompletionSource<VoidType> promiseCb = NetworkApi.PromiseApi.CreateCallback<VoidType>(_taskExecutor);
            PostEventLoopCallback(() =>
            {
                if (SessionState == StateDisposed)
                {
                    promiseCb.CompleteSuccessfully(VoidType.Instance);
                }
                else if (SessionState >= StateClosing)
                {
                    _closeHandler.QueueCallback(promiseCb);
                }
                else if (closeGracefully && IsSendInProgress())
                {
                    promiseCb.CompleteExceptionally(new Exception("Send is in progress"));
                }
                else
                {
                    ResetIdleTimeout();
                    var cause = new ProtocolOperationException(false, closeGracefully ? 
                        ProtocolOperationException.ErrorCodeNormalClose :
                        ProtocolOperationException.ErrorCodeForceClose);
                    if (closeGracefully)
                    {
                        InitiateDispose(cause, promiseCb);
                    }
                    else
                    {
                        ContinueDispose(cause);
                    }
                }
            });
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

        private void ProcessIdleTimeout()
        {
            _lastIdleTimeoutId = null;
            InitiateDispose(new ProtocolOperationException(false, ProtocolOperationException.ErrorCodeTimeout));
        }

        private void ProcessAckTimeout(Action cb)
        {
            _lastAckTimeoutId = null;
            if (SessionState < StateDisposeAwaiting)
            {
                cb.Invoke();
            }
        }

        public void InitiateDispose(ProtocolOperationException cause)
        {
            InitiateDispose(cause, null);
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
            });
            return promiseCb.RelatedPromise;
        }

        // event loop methods
        public void PostEventLoopCallback(Action cb)
        {
            _taskExecutor.PostCallback(() =>
            {
                try
                {
                    cb();
                }
                catch (Exception ex)
                {
                    RecordEventLoopCallbackException("c67258ee-0014-4a96-b3d3-3d340486b0ba", ex);
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
        public Action<ISessionHandler, ProtocolMessage> MessageReceivedHandler { get; set; }
        public Action<ISessionHandler, ProtocolOperationException> SessionDisposingHandler { get; set; }
        public Action<ISessionHandler, ProtocolOperationException> SessionDisposedHandler { get; set; }
        public Action<ISessionHandler, ProtocolOperationException> ReceiveErrorHandler { get; set; }
        public Action<ISessionHandler, ProtocolOperationException> SendErrorHandler { get; set; }

        public void OnDatagramDiscarded(ProtocolDatagram datagram)
        {
            PostEventLoopCallback(() => DatagramDiscardedHandler?.Invoke(this, datagram));
        }

        public void OnMessageReceived(ProtocolMessage message)
        {
            PostEventLoopCallback(() => MessageReceivedHandler?.Invoke(this, message));
        }

        public void OnSessionDisposing(ProtocolOperationException cause)
        {
            PostEventLoopCallback(() => SessionDisposingHandler?.Invoke(this, cause));
        }

        public void OnSessionDisposed(ProtocolOperationException cause)
        {
            PostEventLoopCallback(() => SessionDisposedHandler?.Invoke(this, cause));
        }

        public void OnSendError(ProtocolOperationException cause)
        {
            PostEventLoopCallback(() => SendErrorHandler?.Invoke(this, cause));
        }

        public void OnReceiveError(ProtocolOperationException cause)
        {
            PostEventLoopCallback(() => ReceiveErrorHandler?.Invoke(this, cause));
        }
    }
}
