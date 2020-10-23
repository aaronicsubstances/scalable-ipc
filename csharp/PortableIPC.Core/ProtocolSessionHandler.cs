using PortableIPC.Core.Abstractions;
using PortableIPC.Core.Session;
using System;
using System.Collections.Generic;
using System.Net;

namespace PortableIPC.Core
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
        private readonly AbstractEventLoopApi _eventLoop;
        private object _lastTimeoutId;
        private int _currTimeoutSeqNr; // used to enforce timeout cancellations.

        public ProtocolSessionHandler(IEndpointHandler endpointHandler, AbstractEventLoopApi eventLoop,
            IPEndPoint endPoint, string sessionId, bool isConfiguredForInitialSend)
        {
            EndpointHandler = endpointHandler;
            _eventLoop = eventLoop;
            ConnectedEndpoint = endPoint;
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
            MaxSendDatagramLength = endpointHandler.EndpointConfig.MaxDatagramLength;
            MaxSendWindowSize = MaxReceiveWindowSize = endpointHandler.EndpointConfig.MaxWindowSize;            
        }

        public IEndpointHandler EndpointHandler { get; set; }
        public IPEndPoint ConnectedEndpoint { get; set; }
        public string SessionId { get; set; }

        public SessionState SessionState { get; set; } = SessionState.Opening;

        public int MaxReceiveWindowSize { get; set; }
        public int MaxSendWindowSize { get; set; }
        public int MaxSendDatagramLength { get; set; }
        public int MaxRetryCount { get; set; }
        public int IdleTimeoutSecs { get; set; }
        public int AckTimeoutSecs { get; set; }

        public int NextWindowIdToSend { get; set; } = 0;
        public int LastWindowIdSent { get; set; } = -1;
        public int LastWindowIdReceived { get; set; } = -1;
        public int LastMaxSeqReceived { get; set; }
        public bool IdleTimeoutEnabled { get; set; } = true;

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
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            PostSerially(() =>
            {
                ProcessShutdown(error, timeout);
                promiseCb.CompleteSuccessfully(VoidType.Instance);
            });
            return returnPromise;
        }

        public void ProcessReceive(ProtocolDatagram message)
        {
            PostSerially(() =>
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
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            PostSerially(() =>
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
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            PostSerially(() =>
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

        public void PostSerially(Action cb)
        {
            _eventLoop.PostCallbackSerially(this, cb);
        }

        public void PostNonSerially(Action cb)
        {
            _eventLoop.PostCallback(this, cb);
        }

        public void PostSeriallyIfNotClosed(Action cb)
        {
            PostSerially(() =>
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
                int timeoutSeqNr = ++_currTimeoutSeqNr;
                _lastTimeoutId = _promiseApi.ScheduleTimeout(timeoutSecs * 1000L,
                    () => ProcessTimeout(timeoutSeqNr, cb));
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
            // NB: disabling of idle timeout only applies to data exchange phase.
            if (SessionState != SessionState.OpenedForData || IdleTimeoutEnabled)
            {
                // also interpret non positive value as disable idle timeout.
                if (IdleTimeoutSecs > 0)
                {
                    int timeoutSeqNr = ++_currTimeoutSeqNr;
                    _lastTimeoutId = _promiseApi.ScheduleTimeout(IdleTimeoutSecs * 1000L,
                        () => ProcessTimeout(timeoutSeqNr, null));
                }
            }
        }

        private void CancelTimeout()
        {
            if (_lastTimeoutId != null)
            {
                _promiseApi.CancelTimeout(_lastTimeoutId);
                _lastTimeoutId = null;
            }
        }

        private void ProcessTimeout(int seqNr, Action cb)
        {
            PostSeriallyIfNotClosed(() =>
            {
                if (_lastTimeoutId != null && _currTimeoutSeqNr == seqNr)
                {
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
            });
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
            EndpointHandler.RemoveSessionHandler(ConnectedEndpoint, SessionId);

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
            PostNonSerially(() => OnClose(error, timeout));
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
