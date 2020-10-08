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

        public ProtocolSessionHandler() :
            this(null, null, null)
        { }

        public ProtocolSessionHandler(IEndpointHandler endpointHandler, IPEndPoint endPoint, string sessionId)
        {
            EndpointHandler = endpointHandler;
            ConnectedEndpoint = endPoint;
            SessionId = sessionId;

            _promiseApi = endpointHandler.PromiseApi;
            _eventLoop = endpointHandler.EventLoop;

            var receiveHandler = new ReceiveHandler(this);
            var sendHandler = new SendHandler(this);
            var bulkSendHandler = new BulkSendHandler(this, sendHandler);

            StateHandlers.Add(receiveHandler);
            StateHandlers.Add(sendHandler);
            StateHandlers.Add(bulkSendHandler);
        }

        public IEndpointHandler EndpointHandler { get; set; }
        public IPEndPoint ConnectedEndpoint { get; set; }
        public string SessionId { get; set; }

        public bool IsOpened { get; set; }
        public bool IsClosed { get; private set; }
        public int MaxPduSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int WindowSize { get; set; }
        public int IdleTimeoutSecs { get; set; }
        public int AckTimeoutSecs { get; set; }

        public List<ISessionStateHandler> StateHandlers { get; } = new List<ISessionStateHandler>();

        public AbstractPromise<VoidType> Shutdown(Exception error, bool timeout)
        {
            if (!IsClosed)
            {
                ProcessShutdown(error, timeout);
            }
            return _promiseApi.Resolve(VoidType.Instance);
        }

        public AbstractPromise<VoidType> ProcessReceive(ProtocolDatagram message)
        {
            AbstractPromiseCallback<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            PostSerially(() =>
            {
                bool handled = false;
                if (!IsClosed)
                {
                    EnsureIdleTimeout();
                    foreach (ISessionStateHandler stateHandler in StateHandlers)
                    {
                        handled = stateHandler.ProcessReceive(message, promiseCb);
                        if (handled)
                        {
                            break;
                        }
                    }
                }
                if (!handled)
                {
                    DiscardReceivedMessage(message, promiseCb);
                }
            });
            return returnPromise;
        }

        public void DiscardReceivedMessage(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            // subclasses can log.
            promiseCb.CompleteSuccessfully(VoidType.Instance);
        }

        public AbstractPromise<VoidType> ProcessErrorReceive()
        {
            AbstractPromiseCallback<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            PostSerially(() =>
            {
                // for receipt of error PDUs, ignore processing if session handler is closed or
                // no state handler is interested.
                if (!IsClosed)
                {
                    EnsureIdleTimeout();
                    foreach (ISessionStateHandler stateHandler in StateHandlers)
                    {
                        bool handled = stateHandler.ProcessErrorReceive();
                        if (handled)
                        {
                            break;
                        }
                    }
                }
            });
            // always successful, since this method is intended to notify state handlers regardless of 
            // how it gets processed
            promiseCb.CompleteSuccessfully(VoidType.Instance);
            return returnPromise;
        }

        public AbstractPromise<VoidType> ProcessSend(ProtocolDatagram message)
        {
            AbstractPromiseCallback<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            PostSerially(() =>
            {
                if (IsClosed)
                {
                    promiseCb.CompleteExceptionally(new ProtocolSessionException(SessionId,
                        "Session handler is closed"));
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
                        promiseCb.CompleteExceptionally(new ProtocolSessionException(SessionId,
                            "No state handler found to process send"));
                    }
                }
            });
            return returnPromise;
        }

        public AbstractPromise<VoidType> ProcessSendData(byte[] rawData)
        {
            AbstractPromiseCallback<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            PostSerially(() =>
            {
                if (IsClosed)
                {
                    promiseCb.CompleteExceptionally(new ProtocolSessionException(SessionId,
                        "Session handler is closed"));
                }
                else
                {
                    EnsureIdleTimeout();
                    bool handled = false;
                    foreach (ISessionStateHandler stateHandler in StateHandlers)
                    {
                        handled = stateHandler.ProcessSendData(rawData, promiseCb);
                        if (!handled)
                        {
                            break;
                        }
                    }
                    if (!handled)
                    {
                        promiseCb.CompleteExceptionally(new ProtocolSessionException(SessionId,
                            "No state handler found to process send data"));
                    }
                }
            });
            return returnPromise;
        }

        public void PostSerially(Action cb)
        {
            _eventLoop.PostCallbackSerially(this, cb);
        }

        public void PostSeriallyIfNotClosed(Action cb)
        {
            PostSerially(() =>
            {
                if (!IsClosed)
                {
                    cb.Invoke();
                }
            });
        }

        public void ResetAckTimeout(int timeoutSecs, Action cb)
        {
            CancelTimeout();
            _lastTimeoutId = _promiseApi.ScheduleTimeout(timeoutSecs * 1000L,
                () => ProcessTimeout(cb));
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
            if (IdleTimeoutSecs > 0)
            {
                _lastTimeoutId = _promiseApi.ScheduleTimeout(IdleTimeoutSecs * 1000L,
                    () => ProcessTimeout(null));
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

        private void ProcessTimeout(Action cb)
        {
            PostSeriallyIfNotClosed(() =>
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
            });
        }

        public void ProcessShutdown(Exception error, bool timeout)
        {
            CancelTimeout();
            EndpointHandler.RemoveSessionHandler(ConnectedEndpoint, SessionId);
            foreach (ISessionStateHandler stateHandler in StateHandlers)
            {
                stateHandler.Shutdown(error, timeout);
            }
            IsClosed = true;

            // pass on to application layer. NB: all calls to application layer must go through
            // event loop.
            _eventLoop.PostCallback(() => OnClose(error, timeout));
        }

        // calls to application layer
        public void OnClose(Exception error, bool timeout)
        {
        }

        public void OnOpenReceived(ProtocolDatagram message)
        {
        }

        public void OnDataReceived(byte[] data)
        {
        }
    }
}
