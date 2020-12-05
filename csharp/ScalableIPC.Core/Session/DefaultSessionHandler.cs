using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
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
    /// As such it makes certain assumptions and is largely closed for modification. It assumes
    /// 1. Limited non-extensible number of states.
    /// 2. The only opcodes around are data, ack and close.
    /// 3. congestion control is concern of the underlying network transport.
    /// 4. constant timeout values throughout its operation.
    /// 5. constant max retry count throughout its operation.
    /// </para>
    /// </summary>
    public class DefaultSessionHandler : ISessionHandler
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

        public virtual void CompleteInit(string sessionId, bool configureForInitialSend,
            AbstractNetworkApi networkApi, GenericNetworkIdentifier remoteEndpoint)
        {
            NetworkApi = networkApi;
            EventLoop = networkApi.EventLoop;
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

        public AbstractNetworkApi NetworkApi { get; private set; }
        public GenericNetworkIdentifier RemoteEndpoint { get; private set; }
        public string SessionId { get; private set; }
        public AbstractEventLoopApi EventLoop { get; private set; }

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

        public virtual void IncrementNextWindowIdToSend()
        {
            NextWindowIdToSend = ProtocolDatagram.ComputeNextWindowIdToSend(NextWindowIdToSend);
        }

        public virtual bool IsSendInProgress()
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

        public virtual AbstractPromise<VoidType> ProcessReceiveAsync(ProtocolDatagram message)
        {
            EventLoop.PostCallback(() =>
            {
                Log("163c3ed3-0e9d-40a7-abff-b95310bfe200", message, "Session ProcessReceive");

                bool handled = false;
                if (SessionState < StateDisposeAwaiting)
                {
                    ResetIdleTimeout();
                    foreach (ISessionStateHandler stateHandler in _stateHandlers)
                    {
                        handled = stateHandler.ProcessReceive(message);
                        if (handled)
                        {
                            break;
                        }
                    }
                }
                if (!handled)
                {
                    DiscardReceivedMessage(message);
                }
            });
            return DefaultPromiseApi.CompletedPromise;
        }

        public virtual AbstractPromise<VoidType> ProcessSendAsync(ProtocolDatagram message)
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>(this);
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                Log("5abd8c58-4f14-499c-ad0e-788d59c5f7e2", message, "Session ProcessSend");

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
            return returnPromise;
        }

        public virtual AbstractPromise<VoidType> CloseAsync()
        {
            return CloseAsync(true);
        }

        public virtual AbstractPromise<VoidType> CloseAsync(bool closeGracefully)
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>(this);
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                Log("0447a0ef-5963-457c-a290-7026bed7f372", null, "Session Close");

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
                    var cause = new SessionDisposedException(false, closeGracefully ? ProtocolDatagram.AbortCodeNormalClose :
                        ProtocolDatagram.AbortCodeForceClose);
                    InitiateDispose(cause, promiseCb);
                }
            });
            return returnPromise;
        }

        public virtual void PostIfNotDisposed(Action cb)
        {
            EventLoop.PostCallback(() =>
            {
                if (SessionState < StateDisposeAwaiting)
                {
                    cb.Invoke();
                }
                else
                {
                    Log("49678d2f-518b-4cf1-b29f-4d3ceb74f3ec", "Skipping callback processing because session is disposed or about to be");
                }
            });
        }

        public virtual void ResetAckTimeout(int timeoutSecs, Action cb)
        {
            Log("54c44637-3efe-4a35-a674-22e8e12c48cc", "About to set ack timeout");

            CancelAckTimeout();
            // interpret non positive timeout as disable ack timeout.
            if (timeoutSecs > 0)
            {
                _lastAckTimeoutId = EventLoop.ScheduleTimeout(timeoutSecs,
                    () => ProcessAckTimeout(cb));
            }
        }

        public virtual void ResetIdleTimeout()
        {
            Log("41f243a1-db75-4c08-82fa-b2c7ff7dfda6", "About to reset idle timeout");
            
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
                _lastIdleTimeoutId = EventLoop.ScheduleTimeout(IdleTimeoutSecs, ProcessIdleTimeout);
            }
        }

        private void CancelIdleTimeout()
        {
            if (_lastIdleTimeoutId != null)
            {
                EventLoop.CancelTimeout(_lastIdleTimeoutId);
                _lastIdleTimeoutId = null;
            }
        }

        public virtual void CancelAckTimeout()
        {
            if (_lastAckTimeoutId != null)
            {
                EventLoop.CancelTimeout(_lastAckTimeoutId);
                _lastAckTimeoutId = null;
            }
        }

        private void ProcessIdleTimeout()
        {
            _lastIdleTimeoutId = null;

            Log("33b0e81b-c4fa-4a78-9cb7-0900e60afe3e", "Idle timeout has occured on session");

            InitiateDispose(new SessionDisposedException(false, ProtocolDatagram.AbortCodeTimeout), null);
        }

        private void ProcessAckTimeout(Action cb)
        {
            _lastAckTimeoutId = null;
            if (SessionState >= StateDisposeAwaiting)
            {
                Log("deec47ed-7c13-4e4e-9fd6-030aad245458", "Ignoring ack timeout since session is disposed or about to be");
                return;
            }

            Log("4a130328-aa6e-46eb-81ca-5a705e3d0995", "Ack timeout has occured on session");

            cb.Invoke();
        }

        public virtual void DiscardReceivedMessage(ProtocolDatagram message)
        {
            // subclasses can log more.

            Log("ee37084b-2201-4591-b681-25b0398aba40", message, "Discarding message");
        }
        
        public virtual void Log(string logPosition, ProtocolDatagram pdu, string message, params object[] args)
        {
            CustomLoggerFacade.Log(() =>
            {
                var customEvent = new CustomLogEvent(logPosition, message, null);
                customEvent.FillData("localEndpoint", NetworkApi.LocalEndpoint.ToString());
                customEvent.FillData("sessionId", SessionId);
                customEvent.FillData(pdu);
                customEvent.FillData(args);
                return customEvent;
            });
        }

        public virtual void Log(string logPosition, string message, params object[] args)
        {
            CustomLoggerFacade.Log(() =>
            {
                var customEvent = new CustomLogEvent(logPosition, message, null);
                customEvent.FillData("localEndpoint", NetworkApi.LocalEndpoint.ToString());
                customEvent.FillData("sessionId", SessionId);
                customEvent.FillData(args);
                return customEvent;
            });
        }

        public virtual void InitiateDispose(SessionDisposedException cause, PromiseCompletionSource<VoidType> cb)
        {
            if (SessionState == StateDisposed)
            {
                Log("3f2b1897-7c52-4693-95e6-413c6de47915", "Session already disposed, so skipping dispose initation");
                return;
            }

            if (SessionState >= StateClosing)
            {
                Log("ed8793fb-2fbe-4f14-b385-d134817f1554", "Session already disposing, so skipping dispose initation");
                return;
            }

            Log("890ef817-b90c-45fc-9243-b809c684c730", "Session disposal started");
            SessionState = StateClosing;
            _disposalCause = cause;
            _closeHandler.ProcessSendClose(cause, cb);
        }

        public virtual void ContinueDispose(SessionDisposedException cause)
        {
            Log("65c44e33-acd9-43fa-986d-7de9044f6124", "Continuing session disposal");
            SessionState = StateDisposeAwaiting;
            _disposalCause = cause;

            // cancel all send data handling activities.
            foreach (ISessionStateHandler stateHandler in _stateHandlers)
            {
                stateHandler.PrepareForDispose(cause);
            }

            NetworkApi.RequestSessionDispose(RemoteEndpoint, SessionId, cause);
        }

        public virtual AbstractPromise<VoidType> FinaliseDisposeAsync(SessionDisposedException cause)
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>(this);
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                if (SessionState == StateDisposed)
                {
                    Log("7e0fcf79-0c6d-41ff-9d73-5b4103d49717", "Session is already disposed");
                }
                else
                {
                    Log("e9d228bb-e00d-4002-8fe8-81df4a21dc41", "Completing session disposal", "cause", cause);

                    CancelIdleTimeout();
                    CancelAckTimeout();

                    foreach (ISessionStateHandler stateHandler in _stateHandlers)
                    {
                        stateHandler.Dispose(cause);
                    }

                    SessionState = StateDisposed;

                    Log("bd25f41a-32b0-4f5d-bd93-d8f348bd3e83", "Session disposal completed");

                    // pass on to application layer. NB: all calls to application layer must go through
                    // event loop.
                    EventLoop.PostCallback(() => OnSessionDisposed(new SessionDisposedEventArgs { Cause = cause }));
                }
                promiseCb.CompleteSuccessfully(VoidType.Instance);
            });
            return returnPromise;
        }

        // calls to application layer.

        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public virtual void OnMessageReceived(MessageReceivedEventArgs e)
        {
            MessageReceived?.Invoke(this, e);
        }

        public event EventHandler<SessionDisposedEventArgs> SessionDisposed;
        public virtual void OnSessionDisposed(SessionDisposedEventArgs e)
        {
            SessionDisposed?.Invoke(this, e);
        }
    }
}
