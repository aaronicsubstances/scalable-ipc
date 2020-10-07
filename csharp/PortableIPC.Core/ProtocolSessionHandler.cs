using PortableIPC.Core.Abstractions;
using PortableIPC.Core.Session;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace PortableIPC.Core
{
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
            var closeHandler = new CloseHandler(this);

            StateHandlers.Add(receiveHandler);
            StateHandlers.Add(sendHandler);
            StateHandlers.Add(bulkSendHandler);
            StateHandlers.Add(closeHandler);
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

        public AbstractPromise<VoidType> Close(Exception error, bool timeout)
        {
            AbstractPromiseCallback<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract(); PostSerially(() =>
            {
                if (!IsClosed)
                {
                    HandleClosing(error, timeout, promiseCb);
                }
                else
                {
                    promiseCb.CompleteSuccessfully(VoidType.Instance);
                }
            });
            return returnPromise;
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
                    // don't reset idle timeout for receipt of error PDUs
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
            // always successful, since this method is intended to notify state handlers
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
            _eventLoop.PostCallbackSerially(this, new StoredCallback(_ => cb.Invoke()));
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

        public void ResetAckTimeout(int timeoutSecs, StoredCallback cb)
        {
            CancelTimeout();
            _lastTimeoutId = _promiseApi.ScheduleTimeout(timeoutSecs * 1000L, new StoredCallback(
                _ => HandleTimeout(cb)));
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
                _lastTimeoutId = _promiseApi.ScheduleTimeout(IdleTimeoutSecs * 1000L, new StoredCallback(
                    _ => HandleTimeout(null)));
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

        private void HandleTimeout(StoredCallback cb)
        {
            PostSeriallyIfNotClosed(() =>
            {
                _lastTimeoutId = null;
                if (cb != null)
                {
                    // reset timeout before calling timeout callback.
                    ResetIdleTimeout();
                    cb.Run();
                }
                else
                {
                    HandleClosing(null, true, null);
                }
            });
        }

        public void HandleClosing(Exception error, bool timeout, AbstractPromiseCallback<VoidType> promiseCb)
        {
            CancelTimeout();
            EndpointHandler.RemoveSessionHandler(ConnectedEndpoint, SessionId);
            foreach (ISessionStateHandler stateHandler in StateHandlers)
            {
                stateHandler.Close(error, timeout);
            }
            IsClosed = true;

            // pass on to application layer. NB: all calls to application layer must go through
            // event loop.
            _eventLoop.PostCallback(new StoredCallback(_ => OnClose(error, timeout, promiseCb)));
        }

        // calls to application layer
        public void OnClose(Exception error, bool timeout, AbstractPromiseCallback<VoidType> promiseCb)
        {
            // NB: promiseCb can be null if called from timeout and not from a state handler.
            promiseCb?.CompleteSuccessfully(VoidType.Instance);
        }

        public void OnOpenReceived(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            promiseCb.CompleteSuccessfully(VoidType.Instance);
        }

        public void OnDataReceived(byte[] data, int offset, int length, AbstractPromiseCallback<VoidType> promiseCb)
        {
            promiseCb.CompleteSuccessfully(VoidType.Instance);
        }
    }
}
