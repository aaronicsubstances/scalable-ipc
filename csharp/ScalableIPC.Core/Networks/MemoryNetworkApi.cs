using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using ScalableIPC.Core.Helpers;
using ScalableIPC.Core.Session;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScalableIPC.Core.Networks
{
    public class MemoryNetworkApi : AbstractNetworkApi
    {
        private readonly SessionHandlerStore _sessionHandlerStore;
        private readonly Random _randomGenerator = new Random();

        // behaves like a boolean but for use with Interlocked.Read, has to be
        // a long.
        private long _isShuttingDown = 0;

        public MemoryNetworkApi()
        {
            _sessionHandlerStore = new SessionHandlerStore();
            PromiseApi = new DefaultPromiseApi();
            SessionTaskExecutor = new DefaultSessionTaskExecutor();
            ConnectedNetworks = new Dictionary<GenericNetworkIdentifier, MemoryNetworkApi>();
        }

        public Dictionary<GenericNetworkIdentifier, MemoryNetworkApi> ConnectedNetworks { get; }

        public int MinTransmissionDelayMs { get; set; }
        public int MaxTransmissionDelayMs { get; set; }

        public GenericNetworkIdentifier LocalEndpoint { get; set; }
        public AbstractPromiseApi PromiseApi { get; set; }
        public ISessionTaskExecutor SessionTaskExecutor { get; set; }
        public int IdleTimeoutSecs { get; set; }
        public int MinRemoteIdleTimeoutSecs { get; set; }
        public int MaxRemoteIdleTimeoutSecs { get; set; }
        public int AckTimeoutSecs { get; set; }
        public int MaxSendWindowSize { get; set; }
        public int MaxReceiveWindowSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int MaximumTransferUnitSize { get; set; }
        public ISessionHandlerFactory SessionHandlerFactory { get; set; }

        public AbstractPromise<VoidType> StartAsync()
        {
            // nothing to do.
            return DefaultPromiseApi.CompletedPromise;
        }

        public AbstractPromise<ISessionHandler> OpenSessionAsync(GenericNetworkIdentifier remoteEndpoint, string sessionId, 
            ISessionHandler sessionHandler)
        {
            try
            {
                if (IsShuttingDown())
                {
                    throw new Exception("Cannot start new session due to shutdown");
                }
                if (sessionId == null)
                {
                    sessionId = ProtocolDatagram.GenerateSessionId();
                }
                if (sessionHandler == null)
                {
                    if (SessionHandlerFactory == null)
                    {
                        throw new Exception("Must provide sessionHandler if SessionHandlerFactory is null");
                    }
                    sessionHandler = SessionHandlerFactory.Create();
                }
                sessionHandler.CompleteInit(sessionId, true, this, remoteEndpoint);
                lock (_sessionHandlerStore)
                {
                    _sessionHandlerStore.Add(remoteEndpoint, sessionId,
                        new SessionHandlerWrapper(sessionHandler));
                }
                return PromiseApi.Resolve(sessionHandler);
            }
            catch (Exception ex)
            {
                return PromiseApi.Reject<ISessionHandler>(ex);
            }
        }

        public void RequestSend(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram message, Action<Exception> cb)
        {
            // Fire outside of event loop thread if possible.
            Task.Run(async () =>
            {
                var promise = HandleSendAsync(remoteEndpoint, message);
                try
                {
                    await ((DefaultPromise<VoidType>)promise).WrappedTask;
                    cb(null);
                }
                catch (Exception ex)
                {
                    cb(ex);
                }
            });
        }

        public AbstractPromise<VoidType> HandleSendAsync(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram message)
        {
            if (ConnectedNetworks.ContainsKey(remoteEndpoint))
            {
                Task.Run(async () =>
                {
                    // Simulate transmission delay here.
                    var connectedNetwork = ConnectedNetworks[remoteEndpoint];
                    int transmissionDelayMs;
                    if (connectedNetwork.MinTransmissionDelayMs > connectedNetwork.MaxTransmissionDelayMs)
                    {
                        transmissionDelayMs = -1;
                    }
                    else if (connectedNetwork.MinTransmissionDelayMs == connectedNetwork.MaxTransmissionDelayMs)
                    {
                        transmissionDelayMs = connectedNetwork.MinTransmissionDelayMs;
                    }
                    else
                    {
                        transmissionDelayMs = connectedNetwork._randomGenerator.Next(connectedNetwork.MinTransmissionDelayMs,
                            connectedNetwork.MaxTransmissionDelayMs + 1);
                    }
                    if (transmissionDelayMs > 0)
                    {
                        await Task.Delay(transmissionDelayMs);
                    }
                    await connectedNetwork.HandleReceiveAsync(LocalEndpoint, message);
                });
                return DefaultPromiseApi.CompletedPromise;
            }
            else
            {
                return PromiseApi.Reject<VoidType>(new Exception($"{remoteEndpoint} remote endpoint not found."));
            }
        }

        private Task HandleReceiveAsync(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram message)
        {
            try
            {
                ISessionHandler sessionHandler;
                lock (_sessionHandlerStore)
                {
                    var sessionHandlerWrapper = _sessionHandlerStore.Get(remoteEndpoint, message.SessionId);
                    if (sessionHandlerWrapper != null)
                    {
                        sessionHandler = sessionHandlerWrapper.SessionHandler;
                    }
                    else
                    {
                        if (IsShuttingDown())
                        {
                            // silently ignore new session if shutting down.
                            return Task.CompletedTask;
                        }
                        
                        if (SessionHandlerFactory == null)
                        {
                            throw new Exception("SessionHandlerFactory is null so new session handler could not be " +
                                "created for incoming message");
                        }

                        sessionHandler = SessionHandlerFactory.Create();
                        sessionHandler.CompleteInit(message.SessionId, false, this, remoteEndpoint);
                        _sessionHandlerStore.Add(remoteEndpoint, message.SessionId,
                            new SessionHandlerWrapper(sessionHandler));
                    }
                }
                var promise = sessionHandler.ProcessReceiveAsync(message);
                return ((DefaultPromise<VoidType>) promise).WrappedTask;
            }
            catch (Exception ex)
            {
                CustomLoggerFacade.Log(() =>
                    new CustomLogEvent("1dec508c-2d59-4336-8617-30bb71a9a5a8", "Error occured during message " +
                        $"receipt handling from {remoteEndpoint}", ex));
                return Task.CompletedTask;
            }
        }

        public void RequestSessionDispose(GenericNetworkIdentifier remoteEndpoint, string sessionId, SessionDisposedException cause)
        {
            // Fire outside of event loop thread if possible.
            Task.Run(() => {
                var promise = DisposeSessionAsync(remoteEndpoint, sessionId, cause);
                return ((DefaultPromise<VoidType>)promise).WrappedTask;
            });
        }

        public AbstractPromise<VoidType> DisposeSessionAsync(GenericNetworkIdentifier remoteEndpoint, string sessionId,
            SessionDisposedException cause)
        {
            SessionHandlerWrapper sessionHandler;
            lock (_sessionHandlerStore)
            {
                sessionHandler = _sessionHandlerStore.Get(remoteEndpoint, sessionId);
                _sessionHandlerStore.Remove(remoteEndpoint, sessionId);
            }
            if (sessionHandler != null)
            {
                return SwallowException(sessionHandler.SessionHandler.FinaliseDisposeAsync(cause));
            }
            return DefaultPromiseApi.CompletedPromise;
        }

        private AbstractPromise<VoidType> SwallowException(AbstractPromise<VoidType> promise)
        {
            return promise.CatchCompose(err =>
            {
                CustomLoggerFacade.Log(() => new CustomLogEvent("27d232da-f4e4-4f25-baeb-56bd53ed49fa",
                    "Exception occurred here", err));
                return DefaultPromiseApi.CompletedPromise;
            });
        }

        public AbstractPromise<VoidType> ShutdownAsync(int waitSecs)
        {
            // it is enough to prevent creation of new session handlers
            Interlocked.Exchange(ref _isShuttingDown, 1);
            return DefaultPromiseApi.CompletedPromise;
        }

        public bool IsShuttingDown()
        {
            return Interlocked.Read(ref _isShuttingDown) != 0;
        }

        /*public virtual AbstractPromise<VoidType> CloseSessionAsync(GenericNetworkIdentifier remoteEndpoint, string sessionId,
            SessionDisposedException cause)
        {
            SessionHandlerWrapper sessionHandler;
            lock (_sessionHandlerStore)
            {
                sessionHandler = _sessionHandlerStore.Get(remoteEndpoint, sessionId);
                _sessionHandlerStore.Remove(remoteEndpoint, sessionId);
            }
            if (sessionHandler != null)
            {
                return SwallowException(sessionHandler.SessionHandler.FinaliseDisposeAsync(cause));
            }
            return PromiseApi.Resolve(VoidType.Instance);
        }

        public virtual AbstractPromise<VoidType> CloseSessionsAsync(GenericNetworkIdentifier remoteEndpoint,
            SessionDisposedException cause)
        {
            List<SessionHandlerWrapper> sessionHandlers;
            lock (_sessionHandlerStore)
            {
                sessionHandlers = _sessionHandlerStore.GetSessionHandlers(remoteEndpoint);
                _sessionHandlerStore.Remove(remoteEndpoint);
            }
            var retVal = PromiseApi.Resolve(VoidType.Instance);
            foreach (var sessionHandler in sessionHandlers)
            {
                retVal = retVal.ThenCompose(_ => SwallowException(
                    sessionHandler.SessionHandler.FinaliseDisposeAsync(cause)));
            }
            return retVal;
        }*/
    }
}
