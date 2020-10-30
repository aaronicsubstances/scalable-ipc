using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace ScalableIPC.Core
{
    public class ProtocolEndpointHandler: IEndpointHandler
    {
        private readonly Dictionary<IPEndPoint, Dictionary<Guid, ISessionHandler>> _sessionHandlerMap;
        private readonly AbstractPromise<VoidType> _voidReturnPromise;
        private readonly object _disposeLock = new object();
        private bool _isDisposing = false;

        public ProtocolEndpointHandler(AbstractNetworkApi networkSocket, EndpointConfig endpointConfig,
            AbstractPromiseApi promiseApi)
        {
            NetworkSocket = networkSocket;
            EndpointConfig = endpointConfig;
            PromiseApi = promiseApi;
            _sessionHandlerMap = new Dictionary<IPEndPoint, Dictionary<Guid, ISessionHandler>>();
            _voidReturnPromise = PromiseApi.Resolve(VoidType.Instance);
        }

        public AbstractNetworkApi NetworkSocket { get; }
        public EndpointConfig EndpointConfig { get; }

        public AbstractPromiseApi PromiseApi { get; }

        public AbstractPromise<VoidType> OpenSession(IPEndPoint remoteEndpoint, ISessionHandler sessionHandler,
            ProtocolDatagram message)
        {
            sessionHandler.EndpointHandler = this;
            sessionHandler.RemoteEndpoint = remoteEndpoint;
            if (sessionHandler.SessionId == Guid.Empty)
            {
                sessionHandler.SessionId = Guid.NewGuid();
            }
            if (message.SessionId == null)
            {
                message.SessionId = sessionHandler.SessionId;
            }
            lock (_sessionHandlerMap)
            {
                Dictionary<Guid, ISessionHandler> subDict;
                if (_sessionHandlerMap.ContainsKey(remoteEndpoint))
                {
                    subDict = _sessionHandlerMap[remoteEndpoint];
                }
                else
                {
                    subDict = new Dictionary<Guid, ISessionHandler>();
                    _sessionHandlerMap.Add(remoteEndpoint, subDict);
                }
                subDict.Add(sessionHandler.SessionId, sessionHandler);
            }
            return sessionHandler.ProcessSend(message);
        }

        public void HandleReceive(IPEndPoint remoteEndpoint, byte[] rawBytes, int offset, int length)
        {
            lock (_disposeLock)
            {
                if (_isDisposing)
                {
                    throw new Exception("endpoint handler is shutting down");
                }
            }

            // process data from datagram socket.
            ProtocolDatagram message = ParseRawDatagram(rawBytes, offset, length);
            var handled = HandleReceiveProtocolControlMessage(remoteEndpoint, message);
            if (handled)
            {
                return;
            }
            ISessionHandler sessionHandler = GetOrCreateSessionHandler(remoteEndpoint, message.SessionId);
            if (sessionHandler != null)
            {
                sessionHandler.ProcessReceive(message);
            }
            else
            {
                throw new Exception($"Could not allocate handler for session {message.SessionId} from {remoteEndpoint}");
            }
        }

        public ProtocolDatagram ParseRawDatagram(byte[] rawBytes, int offset, int length)
        {
            // subclasses can implement forward error correction, expiration, etc.

            var message = ProtocolDatagram.Parse(rawBytes, offset, length);

            // validate op code
            switch (message.OpCode)
            {
                case ProtocolDatagram.OpCodeAck:
                case ProtocolDatagram.OpCodeClose:
                case ProtocolDatagram.OpCodeCloseAll:
                case ProtocolDatagram.OpCodeData:
                case ProtocolDatagram.OpCodeOpen:
                case ProtocolDatagram.OpCodeOpenAck:
                    break;
                default:
                    throw new Exception($"Invalid op code: {message.OpCode}");
            }
            return message;
        }

        public bool HandleReceiveProtocolControlMessage(IPEndPoint remoteEndpoint, ProtocolDatagram message)
        {
            if (message.OpCode == ProtocolDatagram.OpCodeCloseAll)
            {
                CloseAllEndpointSessions(remoteEndpoint);
                return true;
            }
            return false;
        }

        public AbstractPromise<VoidType> HandleSend(IPEndPoint remoteEndpoint, ProtocolDatagram message)
        {
            lock (_disposeLock)
            {
                if (_isDisposing)
                {
                    return PromiseApi.Reject(new Exception("endpoint handler is shutting down"));
                }
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
            return HandleException(NetworkSocket.HandleSend(remoteEndpoint, pdu, 0, pdu.Length));
        }

        public byte[] GenerateRawDatagram(ProtocolDatagram message)
        {
            // subclasses can implement forward error correction, expiration, maximum length validation, etc.
            byte[] rawBytes = message.ToRawDatagram(true);
            return rawBytes;
        }

        private AbstractPromise<VoidType> HandleSendCloseAll(IPEndPoint remoteEndpoint)
        {
            ProtocolDatagram pdu = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeCloseAll,
                SessionId = Guid.Empty, // null session id.
            };
            // swallow any send exception.
            return HandleSend(remoteEndpoint, pdu)
                .CatchCompose(_ => _voidReturnPromise)
                .Then(_ => { CloseAllEndpointSessions(remoteEndpoint); return VoidType.Instance; });
        }

        public AbstractPromise<VoidType> Shutdown()
        {
            // swallow exceptions.
            lock (_disposeLock)
            {
                if (_isDisposing)
                {
                    return _voidReturnPromise;
                }
                _isDisposing = true;
            }

            List<IPEndPoint> endpoints;
            lock (_sessionHandlerMap)
            {
                endpoints = _sessionHandlerMap.Keys.ToList();
            }
            var retVal = _voidReturnPromise;
            foreach (var endpoint in endpoints)
            {
                retVal = _voidReturnPromise.ThenCompose(_ => HandleSendCloseAll(endpoint));
            }
            lock (_sessionHandlerMap)
            {
                _sessionHandlerMap.Clear();
            }

            return retVal;
        }

        private void CloseAllEndpointSessions(IPEndPoint remoteEndpoint)
        {
            var sessionHandlersSubset = new List<ISessionHandler>();
            lock (_sessionHandlerMap)
            {
                if (_sessionHandlerMap.ContainsKey(remoteEndpoint))
                {
                    sessionHandlersSubset = _sessionHandlerMap[remoteEndpoint].Values.ToList();
                    _sessionHandlerMap.Remove(remoteEndpoint);
                }
            }
            foreach (var sessionHandler in sessionHandlersSubset)
            {
                SwallowException(sessionHandler.Shutdown(null, false));
            }
        }

        public AbstractPromise<VoidType> HandleException(AbstractPromise<VoidType> promise)
        {
            return promise.Catch(err =>
            {
                // log.
            });
        }

        public AbstractPromise<VoidType> SwallowException(AbstractPromise<VoidType> promise)
        {
            return promise.CatchCompose(err =>
            {
                // log.
                return _voidReturnPromise;
            });
        }

        public void RemoveSessionHandler(IPEndPoint remoteEndpoint, Guid sessionId)
        {
            lock (_sessionHandlerMap)
            {
                if (_sessionHandlerMap.ContainsKey(remoteEndpoint))
                {
                    var subDict = _sessionHandlerMap[remoteEndpoint];
                    if (subDict.ContainsKey(sessionId))
                    {
                        subDict.Remove(sessionId);
                        if (subDict.Count == 0)
                        {
                            _sessionHandlerMap.Remove(remoteEndpoint);
                        }
                    }
                }
            }
        }

        private ISessionHandler GetOrCreateSessionHandler(IPEndPoint remoteEndpoint, Guid sessionId)
        {
            lock (_sessionHandlerMap)
            {
                // handle case in which session handlers must always be created externally,
                // e.g. in client mode
                Dictionary<Guid, ISessionHandler> subDict = null;
                if (_sessionHandlerMap.ContainsKey(remoteEndpoint))
                {
                    subDict = _sessionHandlerMap[remoteEndpoint];
                }
                ISessionHandler sessionHandler = null;
                if (subDict != null && subDict.ContainsKey(sessionId))
                {
                    sessionHandler = subDict[sessionId];
                }
                else
                {
                    if (EndpointConfig.SessionHandlerFactory != null)
                    {
                        sessionHandler = EndpointConfig.SessionHandlerFactory.Create(remoteEndpoint, sessionId);
                    }
                    if (sessionHandler != null)
                    {
                        if (subDict == null)
                        {
                            subDict = new Dictionary<Guid, ISessionHandler>();
                            _sessionHandlerMap.Add(remoteEndpoint, subDict);
                        }
                        subDict.Add(sessionId, sessionHandler);
                    }
                }
                return sessionHandler;
            }
        }
    }
}
