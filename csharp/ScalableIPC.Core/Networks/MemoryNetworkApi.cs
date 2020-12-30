using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using ScalableIPC.Core.Helpers;
using ScalableIPC.Core.Networks.Common;
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

        public ISendBehaviour SendBehaviour { get; set; }

        public ITransmissionBehaviour TransmissionBehaviour { get; set; }

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
                try
                {
                    var promise = HandleSendAsync(remoteEndpoint, message);
                    await ((DefaultPromise<VoidType>)promise).WrappedTask;
                    cb(null);
                }
                catch (Exception ex)
                {
                    cb(ex);
                }
            });
        }

        public AbstractPromise<VoidType> HandleSendAsync(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram datagram)
        {
            // simulate sending.

            SendConfig sendConfig = null;
            if (SendBehaviour != null)
            {
                sendConfig = SendBehaviour.Create(remoteEndpoint, datagram);
            }

            // interpret null send config as immediate success.
            AbstractPromise<VoidType> sendResult = DefaultPromiseApi.CompletedPromise;
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
                Task.Run(() => connectedNetwork.HandleReceiveAsync(LocalEndpoint, datagram));
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
                Task.Run(() => {
                    Task transmissionResult;
                    if (transmissionDelay > 0)
                    {
                        transmissionResult = Task.Delay(transmissionDelay);
                    }
                    else
                    {
                        transmissionResult = Task.CompletedTask;
                    }
                    return transmissionResult.ContinueWith(_ =>
                    {
                        ProtocolDatagram deserialized;
                        if (serialized != null)
                        {
                            deserialized = ProtocolDatagram.Parse(serialized, 0, serialized.Length);
                        }
                        else
                        {
                            deserialized = datagram;
                        }
                        return connectedNetwork.HandleReceiveAsync(LocalEndpoint, deserialized);
                    });
                });
            }

            return sendResult;
        }

        private Task HandleReceiveAsync(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram datagram)
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
                            return Task.CompletedTask;
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
                var promise = sessionHandler.ProcessReceiveAsync(datagram);
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
