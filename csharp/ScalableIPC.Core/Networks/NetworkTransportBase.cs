using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using ScalableIPC.Core.Session;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Networks
{
    public abstract class NetworkTransportBase : INetworkTransportInterface
    {
        protected readonly SessionHandlerStore _sessionHandlerStore;
        protected volatile bool _isShuttingDown;

        public NetworkTransportBase()
        {
            _sessionHandlerStore = new SessionHandlerStore();
            PromiseApi = new DefaultPromiseApi();
            EventLoop = new DefaultEventLoopApi();
        }

        public AbstractPromiseApi PromiseApi { get; set; }
        public AbstractEventLoopApi EventLoop { get; set; }
        public GenericNetworkIdentifier LocalEndpoint { get; set; }
        public int IdleTimeoutSecs { get; set; }
        public int MinRemoteIdleTimeoutSecs { get; set; }
        public int MaxRemoteIdleTimeoutSecs { get; set; }
        public int AckTimeoutSecs { get; set; }
        public int MaxSendWindowSize { get; set; }
        public int MaxReceiveWindowSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int MaximumTransferUnitSize { get; set; }
        public ISessionHandlerFactory SessionHandlerFactory { get; set; }

        public virtual AbstractPromise<VoidType> HandleReceiveAsync(GenericNetworkIdentifier remoteEndpoint,
             byte[] rawBytes, int offset, int length)
        {
            // Process data from underlying network.
            ProtocolDatagram message;
            try
            {
                message = ParseRawDatagram(rawBytes, offset, length);
            }
            catch (Exception ex)
            {
                return PromiseApi.Reject(ex);
            }

            // handle protocol control messages
            if (message.OpCode == ProtocolDatagram.OpCodeCloseAll)
            {
                return CloseSessionsAsync(remoteEndpoint, 
                    new SessionDisposedException(true, ProtocolDatagram.AbortCodeCloseAll));
            }

            // split session processing in two to enable connection-oriented transports
            // to associate connections with new sessions.
            return PrepareReceiveDataAsync(remoteEndpoint, message)
                .ThenCompose(_ => CompleteReceiveDataAsync(remoteEndpoint, message));
        }

        protected virtual AbstractPromise<VoidType> PrepareReceiveDataAsync(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram message)
        {
            // By default for connection-less transports, get existing session handler
            // or create new one.
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
                    if (_isShuttingDown)
                    {
                        // silently ignore new session if shutting down.
                        return PromiseApi.Resolve(VoidType.Instance);
                    }

                    sessionHandler = SessionHandlerFactory.Create();
                    sessionHandler.CompleteInit(message.SessionId, true, this, remoteEndpoint);
                    _sessionHandlerStore.Add(remoteEndpoint, message.SessionId,
                        CreateSessionHandlerWrapper(sessionHandler));

                }
            }
            return PromiseApi.Resolve(VoidType.Instance);
        }

        protected virtual AbstractPromise<VoidType> CompleteReceiveDataAsync(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram message)
        {
            SessionHandlerWrapper sessionHandlerWrapper;
            lock (_sessionHandlerStore)
            {
                sessionHandlerWrapper = _sessionHandlerStore.Get(remoteEndpoint, message.SessionId);
            }
            if (sessionHandlerWrapper != null)
            {
                return sessionHandlerWrapper.SessionHandler.ProcessReceiveAsync(message);
            }
            else
            {
                // missing session handler not a problem if shutting down.
                if (_isShuttingDown)
                {
                    return PromiseApi.Resolve(VoidType.Instance);
                }
                else
                {
                    return PromiseApi.Reject(new Exception($"Could not allocate handler for session " +
                        $"{message.SessionId} from {remoteEndpoint}"));
                }
            }
        }

        protected virtual ProtocolDatagram ParseRawDatagram(byte[] rawBytes, int offset, int length)
        {
            // subclasses can implement forward error correction, expiration, etc.

            var message = ProtocolDatagram.Parse(rawBytes, offset, length);

            // validate op code
            switch (message.OpCode)
            {
                case ProtocolDatagram.OpCodeData:
                case ProtocolDatagram.OpCodeAck:
                case ProtocolDatagram.OpCodeClose:
                case ProtocolDatagram.OpCodeCloseAll:
                    break;
                default:
                    throw new Exception($"Invalid op code: {message.OpCode}");
            }
            return message;
        }

        protected virtual AbstractPromise<VoidType> SwallowException(AbstractPromise<VoidType> promise)
        {
            return promise.CatchCompose(err =>
            {
                CustomLoggerFacade.Log(() => new CustomLogEvent("27d232da-f4e4-4f25-baeb-56bd53ed49fa",
                    "Exception occurred here", err));
                return PromiseApi.Resolve(VoidType.Instance);
            });
        }

        public virtual AbstractPromise<ISessionHandler> OpenSessionAsync(GenericNetworkIdentifier remoteEndpoint,
            string sessionId = null, ISessionHandler sessionHandler = null)
        {
            if (sessionId == null)
            {
                sessionId = ProtocolDatagram.GenerateSessionId();
            }
            if (sessionHandler == null)
            {
                sessionHandler = SessionHandlerFactory.Create();
            }
            sessionHandler.CompleteInit(sessionId, true, this, remoteEndpoint);
            lock (_sessionHandlerStore)
            {
                _sessionHandlerStore.Add(remoteEndpoint, sessionId,
                    CreateSessionHandlerWrapper(sessionHandler));
            }
            return PromiseApi.Resolve(sessionHandler);
        }

        public virtual AbstractPromise<VoidType> ShutdownAsync(int waitSecs)
        {
            if (_isShuttingDown)
            {
                return PromiseApi.Resolve(VoidType.Instance);
            }
            
            // stop receiving new sessions.
            _isShuttingDown = true;

            // interpret positive waitSecs to mean
            // wait for existing sessions to complete on their own,
            // before tearing them down forcefully.
            if (waitSecs > 0)
            {
                return PromiseApi.Delay(waitSecs).ThenCompose(_ => ShutdownSessionsAsync());
            }
            else
            {
                return ShutdownSessionsAsync();
            }
        }

        protected AbstractPromise<VoidType> ShutdownSessionsAsync()
        {
            List<GenericNetworkIdentifier> endpoints;
            lock (_sessionHandlerStore)
            {
                endpoints = _sessionHandlerStore.GetEndpoints();
            }
            var retVal = PromiseApi.Resolve(VoidType.Instance);
            foreach (var endpoint in endpoints)
            {
                retVal = retVal.ThenCompose(_ => HandleSendCloseAll(endpoint));
            }
            return retVal;
        }

        private AbstractPromise<VoidType> HandleSendCloseAll(GenericNetworkIdentifier remoteEndpoint)
        {
            ProtocolDatagram pdu = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeCloseAll,
                SessionId = ProtocolDatagram.GenerateSessionId(),
            };
            // swallow any send exception.
            return HandleSendAsync(remoteEndpoint, pdu)
                .CatchCompose(_ => PromiseApi.Resolve(VoidType.Instance))
                .ThenCompose(_ => CloseSessionsAsync(remoteEndpoint, 
                    new SessionDisposedException(false, ProtocolDatagram.AbortCodeShutdown)));
        }

        public virtual void OnCloseSession(GenericNetworkIdentifier remoteEndpoint, string sessionId, 
            SessionDisposedException cause)
        {
            // invoke in different thread outside event loop?
            _ = CloseSessionAsync(remoteEndpoint, sessionId, cause);
        }

        // separate from OnCloseSession so it can be overriden to tear down connections if need be.
        public virtual AbstractPromise<VoidType> CloseSessionAsync(GenericNetworkIdentifier remoteEndpoint, string sessionId,
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
        }

        protected virtual SessionHandlerWrapper CreateSessionHandlerWrapper(ISessionHandler sessionHandler)
        {
            return new SessionHandlerWrapper(sessionHandler);
        }

        // Implementations must deal with CloseAll messages separately from other messages.
        // For connection-oriented transports, implementations must also identify the connection
        // to use for message, or create new connections for the very first message of a session
        // i.e., Data messages with window = 0 and seqNr = 0.
        public abstract AbstractPromise<VoidType> HandleSendAsync(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram message);
    }
}
