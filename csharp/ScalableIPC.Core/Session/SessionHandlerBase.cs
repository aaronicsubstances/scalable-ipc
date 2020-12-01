using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    /// <summary>
    /// So design of session handler default implementation is to hide acks, retries, window ids and
    /// sequence numbers from application layer. 
    /// It also should be the only one to use PromiseCompletionSource; the rest of the project
    /// should only use AbstractPromise.
    /// </summary>
    public abstract class SessionHandlerBase : ISessionHandler
    {
        public static readonly int StateOpen = 1;
        public static readonly int StateClosed = 0;

        private AbstractPromiseApi _promiseApi;
        private object _lastIdleTimeoutId;
        private object _lastAckTimeoutId;
        private bool _isInputShutdown;
        private bool _isOutputShutdown;

        public SessionHandlerBase()
        { }

        public virtual void CompleteInit(string sessionId, bool configureForInitialSend,
            INetworkTransportInterface networkInterface, GenericNetworkIdentifier remoteEndpoint)
        {
            NetworkInterface = networkInterface;
            EventLoop = networkInterface.EventLoop;
            RemoteEndpoint = remoteEndpoint;
            SessionId = sessionId;

            _promiseApi = networkInterface.PromiseApi;

            StateHandlers.Add(new ReceiveDataHandler(this));
            StateHandlers.Add(new SendDataHandler(this));
            StateHandlers.Add(new BulkSendDataHandler(this));
            StateHandlers.Add(new CloseHandler(this));

            // initialize session management parameters from endpoint config.
            IdleTimeoutSecs = networkInterface.IdleTimeoutSecs;
            AckTimeoutSecs = networkInterface.AckTimeoutSecs;
            MaxRetryCount = networkInterface.MaxRetryCount;
            MaximumTransferUnitSize = networkInterface.MaximumTransferUnitSize;
            MaxSendWindowSize = networkInterface.MaxSendWindowSize;
            MaxReceiveWindowSize = networkInterface.MaxReceiveWindowSize;
        }

        public INetworkTransportInterface NetworkInterface { get; private set; }
        public GenericNetworkIdentifier RemoteEndpoint { get; private set; }
        public string SessionId { get; private set; }
        public AbstractEventLoopApi EventLoop { get; private set; }

        public int SessionState { get; set; } = StateOpen;

        public int MaxReceiveWindowSize { get; set; }
        public int MaxSendWindowSize { get; set; }
        public int MaximumTransferUnitSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int IdleTimeoutSecs { get; set; }
        public int AckTimeoutSecs { get; set; }

        // Protocol requires initial value for window id to be 0,
        // and hence last window id should be negative to trigger
        // validation logic to expect 0.
        public long NextWindowIdToSend { get; set; } = 0;
        public long LastWindowIdReceived { get; set; } = -1;

        public int LastMaxSeqReceived { get; set; }
        public int? SessionIdleTimeoutSecs { get; set; }
        public bool? SessionCloseReceiverOption { get; set; }

        public virtual void IncrementNextWindowIdToSend()
        {
            NextWindowIdToSend = ProtocolDatagram.ComputeNextWindowIdToSend(NextWindowIdToSend);
        }

        public virtual bool IsSendInProgress()
        {
            foreach (var handler in StateHandlers)
            {
                if (handler.SendInProgress)
                {
                    return true;
                }
            }
            return false;
        }

        public List<ISessionStateHandler> StateHandlers { get; } = new List<ISessionStateHandler>();

        public virtual AbstractPromise<VoidType> ProcessReceive(ProtocolDatagram message)
        {
            EventLoop.PostCallback(() =>
            {
                Log("163c3ed3-0e9d-40a7-abff-b95310bfe200", message, "Session ProcessReceive");

                bool handled = false;
                if (SessionState != StateClosed)
                {
                    ResetIdleTimeout();
                    foreach (ISessionStateHandler stateHandler in StateHandlers)
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
            return _promiseApi.Resolve(VoidType.Instance);
        }

        public virtual AbstractPromise<VoidType> ProcessSend(ProtocolDatagram message)
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>(this);
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                message.SessionId = SessionId;
                Log("5abd8c58-4f14-499c-ad0e-788d59c5f7e2", message, "Session ProcessSend");

                if (SessionState == StateClosed)
                {
                    promiseCb.CompleteExceptionally(new Exception("Session handler is closed"));
                }
                else if (_isOutputShutdown)
                {
                    promiseCb.CompleteExceptionally(new Exception("Output has been shutdown"));
                }
                else if (IsSendInProgress())
                {
                    promiseCb.CompleteExceptionally(new Exception("send is in progress"));
                }
                else
                {
                    ResetIdleTimeout();
                    bool handled = false;
                    foreach (ISessionStateHandler stateHandler in StateHandlers)
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

        public virtual AbstractPromise<VoidType> ProcessSend(byte[] windowData, ProtocolDatagramOptions windowOptions)
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>(this);
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                Log("082f5b3f-c1fa-4d70-b224-0bf09d47ef84", "Session ProcessBulkSend");

                if (SessionState == StateClosed)
                {
                    promiseCb.CompleteExceptionally(new Exception("Session handler is closed"));
                }
                else if (_isOutputShutdown)
                {
                    promiseCb.CompleteExceptionally(new Exception("Output has been shutdown"));
                }
                else if (IsSendInProgress())
                {
                    promiseCb.CompleteExceptionally(new Exception("send is in progress"));
                }
                else
                {
                    ResetIdleTimeout();
                    bool handled = false;
                    foreach (ISessionStateHandler stateHandler in StateHandlers)
                    {
                        handled = stateHandler.ProcessSend(windowData, windowOptions, promiseCb);
                        if (handled)
                        {
                            break;
                        }
                    }
                    if (!handled)
                    {
                        promiseCb.CompleteExceptionally(new Exception("No state handler found to process send data"));
                    }
                }
            });
            return returnPromise;
        }

        public virtual AbstractPromise<VoidType> ShutdownInput()
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>(this);
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                _isInputShutdown = true;
                promiseCb.CompleteSuccessfully(VoidType.Instance);
            });
            return returnPromise;
        }

        public AbstractPromise<bool> IsInputShutdown()
        {
            PromiseCompletionSource<bool> promiseCb = _promiseApi.CreateCallback<bool>(this);
            AbstractPromise<bool> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                promiseCb.CompleteSuccessfully(_isInputShutdown);
            });
            return returnPromise;
        }

        public bool IsInputShutdownInternal()
        {
            return _isInputShutdown;
        }

        public virtual AbstractPromise<VoidType> ShutdownOutput()
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>(this);
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                _isOutputShutdown = true;
                promiseCb.CompleteSuccessfully(VoidType.Instance);
            });
            return returnPromise;
        }

        public AbstractPromise<bool> IsOutputShutdown()
        {
            PromiseCompletionSource<bool> promiseCb = _promiseApi.CreateCallback<bool>(this);
            AbstractPromise<bool> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                promiseCb.CompleteSuccessfully(_isOutputShutdown);
            });
            return returnPromise;
        }

        public virtual AbstractPromise<VoidType> Close(Exception error, bool timeout)
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>(this);
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                Log("e9d228bb-e00d-4002-8fe8-81df4a21dc41", "Session Close", "error", error,
                    "timeout", timeout);

                if (SessionState != StateClosed)
                {
                    CancelIdleTimeout();
                    CancelAckTimeout();

                    var unifiedError = error;
                    if (unifiedError == null)
                    {
                        if (timeout)
                        {
                            unifiedError = new Exception("Session timed out");
                        }
                        else
                        {
                            unifiedError = new Exception("Session closed");
                        }
                    }
                    foreach (ISessionStateHandler stateHandler in StateHandlers)
                    {
                        stateHandler.Shutdown(unifiedError);
                    }

                    SessionState = StateClosed;

                    Log("bd25f41a-32b0-4f5d-bd93-d8f348bd3e83", "Session close completed");

                    // pass on to application layer. NB: all calls to application layer must go through
                    // event loop.
                    EventLoop.PostCallback(() => OnClose(error, timeout));
                }
                promiseCb.CompleteSuccessfully(VoidType.Instance);
            });
            return returnPromise;
        }

        public virtual void PostIfNotClosed(Action cb)
        {
            EventLoop.PostCallback(() =>
            {
                if (SessionState != StateClosed)
                {
                    cb.Invoke();
                }
                else
                {
                    Log("49678d2f-518b-4cf1-b29f-4d3ceb74f3ec", "Skipping callback processing because session is closed");
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

            // Interpret non positive default value as disable idle timeout AND ignore session idle timeout.
            // On the other hand, let non negative session idle timeout override any positive default value
            // if larger.
            int effectiveIdleTimeoutSecs = IdleTimeoutSecs;
            if (effectiveIdleTimeoutSecs > 0 && SessionIdleTimeoutSecs.HasValue)
            {
                effectiveIdleTimeoutSecs = Math.Max(effectiveIdleTimeoutSecs, SessionIdleTimeoutSecs.Value);
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
            if (SessionState == StateClosed)
            {
                Log("06a8dca3-56a4-4121-ad2d-e63bf7bfb34d", "Ignoring idle timeout since session is closed");
                return;
            }

            Log("33b0e81b-c4fa-4a78-9cb7-0900e60afe3e", "Idle timeout has occured on session");

            _lastIdleTimeoutId = null;
            InitiateClose(null, true);
        }

        private void ProcessAckTimeout(Action cb)
        {
            if (SessionState == StateClosed)
            {
                Log("deec47ed-7c13-4e4e-9fd6-030aad245458", "Ignoring ack timeout since session is closed");
                return;
            }

            Log("4a130328-aa6e-46eb-81ca-5a705e3d0995", "Ack timeout has occured on session");

            _lastAckTimeoutId = null;
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
                customEvent.FillData("localEndpoint", NetworkInterface.LocalEndpoint.ToString());
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
                customEvent.FillData("localEndpoint", NetworkInterface.LocalEndpoint.ToString());
                customEvent.FillData("sessionId", SessionId);
                customEvent.FillData(args);
                return customEvent;
            });
        }

        public virtual void InitiateClose(Exception error, bool timeout)
        {
            if (SessionState == StateClosed)
            {
                Log("3f2b1897-7c52-4693-95e6-413c6de47915", "Session already closed, so skipping close initation");
                return;
            }

            Log("890ef817-b90c-45fc-9243-b809c684c730", "Session close initiation started");
            NetworkInterface.OnCloseSession(RemoteEndpoint, SessionId, error, timeout);
        }

        // calls to application layer.
        public abstract void OnDataReceived(byte[] windowData, ProtocolDatagramOptions windowOptions);
        public abstract void OnClose(Exception error, bool timeout);
    }
}
