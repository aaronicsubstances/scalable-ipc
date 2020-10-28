using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Session;
using System;
using System.Collections.Generic;
using System.Net;

namespace ScalableIPC.Core
{
    /// <summary>
    /// So design of session handler default implementation is to hide acks, retries, window ids and
    /// sequence numbers from application layer. 
    /// It also should be the only one to use promise callbacks and event loops; the rest of the project
    /// should only use abstract promises.
    /// </summary>
    public class ProtocolSessionHandler : ISessionHandler
    {
        private readonly AbstractPromiseApi _promiseApi;
        private object _lastTimeoutId;

        public ProtocolSessionHandler(IEndpointHandler endpointHandler, AbstractEventLoopApi eventLoop,
            IPEndPoint endPoint, Guid sessionId, bool isConfiguredForInitialSend)
        {
            EndpointHandler = endpointHandler;
            EventLoop = eventLoop;
            RemoteEndpoint = endPoint;
            SessionId = sessionId;

            _promiseApi = endpointHandler.PromiseApi;

            StateHandlers.Add(new ReceiveDataHandler(this));
            StateHandlers.Add(new SendDataHandler(this));
            StateHandlers.Add(new BulkSendDataHandler(this));
            StateHandlers.Add(new CloseHandler(this));

            if (isConfiguredForInitialSend)
            {
                StateHandlers.Add(new SendOpenHandler(this));
                StateHandlers.Add(new BulkSendOpenHandler(this));
            }
            else
            {
                StateHandlers.Add(new ReceiveOpenHandler(this));
            }

            // initialize session management parameters from endpoint config.
            IdleTimeoutSecs = endpointHandler.EndpointConfig.IdleTimeoutSecs;
            AckTimeoutSecs = endpointHandler.EndpointConfig.AckTimeoutSecs;
            MaxRetryCount = endpointHandler.EndpointConfig.MaxRetryCount;
            MaximumTransferUnitSize = endpointHandler.EndpointConfig.MaximumTransferUnitSize;
            MaxSendWindowSize = endpointHandler.EndpointConfig.MaxSendWindowSize;
            MaxReceiveWindowSize = endpointHandler.EndpointConfig.MaxReceiveWindowSize;
        }

        public IEndpointHandler EndpointHandler { get; set; }
        public IPEndPoint RemoteEndpoint { get; set; }
        public Guid SessionId { get; set; }

        public SessionState SessionState { get; set; } = SessionState.Opening;
        public AbstractEventLoopApi EventLoop { get; set; }

        public int MaxReceiveWindowSize { get; set; }
        public int MaxSendWindowSize { get; set; }
        public int MaximumTransferUnitSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int IdleTimeoutSecs { get; set; }
        public int AckTimeoutSecs { get; set; }

        public int NextWindowIdToSend { get; set; } = 0;
        public int LastWindowIdSent { get; set; } = -1;
        public int LastWindowIdReceived { get; set; } = -1;
        public int LastMaxSeqReceived { get; set; }
        public int? SessionIdleTimeoutSecs { get; set; }

        public void IncrementNextWindowIdToSend()
        {
            LastWindowIdSent = NextWindowIdToSend;
            NextWindowIdToSend++;
            if (NextWindowIdToSend < 0)
            {
                NextWindowIdToSend = 0;
            }
        }

        public bool IsSendInProgress()
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

        public AbstractPromise<VoidType> Shutdown(Exception error, bool timeout)
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>(this);
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                ProcessShutdown(error, timeout);
                promiseCb.CompleteSuccessfully(VoidType.Instance);
            });
            return returnPromise;
        }

        public void ProcessReceive(ProtocolDatagram message)
        {
            EventLoop.PostCallback(() =>
            {
                bool handled = false;
                if (SessionState != SessionState.Closed)
                {
                    EnsureIdleTimeout();
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
        }

        public AbstractPromise<VoidType> ProcessSend(ProtocolDatagram message)
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>(this);
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                if (SessionState == SessionState.Closed)
                {
                    promiseCb.CompleteExceptionally(new Exception(
                        SessionState == SessionState.Closed ? "Session handler is closed" : "Session handler is closing"));
                }
                else
                {
                    EnsureIdleTimeout();
                    bool handled = false;
                    foreach (ISessionStateHandler stateHandler in StateHandlers)
                    {
                        handled = stateHandler.ProcessSend(message, promiseCb);
                        if (!handled)
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

        public AbstractPromise<VoidType> ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options)
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>(this);
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                if (SessionState == SessionState.Closed)
                {
                    promiseCb.CompleteExceptionally(new Exception(
                        SessionState == SessionState.Closed ? "Session handler is closed" : "Session handler is closing"));
                }
                else
                {
                    EnsureIdleTimeout();
                    bool handled = false;
                    foreach (ISessionStateHandler stateHandler in StateHandlers)
                    {
                        handled = stateHandler.ProcessSend(opCode, data, options, promiseCb);
                        if (!handled)
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

        public void PostIfNotClosed(Action cb)
        {
            EventLoop.PostCallback(() =>
            {
                if (SessionState != SessionState.Closed)
                {
                    cb.Invoke();
                }
            });
        }

        public void ResetAckTimeout(int timeoutSecs, Action cb)
        {
            CancelTimeout();
            // interpret non positive timeout as disable ack timeout.
            if (timeoutSecs > 0)
            {
                _lastTimeoutId = EventLoop.ScheduleTimeout(timeoutSecs,
                    () => ProcessTimeout(cb));
            }
        }

        public void ResetIdleTimeout()
        {
            SetIdleTimeout(true);
        }

        public void EnsureIdleTimeout()
        {
            SetIdleTimeout(false);
        }

        private void SetIdleTimeout(bool reset)
        {
            if (reset)
            {
                CancelTimeout();
            }
            else if (_lastTimeoutId != null)
            {
                return;
            }

            // Interpret non positive default value as disable idle timeout AND ignore session idle timeout.
            // On the other hand, let non negative session idle timeout override any positive default value.
            // NB: use session idle timeout only in data exchange phase.
            int effectiveIdleTimeoutSecs = IdleTimeoutSecs;
            if (effectiveIdleTimeoutSecs > 0 && SessionState == SessionState.OpenedForData)
            {
                if (SessionIdleTimeoutSecs.HasValue && SessionIdleTimeoutSecs.Value >= 0)
                {
                    effectiveIdleTimeoutSecs = SessionIdleTimeoutSecs.Value;
                }
            }

            // In the end, only positive values result in idle timeouts.
            if (effectiveIdleTimeoutSecs > 0)
            {
                _lastTimeoutId = EventLoop.ScheduleTimeout(IdleTimeoutSecs,
                    () => ProcessTimeout(null));
            }
        }

        private void CancelTimeout()
        {
            if (_lastTimeoutId != null)
            {
                EventLoop.CancelTimeout(_lastTimeoutId);
                _lastTimeoutId = null;
            }
        }

        private void ProcessTimeout(Action cb)
        {
            if (SessionState == SessionState.Closed)
            {
                return;
            }
            _lastTimeoutId = null;
            if (cb != null)
            {
                // reset timeout before calling timeout callback.
                ResetIdleTimeout();
                cb.Invoke();
            }
            else
            {
                ProcessShutdown(null, true);
            }
        }

        public void DiscardReceivedMessage(ProtocolDatagram message)
        {
            // subclasses can log.
        }

        public void ProcessShutdown(Exception error, bool timeout)
        {
            if (SessionState == SessionState.Closed)
            {
                return;
            }

            CancelTimeout();
            EndpointHandler.RemoveSessionHandler(RemoteEndpoint, SessionId);

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

            SessionState = SessionState.Closed;

            // pass on to application layer. NB: all calls to application layer must go through
            // event loop.
            EventLoop.PostCallback(() => OnClose(error, timeout));
        }

        // calls to application layer

        public void OnOpenRequest(byte[] data, Dictionary<string, List<string>> options, bool isLastOpenRequest)
        {
        }
        public void OnDataReceived(byte[] data, Dictionary<string, List<string>> options)
        {
        }
        public void OnClose(Exception error, bool timeout)
        {
        }
    }
}
