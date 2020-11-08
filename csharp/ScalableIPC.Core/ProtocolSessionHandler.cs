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
        public static readonly int StateOpening = 0;
        public static readonly int StateOpenedForData = 8;
        public static readonly int StateClosed = 20;

        private readonly AbstractPromiseApi _promiseApi;
        private object _lastTimeoutId;

        public ProtocolSessionHandler(IEndpointHandler endpointHandler, AbstractEventLoopApi eventLoop,
            IPEndPoint endPoint, string sessionId, bool isConfiguredForInitialSend)
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
        public string SessionId { get; set; }

        public int SessionState { get; set; } = StateOpening;
        public AbstractEventLoopApi EventLoop { get; set; }

        public int MaxReceiveWindowSize { get; set; }
        public int MaxSendWindowSize { get; set; }
        public int MaximumTransferUnitSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int IdleTimeoutSecs { get; set; }
        public int AckTimeoutSecs { get; set; }

        public long NextWindowIdToSend { get; set; } = 0;
        public long LastWindowIdReceived { get; set; } = -1;
        public int LastMaxSeqReceived { get; set; }
        public int? SessionIdleTimeoutSecs { get; set; }

        public void IncrementNextWindowIdToSend()
        {
            NextWindowIdToSend = ProtocolDatagram.ComputeNextWindowIdToSend(NextWindowIdToSend);
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
            Log("e9d228bb-e00d-4002-8fe8-81df4a21dc41", "Session Shutdown", "error", error,
                "timeout", timeout);

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
            Log("163c3ed3-0e9d-40a7-abff-b95310bfe200", message, "Session ProcessReceive");

            EventLoop.PostCallback(() =>
            {
                bool handled = false;
                if (SessionState != StateClosed)
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
            Log("5abd8c58-4f14-499c-ad0e-788d59c5f7e2", message, "Session ProcessSend");

            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>(this);
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                if (SessionState == StateClosed)
                {
                    promiseCb.CompleteExceptionally(new Exception("Session handler is closed"));
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
            Log("082f5b3f-c1fa-4d70-b224-0bf09d47ef84", "Session ProcessBulkSend");

            PromiseCompletionSource<VoidType> promiseCb = _promiseApi.CreateCallback<VoidType>(this);
            AbstractPromise<VoidType> returnPromise = promiseCb.Extract();
            EventLoop.PostCallback(() =>
            {
                if (SessionState == StateClosed)
                {
                    promiseCb.CompleteExceptionally(new Exception("Session handler is closed"));
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

        public void ResetAckTimeout(int timeoutSecs, Action cb)
        {
            Log("54c44637-3efe-4a35-a674-22e8e12c48cc", "About to set ack timeout");

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
            Log("41f243a1-db75-4c08-82fa-b2c7ff7dfda6", "About to reset idle timeout");
            SetIdleTimeout(true);
        }

        public void EnsureIdleTimeout()
        {
            Log("07fa532e-f45c-4acb-91b7-3e4d7ad9408c", "About to set idle timeout if not set already");
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
            if (effectiveIdleTimeoutSecs > 0 && SessionState == StateOpenedForData)
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
            if (SessionState == StateClosed)
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
            // subclasses can log more.

            Log("ee37084b-2201-4591-b681-25b0398aba40", message, "Discarding message");
        }
        
        public void Log(string logPosition, ProtocolDatagram pdu, string message, params object[] args)
        {
            CustomLoggerFacade.Log(() =>
            {
                return new CustomLogEvent(logPosition, pdu, message, args);
            });
        }

        public void Log(string logPosition, string message, params object[] args)
        {
            CustomLoggerFacade.Log(() =>
            {
                return new CustomLogEvent(logPosition, SessionId, message, args);
            });
        }

        public void ProcessShutdown(Exception error, bool timeout)
        {
            if (SessionState == StateClosed)
            {
                Log("3f2b1897-7c52-4693-95e6-413c6de47915", "Session already closed, so skipping shutdown");
                return;
            }

            Log("890ef817-b90c-45fc-9243-b809c684c730", "Session shutdown started");

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

            SessionState = StateClosed;

            Log("bd25f41a-32b0-4f5d-bd93-d8f348bd3e83", "Session shutdown completed");

            // pass on to application layer. NB: all calls to application layer must go through
            // event loop.
            EventLoop.PostCallback(() => OnClose(error, timeout));
        }

        // calls to application layer

        public void OnOpenRequest(byte[] data, Dictionary<string, List<string>> options, bool isLastOpenRequest)
        {
            Log("93be5fa4-20ca-4bca-94da-760549096d27", "OnOpenRequest");
        }
        public void OnDataReceived(byte[] data, Dictionary<string, List<string>> options)
        {
            Log("ec6784dd-895e-4c13-a973-fa4733909f4e", "OnDataReceived");
        }
        public void OnClose(Exception error, bool timeout)
        {
            Log("7fdb5b22-4a76-4ab3-9dc3-7a5bf1863709", "OnClose");
        }
    }
}
