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
    /// sequence numbers from application layer. 
    /// It also should be the only one to use PromiseCompletionSource; the rest of the project
    /// should only use AbstractPromise.
    /// <para>
    /// Also this implementation is intended to be interoperable with counterparts on other programming platforms.
    /// As such it makes certain assumptions and is closed for modification. It assumes
    /// 1. Limited non-extensible number of states.
    /// 2. The only opcodes around are data, close and their acks.
    /// 3. congestion control and data integrity are responsibilities of the underlying network transport
    ///    (except for duplication of previously sent PDUs, which it handles)
    /// 4. constant timeout values throughout its operation.
    /// 5. constant max retry count throughout its operation.
    /// </para>
    /// </summary>
    public class DefaultSessionHandler : IDefaultSessionHandler
    {
        public static readonly int StateOpen = 1;
        public static readonly int StateClosing = 2;
        public static readonly int StateDisposeAwaiting = 3;
        public static readonly int StateDisposed = 4;

        private List<ISessionStateHandler> _stateHandlers;
        private CloseHandler _closeHandler;

        private AbstractPromiseApi _promiseApi;
        private object _lastIdleTimeoutId;
        private object _lastAckTimeoutId;
        private SessionDisposedException _disposalCause;

        public DefaultSessionHandler()
        { }

        public void CompleteInit(string sessionId, bool configureForInitialSend,
            AbstractNetworkApi networkApi, GenericNetworkIdentifier remoteEndpoint)
        {
            NetworkApi = networkApi;
            TaskExecutor = networkApi.SessionTaskExecutor;
            RemoteEndpoint = remoteEndpoint;
            SessionId = sessionId;

            _promiseApi = networkApi.PromiseApi;

            _stateHandlers = new List<ISessionStateHandler>();
            _stateHandlers.Add(new ReceiveDataHandler(this));
            _stateHandlers.Add(new SendDataHandler(this));
            _closeHandler = new CloseHandler(this);
            _stateHandlers.Add(_closeHandler);

            // initialize session management parameters from network interface config.
            IdleTimeoutSecs = networkApi.IdleTimeoutSecs;
            MinRemoteIdleTimeoutSecs = networkApi.MinRemoteIdleTimeoutSecs;
            MaxRemoteIdleTimeoutSecs = networkApi.MaxRemoteIdleTimeoutSecs;
            AckTimeoutSecs = networkApi.AckTimeoutSecs;
            MaxRetryCount = networkApi.MaxRetryCount;
            MaximumTransferUnitSize = networkApi.MaximumTransferUnitSize;
            MaxSendWindowSize = networkApi.MaxSendWindowSize;
            MaxReceiveWindowSize = networkApi.MaxReceiveWindowSize;
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

        public AbstractNetworkApi NetworkApi { get; private set; }
        public GenericNetworkIdentifier RemoteEndpoint { get; private set; }
        public string SessionId { get; private set; }
        public ISessionTaskExecutor TaskExecutor { get; private set; }

        public int SessionState { get; set; } = StateOpen;

        public int MaxReceiveWindowSize { get; set; }
        public int MaxSendWindowSize { get; set; }
        public int MaximumTransferUnitSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int IdleTimeoutSecs { get; set; }
        public int MinRemoteIdleTimeoutSecs { get; set; }
        public int MaxRemoteIdleTimeoutSecs { get; set; }
        public int AckTimeoutSecs { get; set; }

        // Protocol requires initial value for window id to be 0,
        // and hence last window id should be negative to trigger
        // validation logic to expect 0.
        public long NextWindowIdToSend { get; set; } = 0;
        public long LastWindowIdReceived { get; set; } = -1;

        public int LastMaxSeqReceived { get; set; }
        public int? RemoteIdleTimeoutSecs { get; set; }

        public void IncrementNextWindowIdToSend()
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
            TaskExecutor.PostCallback(() =>
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
                    DiscardReceivedDatagram(datagram);
                }
            });
            return DefaultPromiseApi.CompletedPromise;
        }

        public AbstractPromise<VoidType> ProcessSendAsync(ProtocolMessage message)
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            TaskExecutor.PostCallback(() =>
            {
                if (SessionState >= StateDisposeAwaiting)
                {
                    TaskExecutor.CompletePromiseCallbackExceptionally(promiseCb, _disposalCause);
                }
                else if (IsSendInProgress())
                {
                    TaskExecutor.CompletePromiseCallbackExceptionally(promiseCb,
                        new Exception("Send is in progress"));
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
                        TaskExecutor.CompletePromiseCallbackExceptionally(promiseCb,
                            new Exception("No state handler found to process send"));
                    }
                }
            });
            return returnPromise;
        }

        public AbstractPromise<VoidType> CloseAsync()
        {
            return CloseAsync(true);
        }

        public AbstractPromise<VoidType> CloseAsync(bool closeGracefully)
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            TaskExecutor.PostCallback(() =>
            {
                if (SessionState == StateDisposed)
                {
                    TaskExecutor.CompletePromiseCallbackSuccessfully(promiseCb, VoidType.Instance);
                }
                else if (SessionState >= StateClosing)
                {
                    _closeHandler.QueueCallback(promiseCb);
                }
                else if (closeGracefully && IsSendInProgress())
                {
                    TaskExecutor.CompletePromiseCallbackExceptionally(promiseCb,
                        new Exception("Send is in progress"));
                }
                else
                {
                    ResetIdleTimeout();
                    var cause = new SessionDisposedException(false, closeGracefully ? ProtocolDatagram.AbortCodeNormalClose :
                        ProtocolDatagram.AbortCodeForceClose);
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
            return returnPromise;
        }

        public void ResetAckTimeout(int timeoutSecs, Action cb)
        {
            CancelAckTimeout();

            // interpret non positive timeout as disable ack timeout.
            if (timeoutSecs > 0)
            {
                _lastAckTimeoutId = TaskExecutor.ScheduleTimeout(timeoutSecs,
                    () => ProcessAckTimeout(cb));
            }
        }

        public void ResetIdleTimeout()
        {
            CancelIdleTimeout();

            int effectiveIdleTimeoutSecs = IdleTimeoutSecs;
            if (RemoteIdleTimeoutSecs.HasValue)
            {
                // accept remote idle timeout only if it is within bounds of min and max.
                if (RemoteIdleTimeoutSecs >= MinRemoteIdleTimeoutSecs && RemoteIdleTimeoutSecs <= MaxRemoteIdleTimeoutSecs)
                {
                    effectiveIdleTimeoutSecs = RemoteIdleTimeoutSecs.Value;
                }
            }

            // In the end, only positive values result in idle timeouts.
            if (effectiveIdleTimeoutSecs > 0)
            {
                _lastIdleTimeoutId = TaskExecutor.ScheduleTimeout(IdleTimeoutSecs, ProcessIdleTimeout);
            }
        }

        private void CancelIdleTimeout()
        {
            if (_lastIdleTimeoutId != null)
            {
                TaskExecutor.CancelTimeout(_lastIdleTimeoutId);
                _lastIdleTimeoutId = null;
            }
        }

        public void CancelAckTimeout()
        {
            if (_lastAckTimeoutId != null)
            {
                TaskExecutor.CancelTimeout(_lastAckTimeoutId);
                _lastAckTimeoutId = null;
            }
        }

        private void ProcessIdleTimeout()
        {
            _lastIdleTimeoutId = null;
            InitiateDispose(new SessionDisposedException(false, ProtocolDatagram.AbortCodeTimeout));
        }

        private void ProcessAckTimeout(Action cb)
        {
            _lastAckTimeoutId = null;
            if (SessionState < StateDisposeAwaiting)
            {
                cb.Invoke();
            }
        }

        public void DiscardReceivedDatagram(ProtocolDatagram datagram)
        {
            // subclasses can log more.

            CustomLoggerFacade.Log(() => new CustomLogEvent("ee37084b-2201-4591-b681-25b0398aba40", 
                "Discarding datagram: " + datagram, null));
        }

        public void InitiateDispose(SessionDisposedException cause)
        {
            InitiateDispose(cause, null);
        }

        public void InitiateDispose(SessionDisposedException cause, PromiseCompletionSource<VoidType> cb)
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

        public void ContinueDispose(SessionDisposedException cause)
        {
            _disposalCause = cause;

            // cancel all data handling activities.
            foreach (ISessionStateHandler stateHandler in _stateHandlers)
            {
                stateHandler.PrepareForDispose(cause);
            }

            SessionState = StateDisposeAwaiting;

            OnSessionDisposing(new SessionDisposingEventArgs { Cause = cause });

            NetworkApi.RequestSessionDispose(RemoteEndpoint, SessionId, cause);
        }

        public AbstractPromise<VoidType> FinaliseDisposeAsync(SessionDisposedException cause)
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            TaskExecutor.PostCallback(() =>
            {
                if (SessionState == StateDisposed)
                {
                    TaskExecutor.CompletePromiseCallbackSuccessfully(promiseCb, VoidType.Instance);
                }
                else
                {
                    CancelIdleTimeout();
                    CancelAckTimeout();

                    foreach (ISessionStateHandler stateHandler in _stateHandlers)
                    {
                        stateHandler.Dispose(cause);
                    }

                    SessionState = StateDisposed;

                    TaskExecutor.CompletePromiseCallbackSuccessfully(promiseCb, VoidType.Instance);

                    // pass on to application layer.
                    OnSessionDisposed(new SessionDisposedEventArgs { Cause = cause });
                }
            });
            return returnPromise;
        }

        // calls to application layer.
        // Contract here is that both calls should behave like notifications, and
        // hence these should be called from outside event loop if possible, but after current
        // event in event loop has been processed.

        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public void OnMessageReceived(MessageReceivedEventArgs e)
        {
            TaskExecutor.PostTask(() => MessageReceived?.Invoke(this, e));
        }

        public event EventHandler<SessionDisposingEventArgs> SessionDisposing;
        public void OnSessionDisposing(SessionDisposingEventArgs e)
        {
            TaskExecutor.PostTask(() => SessionDisposing?.Invoke(this, e));
        }

        public event EventHandler<SessionDisposedEventArgs> SessionDisposed;
        public void OnSessionDisposed(SessionDisposedEventArgs e)
        {
            TaskExecutor.PostTask(() => SessionDisposed?.Invoke(this, e));
        }
    }
}
