using PortableIPC.Core.Abstractions;
using PortableIPC.Core.Session;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace PortableIPC.Core
{
    /// <summary>
    /// So design of session handler default implementation is to hide acks, retries and sequence numbers from application
    /// layer. It also should be the only one to use promise callbacks and event loops; the rest of the project
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

            var receiveHandler = new ReceiveDataHandler(this);
            var sendHandler = new SendDataHandler(this);
            var bulkSendHandler = new BulkSendDataHandler(this, sendHandler);
            var closeHandler = new CloseHandler(this);

            StateHandlers.Add(receiveHandler);
            StateHandlers.Add(sendHandler);
            StateHandlers.Add(bulkSendHandler);
            StateHandlers.Add(closeHandler);

            if (isConfiguredForInitialSend)
            {
                var sendOpenHandler = new SendOpenHandler(this);
                StateHandlers.Add(sendOpenHandler);
                var bulkSendOpenHandler = new BulkSendOpenHandler(this, sendOpenHandler);
                StateHandlers.Add(bulkSendOpenHandler);
            }
            else
            {
                var receiveOpenHandler = new ReceiveOpenHandler(this);
                StateHandlers.Add(receiveOpenHandler);
            }
        }

        public IEndpointHandler EndpointHandler { get; set; }
        public IPEndPoint ConnectedEndpoint { get; set; }
        public string SessionId { get; set; }

        public SessionState SessionState { get; set; } = SessionState.NotStarted;
        public bool IsOpening
        {
            get
            {
                return SessionState == SessionState.NotStarted || SessionState == SessionState.Opening;
            }
        }
        public bool IsClosing
        { 
            get
            {
                return SessionState == SessionState.Closing || SessionState == SessionState.Closed;
            }
        }
        public long NextWindowIdToSend { get; set; } = 0;
        public long LastWindowIdSent { get; set; } = -1;
        public long LastWindowIdReceived { get; set; } = -1;
        public bool IdleTimeoutEnabled { get; set; } = true;

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
                if (!IsClosing)
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

        public void DiscardReceivedMessage(ProtocolDatagram message)
        {
            // subclasses can log.
        }

        public AbstractPromise<VoidType> ProcessSend(ProtocolDatagram message)
        {
            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            PostSerially(() =>
            {
                if (IsClosing)
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
                if (IsClosing)
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
            _eventLoop.PostCallback(cb);
        }

        public void PostSeriallyIfNotClosed(Action cb)
        {
            PostSerially(() =>
            {
                if (!IsClosing)
                {
                    cb.Invoke();
                }
            });
        }

        public void ResetAckTimeout(int timeoutSecs, Action cb)
        {
            CancelTimeout();
            int timeoutSeqNr = ++_currTimeoutSeqNr;
            _lastTimeoutId = _promiseApi.ScheduleTimeout(timeoutSecs * 1000L,
                () => ProcessTimeout(timeoutSeqNr, cb));
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
            if (IdleTimeoutEnabled)
            {
                int timeoutSeqNr = ++_currTimeoutSeqNr;
                _lastTimeoutId = _promiseApi.ScheduleTimeout(
                    EndpointHandler.EndpointConfig.IdleTimeoutSecs * 1000L,
                    () => ProcessTimeout(timeoutSeqNr, null));
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

        public void ProcessShutdown(Exception error, bool timeout)
        {
            if (IsClosing)
            {
                return;
            }

            SessionState = SessionState.Closing;

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

        public void OnOpenRequest(byte[] data, Dictionary<string, List<string>> options)
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
