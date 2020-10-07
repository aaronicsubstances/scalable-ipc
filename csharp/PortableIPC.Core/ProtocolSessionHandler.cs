using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace PortableIPC.Core
{
    public class ProtocolSessionHandler : ISessionHandler
    {
        private readonly List<ISessionStateHandler> _stateHandlers = new List<ISessionStateHandler>();
        private readonly AbstractPromiseApi _promiseApi;
        private readonly AbstractEventLoopApi _eventLoop;
        private object _lastTimeoutId;

        public ProtocolEndpointHandler EndpointHandler { get; set; }
        public IPEndPoint ConnectedEndpoint { get; set; }
        public string SessionId { get; set; }

        public bool IsClosed { get; private set; }
        public int MaxPduSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int WindowSize { get; set; }
        public int IdleTimeoutSecs { get; set; }
        public int AckTimeoutSecs { get; set; }

        public AbstractPromise<VoidType> Close(Exception error, bool timeout)
        {
            throw new NotImplementedException();
        }

        public AbstractPromise<VoidType> ProcessReceive(ProtocolDatagram message)
        {
            AbstractPromiseOnHold<VoidType> promiseOnHold = _promiseApi.CreateOnHold<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseOnHold.Extract();
            PostSerially(() =>
            {
                bool handled = false;
                if (!IsClosed)
                {
                    EnsureIdleTimeout();
                    foreach (ISessionStateHandler stateHandler in _stateHandlers)
                    {
                        handled = stateHandler.ProcessReceive(message, promiseOnHold);
                        if (handled)
                        {
                            break;
                        }
                    }
                }
                if (!handled)
                {
                    DiscardReceivedMessage(message, promiseOnHold);
                }
            });
            return returnPromise;
        }

        public void DiscardReceivedMessage(ProtocolDatagram message, AbstractPromiseOnHold<VoidType> promiseOnHold)
        {
            // subclasses can log.
            promiseOnHold.CompleteSuccessfully(VoidType.Instance);
        }

        public AbstractPromise<VoidType> ProcessErrorReceive()
        {
            AbstractPromiseOnHold<VoidType> promiseOnHold = _promiseApi.CreateOnHold<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseOnHold.Extract();
            PostSerially(() =>
            {
                // for receipt of error PDUs, ignore processing if session handler is closed or
                // no state handler is interested.
                if (!IsClosed)
                {
                    // don't reset idle timeout for receipt of error PDUs
                    foreach (ISessionStateHandler stateHandler in _stateHandlers)
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
            promiseOnHold.CompleteSuccessfully(VoidType.Instance);
            return returnPromise;
        }

        public AbstractPromise<VoidType> ProcessSend(ProtocolDatagram message)
        {
            AbstractPromiseOnHold<VoidType> promiseOnHold = _promiseApi.CreateOnHold<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseOnHold.Extract();
            PostSerially(() =>
            {
                if (IsClosed)
                {
                    promiseOnHold.CompleteExceptionally(new ProtocolSessionException(SessionId,
                        "Session handler is closed"));
                }
                else
                {
                    EnsureIdleTimeout();
                    bool handled = false;
                    foreach (ISessionStateHandler stateHandler in _stateHandlers)
                    {
                        handled = stateHandler.ProcessSend(message, promiseOnHold);
                        if (!handled)
                        {
                            break;
                        }
                    }
                    if (!handled)
                    {
                        promiseOnHold.CompleteExceptionally(new ProtocolSessionException(SessionId,
                            "No state handler found to process send"));
                    }
                }
            });
            return returnPromise;
        }

        public AbstractPromise<VoidType> ProcessSendData(byte[] rawData)
        {
            AbstractPromiseOnHold<VoidType> promiseOnHold = _promiseApi.CreateOnHold<VoidType>();
            AbstractPromise<VoidType> returnPromise = promiseOnHold.Extract();
            PostSerially(() =>
            {
                if (IsClosed)
                {
                    promiseOnHold.CompleteExceptionally(new ProtocolSessionException(SessionId,
                        "Session handler is closed"));
                }
                else
                {
                    EnsureIdleTimeout();
                    bool handled = false;
                    foreach (ISessionStateHandler stateHandler in _stateHandlers)
                    {
                        handled = stateHandler.ProcessSendData(rawData, promiseOnHold);
                        if (!handled)
                        {
                            break;
                        }
                    }
                    if (!handled)
                    {
                        promiseOnHold.CompleteExceptionally(new ProtocolSessionException(SessionId,
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

        public void HandleClosing(Exception error, bool timeout, AbstractPromiseOnHold<VoidType> promiseOnHold)
        {
            CancelTimeout();
            IsClosed = true;
            EndpointHandler.RemoveSessionHandler(ConnectedEndpoint, SessionId);
            foreach (ISessionStateHandler stateHandler in _stateHandlers)
            {
                stateHandler.Close(error, timeout);
            }

            _eventLoop.PostCallback(new StoredCallback(_ => OnClose(error, timeout, promiseOnHold)));
        }

        // calls to application layer
        public void OnClose(Exception error, bool timeout, AbstractPromiseOnHold<VoidType> promiseOnHold)
        {
            // NB: promiseOnHold can be null if called from timeout and not from a state handler.
            promiseOnHold?.CompleteSuccessfully(VoidType.Instance);
        }

        public void OnOpenReceived(ProtocolDatagram message, AbstractPromiseOnHold<VoidType> promiseOnHold)
        {
            promiseOnHold.CompleteSuccessfully(VoidType.Instance);
        }

        public void OnDataReceived(byte[] data, int offset, int length, AbstractPromiseOnHold<VoidType> promiseOnHold)
        {
            promiseOnHold.CompleteSuccessfully(VoidType.Instance);
        }
    }
}
