using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using ScalableIPC.Core.Helpers;
using ScalableIPC.Core.Networks.Common;
using ScalableIPC.Core.Session;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using static ScalableIPC.Core.Helpers.CustomLogEvent;

namespace ScalableIPC.Core.Networks
{
    public class MemoryNetworkApi : AbstractNetworkApi
    {
        public interface ISendBehaviour
        {
            SendConfig Create(GenericNetworkIdentifier remoteIdentifier, ProtocolDatagram datagram);
        }
        public class SendConfig
        {
            public bool SerializeDatagram { get; set; }
            public int Delay { get; set; }
            public Exception Error { get; set; }
        }
        public interface ITransmissionBehaviour
        {
            TransmissionConfig Create(GenericNetworkIdentifier remoteIdentifier, ProtocolDatagram datagram);
        }
        public class TransmissionConfig
        {
            public int[] Delays { get; set; }
        }

        private readonly SessionHandlerStore _sessionHandlerStore;
        private bool _isShuttingDown;
        private readonly object _isShuttingDownLock = new object();

        public MemoryNetworkApi()
        {
            _sessionHandlerStore = new SessionHandlerStore();
            PromiseApi = DefaultPromiseApi.Instance;
            SessionTaskExecutor = new DefaultSessionTaskExecutor();
            ConnectedNetworks = new Dictionary<GenericNetworkIdentifier, MemoryNetworkApi>();
        }

        public Dictionary<GenericNetworkIdentifier, MemoryNetworkApi> ConnectedNetworks { get; }

        public ISendBehaviour SendBehaviour { get; set; }

        public ITransmissionBehaviour TransmissionBehaviour { get; set; }

        public GenericNetworkIdentifier LocalEndpoint { get; set; }
        public AbstractPromiseApi PromiseApi { get; set; }
        public ISessionTaskExecutor SessionTaskExecutor { get; set; }
        public int IdleTimeout { get; set; }
        public int MinRemoteIdleTimeout { get; set; }
        public int MaxRemoteIdleTimeout { get; set; }
        public int AckTimeout { get; set; }
        public int MaxSendWindowSize { get; set; }
        public int MaxReceiveWindowSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int MaximumTransferUnitSize { get; set; }
        public ISessionHandlerFactory SessionHandlerFactory { get; set; }

        public AbstractPromise<VoidType> StartAsync()
        {
            // nothing to do.
            return PromiseApi.CompletedPromise();
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
            var newLogicalThreadId = GenerateAndRecordLogicalThreadId();
            Task.Run(() =>
            {
                var promise = PromiseApi.StartLogicalThread(newLogicalThreadId)
                    .ThenCompose(_ => HandleSendAsync(remoteEndpoint, message))
                    .Then(_ =>
                    {
                        cb(null);
                        return VoidType.Instance;
                    })
                    .CatchCompose(ex =>
                    {
                        cb(ex);
                        return PromiseApi.CompletedPromise();
                    })
                    .Finally(() => RecordAndEndLogicalThread());
                return ((DefaultPromise<VoidType>)promise).WrappedTask;
            });
        }

        public AbstractPromise<VoidType> HandleSendAsync(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram datagram)
        {
            // simulate sending.

            SendConfig sendConfig = null;
            if (SendBehaviour != null)
            {
                sendConfig = SendBehaviour.Create(remoteEndpoint, datagram);
            }

            // interpret null send config as immediate success.
            AbstractPromise<VoidType> sendResult = PromiseApi.CompletedPromise();
            byte[] serialized = null;
            if (sendConfig != null)
            {
                if (sendConfig.SerializeDatagram)
                {
                    // Simulate serialization
                    serialized = datagram.ToRawDatagram();
                }
                if (sendConfig.Delay > 0)
                {
                    sendResult = sendResult.ThenCompose(_ => PromiseApi.Delay(sendConfig.Delay));
                }
                if (sendConfig.Error != null)
                {
                    // don't proceed further
                    return sendResult.ThenCompose(_ => PromiseApi.Reject<VoidType>(sendConfig.Error));
                }
            }

            // done with simulating sending.

            if (!ConnectedNetworks.ContainsKey(remoteEndpoint))
            {
                throw new Exception($"{remoteEndpoint} remote endpoint not found.");
            }
            var connectedNetwork = ConnectedNetworks[remoteEndpoint];

            // Simulate transmission delays and duplication of datagrams.
            // NB: null/empty array of transmission delays simulates dropping of datagrams.
            // multiple transmission delays simulates duplication of datagrams

            TransmissionConfig transmissionConfig = null;
            if (TransmissionBehaviour != null)
            {
                transmissionConfig = TransmissionBehaviour.Create(remoteEndpoint, datagram);
            }
            if (transmissionConfig == null)
            {
                // interpret as immediate transfer to connected network.
                var newLogicalThreadId = GenerateAndRecordLogicalThreadId();
                Task.Run(() => {
                    var transmissionResult = PromiseApi.StartLogicalThread(newLogicalThreadId)
                        .ThenCompose(_ => connectedNetwork.HandleReceiveAsync(
                            LocalEndpoint, datagram))
                        .Finally(() => RecordAndEndLogicalThread());
                    return ((DefaultPromise<VoidType>)transmissionResult).WrappedTask;
                });
                return sendResult;
            }

            // do nothing if delays are not specified.
            if (transmissionConfig.Delays == null)
            {
                return sendResult;
            }
            for (int i = 0; i < transmissionConfig.Delays.Length; i++)
            {
                // capture usage of index i before entering closure
                int transmissionDelay = transmissionConfig.Delays[i];
                var newLogicalThreadId = GenerateAndRecordLogicalThreadId();
                Task.Run(() => {
                    var transmissionResult = PromiseApi.StartLogicalThread(newLogicalThreadId)
                        .ThenCompose(_ =>
                        {
                            if (transmissionDelay > 0)
                            {
                                return PromiseApi.Delay(transmissionDelay);
                            }
                            else
                            {
                                return PromiseApi.CompletedPromise();
                            }
                        })
                        .Then(_ =>
                        {
                            if (serialized == null)
                            {
                                return datagram;
                            }
                            ProtocolDatagram deserialized = ProtocolDatagram.Parse(serialized, 0, serialized.Length);
                            return deserialized;
                        })
                        .ThenCompose(deserialized => connectedNetwork.HandleReceiveAsync(
                            LocalEndpoint, deserialized))
                        .Finally(() => RecordAndEndLogicalThread());

                    return ((DefaultPromise<VoidType>)transmissionResult).WrappedTask;
                });
            }

            return sendResult;
        }

        private AbstractPromise<VoidType> HandleReceiveAsync(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram datagram)
        {
            try
            {
                ISessionHandler sessionHandler;
                lock (_sessionHandlerStore)
                {
                    var sessionHandlerWrapper = _sessionHandlerStore.Get(remoteEndpoint, datagram.SessionId);
                    if (sessionHandlerWrapper != null)
                    {
                        sessionHandler = sessionHandlerWrapper.SessionHandler;
                    }
                    else
                    {
                        if (IsShuttingDown())
                        {
                            // silently ignore new session if shutting down.
                            return PromiseApi.CompletedPromise();
                        }
                        
                        if (SessionHandlerFactory == null)
                        {
                            throw new Exception("SessionHandlerFactory is null so new session handler could not be " +
                                "created for incoming message");
                        }

                        sessionHandler = SessionHandlerFactory.Create();
                        sessionHandler.CompleteInit(datagram.SessionId, false, this, remoteEndpoint);
                        _sessionHandlerStore.Add(remoteEndpoint, datagram.SessionId,
                            new SessionHandlerWrapper(sessionHandler));
                    }
                }
                return sessionHandler.ProcessReceiveAsync(datagram);
            }
            catch (Exception ex)
            {
                CustomLoggerFacade.Log(() =>
                    new CustomLogEvent("Error occured during message " +
                            $"receipt handling from {remoteEndpoint}", ex)
                        .AddProperty(LogDataKeyLogPositionId, "bb741504-3a4b-4ea3-a749-21fc8aec347f"));
                return PromiseApi.CompletedPromise();
            }
        }

        public void RequestSessionDispose(GenericNetworkIdentifier remoteEndpoint,
            string sessionId, SessionDisposedException cause)
        {
            // Fire outside of event loop thread if possible.
            var newLogicalThreadId = GenerateAndRecordLogicalThreadId();
            Task.Run(() => {
                var promise = PromiseApi.StartLogicalThread(newLogicalThreadId)
                    .ThenCompose(_ => DisposeSessionAsync(remoteEndpoint, sessionId, cause))
                    .Finally(() => RecordAndEndLogicalThread());
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
            return PromiseApi.CompletedPromise();
        }

        private AbstractPromise<VoidType> SwallowException(AbstractPromise<VoidType> promise)
        {
            return promise.CatchCompose(ex =>
            {
                CustomLoggerFacade.Log(() => new CustomLogEvent(
                        "Error encountered while disposing session handler", ex)
                    .AddProperty(LogDataKeyLogPositionId, "86a662a4-c098-4053-ac26-32b984079419"));
                return PromiseApi.CompletedPromise();
            });
        }

        public AbstractPromise<VoidType> ShutdownAsync(int waitPeriod)
        {
            // it is enough to prevent creation of new session handlers
            lock (_isShuttingDownLock)
            {
                _isShuttingDown = true;
            }
            return PromiseApi.CompletedPromise();
        }

        public bool IsShuttingDown()
        {
            lock (_isShuttingDownLock)
            {
                return _isShuttingDown;
            }
        }

        private Guid GenerateAndRecordLogicalThreadId()
        {
            var logicalThreadId = Guid.NewGuid();
            CustomLoggerFacade.TestLog(() =>
            {
                var logEvent = new CustomLogEvent("Starting new logical thread")
                    .AddProperty(LogDataKeyNewLogicalThreadId, logicalThreadId)
                    .AddProperty(LogDataKeyCurrentLogicalThreadId, PromiseApi.CurrentLogicalThreadId);
                return logEvent;
            });
            return logicalThreadId;
        }

        private void RecordAndEndLogicalThread()
        {
            CustomLoggerFacade.TestLog(() =>
            {
                var logEvent = new CustomLogEvent("Ending current logical thread")
                    .AddProperty(LogDataKeyEndingLogicalThreadId, PromiseApi.CurrentLogicalThreadId);
                return logEvent;
            });
            PromiseApi.EndCurrentLogicalThread();
        }

        private void Record(string logPosition, Action<CustomLogEvent> logEventReceiver)
        {
            var logEvent = new CustomLogEvent()
                .AddProperty(LogDataKeyCurrentLogicalThreadId, PromiseApi.CurrentLogicalThreadId)
                .AddProperty(LogDataKeyLogPositionId, logPosition);
            logEventReceiver.Invoke(logEvent);
            CustomLoggerFacade.TestLog(() => logEvent);
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
