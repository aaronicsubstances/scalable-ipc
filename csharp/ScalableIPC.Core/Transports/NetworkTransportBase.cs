using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.ConcreteComponents;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Transports
{
    public abstract class NetworkTransportBase : INetworkTransportInterface
    {
        protected readonly SessionHandlerStore _sessionHandlerStore;
        protected volatile bool _isDisposing = false;

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
        public int AckTimeoutSecs { get; set; }
        public int MaxSendWindowSize { get; set; }
        public int MaxReceiveWindowSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int MaximumTransferUnitSize { get; set; }
        public ISessionHandlerFactory SessionHandlerFactory { get; set; }

        public virtual AbstractPromise<VoidType> HandleReceive(GenericNetworkIdentifier remoteEndpoint,
             byte[] rawBytes, int offset, int length)
        {
            if (_isDisposing)
            {
                return PromiseApi.Reject(new Exception("endpoint handler is shutting down"));
            }

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
                return CloseSessions(remoteEndpoint);
            }

            // handle opening window messages separately.
            if (message.WindowId == 0 && message.OpCode == ProtocolDatagram.OpCodeData)
            {
                return HandleReceiveOpeningWindowMessage(remoteEndpoint, message);
            }

            SessionHandlerWrapper sessionHandlerWrapper;
            lock (_sessionHandlerStore)
            {
                sessionHandlerWrapper = _sessionHandlerStore.Get(remoteEndpoint, message.SessionId);
            }
            if (sessionHandlerWrapper != null)
            {
                return sessionHandlerWrapper.SessionHandler.ProcessReceive(message);
            }
            else
            {
                return PromiseApi.Reject(new Exception($"Could not allocate handler for session " +
                    $"{message.SessionId} from {remoteEndpoint}"));
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

        public virtual AbstractPromise<VoidType> HandleSend(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram message)
        {
            if (_isDisposing)
            {
                return PromiseApi.Reject(new Exception("endpoint handler is shutting down"));
            }

            try
            {
                // handle opening window messages separately.
                if (message.WindowId == 0 && message.OpCode == ProtocolDatagram.OpCodeData)
                {
                    return HandleSendOpeningWindowMessage(remoteEndpoint, message);
                }
                byte[] pdu = GenerateRawDatagram(message);
                // send through network.
                return HandleSendData(remoteEndpoint, message.SessionId, pdu, 0, pdu.Length);
            }
            catch (Exception ex)
            {
                return PromiseApi.Reject(ex);
            }
        }

        protected virtual byte[] GenerateRawDatagram(ProtocolDatagram message)
        {
            // subclasses can implement forward error correction, expiration, maximum length validation, etc.
            byte[] rawBytes = message.ToRawDatagram();
            return rawBytes;
        }

        public virtual AbstractPromise<VoidType> Shutdown()
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
                SessionId = ProtocolDatagram.GenerateSessionId()
            };
            // swallow any send exception.
            return HandleSend(remoteEndpoint, pdu)
                .CatchCompose(_ => PromiseApi.Resolve(VoidType.Instance))
                .ThenCompose(_ => CloseSessions(remoteEndpoint));
        }

        public virtual void OnCloseSession(GenericNetworkIdentifier remoteEndpoint, string sessionId, 
            Exception error, bool timeout)
        {
            // invoke in different thread outside event loop?
            _ = CloseSession(remoteEndpoint, sessionId, error, timeout);
        }

        public virtual AbstractPromise<VoidType> CloseSession(GenericNetworkIdentifier remoteEndpoint, string sessionId,
            Exception error, bool timeout)
        {
            SessionHandlerWrapper sessionHandler;
            lock (_sessionHandlerStore)
            {
                sessionHandler = _sessionHandlerStore.Get(remoteEndpoint, sessionId);
                _sessionHandlerStore.Remove(remoteEndpoint, sessionId);
            }
            if (sessionHandler != null)
            {
                return SwallowException(sessionHandler.SessionHandler.Close(error, timeout));
            }
            return PromiseApi.Resolve(VoidType.Instance);
        }

        public virtual AbstractPromise<VoidType> CloseSessions(GenericNetworkIdentifier remoteEndpoint)
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
                    sessionHandler.SessionHandler.Close(null, false)));
            }
            return retVal;
        }

        protected virtual SessionHandlerWrapper CreateSessionHandlerWrapper(ISessionHandler sessionHandler)
        {
            return new SessionHandlerWrapper(sessionHandler);
        }

        public virtual AbstractPromise<ISessionHandler> OpenSession(GenericNetworkIdentifier remoteEndpoint, 
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

        protected virtual AbstractPromise<VoidType> HandleReceiveOpeningWindowMessage(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram message)
        {
            // for receipt of window 0, reuse existing session handler or create one and add.
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
                    sessionHandler = SessionHandlerFactory.Create();
                    sessionHandler.CompleteInit(message.SessionId, true, this, remoteEndpoint);
                    _sessionHandlerStore.Add(remoteEndpoint, message.SessionId,
                        CreateSessionHandlerWrapper(sessionHandler));

                }
            }
            return sessionHandler.ProcessReceive(message);
        }

        protected virtual AbstractPromise<VoidType> HandleSendOpeningWindowMessage(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram message)
        {
            byte[] data = GenerateRawDatagram(message);
            return HandleSendData(remoteEndpoint, message.SessionId, data, 0, data.Length);
        }

        protected abstract AbstractPromise<VoidType> HandleSendData(GenericNetworkIdentifier remoteEndpoint,
            string sessionId, byte[] data, int offset, int length);
    }
}
