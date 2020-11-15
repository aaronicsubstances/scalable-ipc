using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.ConcreteComponents;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ScalableIPC.Core
{
    public class NetworkTransportBase : INetworkTransportInterface
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
        public IPEndPoint LocalEndpoint { get; set; }
        public int IdleTimeoutSecs { get; set; }
        public int AckTimeoutSecs { get; set; }
        public int MaxSendWindowSize { get; set; }
        public int MaxReceiveWindowSize { get; set; }
        public int MaxRetryCount { get; set; }
        public int MaximumTransferUnitSize { get; set; }
        public ISessionHandlerFactory SessionHandlerFactory { get; set; }

        public virtual AbstractPromise<VoidType> HandleReceive(IPEndPoint remoteEndpoint,
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

            ISessionHandlerWrapper sessionHandlerWrapper;
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

        public virtual AbstractPromise<VoidType> HandleSend(IPEndPoint remoteEndpoint, ProtocolDatagram message)
        {
            if (_isDisposing)
            {
                return PromiseApi.Reject(new Exception("endpoint handler is shutting down"));
            }

            // handle opening window messages separately.
            if (message.WindowId == 0 && message.OpCode == ProtocolDatagram.OpCodeData)
            {
                return HandleSendOpeningWindowMessage(remoteEndpoint, message);
            }

            byte[] pdu;
            try
            {
                pdu = GenerateRawDatagram(message);
            }
            catch (Exception ex)
            {
                return PromiseApi.Reject(ex);
            }
            // send through network.
            return HandleSendData(remoteEndpoint, message.SessionId, pdu, 0, pdu.Length);
        }

        protected virtual byte[] GenerateRawDatagram(ProtocolDatagram message)
        {
            // subclasses can implement forward error correction, expiration, maximum length validation, etc.
            byte[] rawBytes = message.ToRawDatagram(true);
            return rawBytes;
        }

        public virtual AbstractPromise<VoidType> Shutdown()
        {
            List<IPEndPoint> endpoints;
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

        private AbstractPromise<VoidType> HandleSendCloseAll(IPEndPoint remoteEndpoint)
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

        public virtual void OnCloseSession(IPEndPoint remoteEndpoint, string sessionId, Exception error, bool timeout)
        {
            // invoke in different thread outside event loop?
            CloseSession(remoteEndpoint, sessionId, error, timeout);
        }

        public virtual AbstractPromise<VoidType> CloseSession(IPEndPoint remoteEndpoint, string sessionId,
            Exception error, bool timeout)
        {
            ISessionHandlerWrapper sessionHandler;
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

        public virtual AbstractPromise<VoidType> CloseSessions(IPEndPoint remoteEndpoint)
        {
            List<ISessionHandlerWrapper> sessionHandlers;
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

        public virtual AbstractPromise<ISessionHandler> OpenSession(IPEndPoint remoteEndpoint, string sessionId = null,
            ISessionHandler sessionHandler = null)
        {
            throw new NotImplementedException();
        }

        protected virtual AbstractPromise<VoidType> HandleReceiveOpeningWindowMessage(IPEndPoint remoteEndpoint,
            ProtocolDatagram message)
        {
            throw new NotImplementedException();
        }

        protected virtual AbstractPromise<VoidType> HandleSendOpeningWindowMessage(IPEndPoint remoteEndpoint,
            ProtocolDatagram message)
        {
            throw new NotImplementedException();
        }

        protected virtual AbstractPromise<VoidType> HandleSendData(IPEndPoint remoteEndpoint, string sessionId, byte[] data, 
            int offset, int length)
        {
            throw new NotImplementedException();
        }
    }
}
