using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace PortableIPC.Core
{
    public class ProtocolEndpointHandler: IEndpointHandler
    {
        private readonly Dictionary<IPEndPoint, Dictionary<string, ISessionHandler>> _sessionHandlerMap;
        private readonly AbstractPromise<VoidType> _voidReturnPromise;
        private readonly object _disposeLock = new object();
        private bool _isDisposing = false;

        public ProtocolEndpointHandler(AbstractNetworkApi networkSocket, EndpointConfig endpointConfig, AbstractPromiseApi promiseApi)
        {
            NetworkSocket = networkSocket;
            EndpointConfig = endpointConfig;
            PromiseApi = promiseApi;
            _sessionHandlerMap = new Dictionary<IPEndPoint, Dictionary<string, ISessionHandler>>();
            _voidReturnPromise = PromiseApi.Resolve(VoidType.Instance);
        }

        public AbstractNetworkApi NetworkSocket { get; }
        public EndpointConfig EndpointConfig { get; }

        public AbstractPromiseApi PromiseApi { get; }

        public AbstractPromise<VoidType> OpenSession(IPEndPoint endpoint, ISessionHandler sessionHandler,
            ProtocolDatagram message)
        {
            sessionHandler.EndpointHandler = this;
            sessionHandler.ConnectedEndpoint = endpoint;
            if (sessionHandler.SessionId == null)
            {
                sessionHandler.SessionId = EndpointConfig.GenerateSessionId();
            }
            if (message.SessionId == null)
            {
                message.SessionId = sessionHandler.SessionId;
            }
            lock (_sessionHandlerMap)
            {
                Dictionary<string, ISessionHandler> subDict;
                if (_sessionHandlerMap.ContainsKey(endpoint))
                {
                    subDict = _sessionHandlerMap[endpoint];
                }
                else
                {
                    subDict = new Dictionary<string, ISessionHandler>();
                    _sessionHandlerMap.Add(endpoint, subDict);
                }
                subDict.Add(sessionHandler.SessionId, sessionHandler);
            }
            return sessionHandler.ProcessSend(message);
        }

        public void HandleReceive(IPEndPoint endpoint, byte[] rawBytes, int offset, int length)
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
            var handled = HandleReceiveProtocolControlMessage(endpoint, message);
            if (handled)
            {
                return;
            }
            ISessionHandler sessionHandler = GetOrCreateSessionHandler(endpoint, message.SessionId);
            if (sessionHandler != null)
            {
                sessionHandler.ProcessReceive(message);
            }
            else
            {
                throw new Exception($"Could not allocate handler for session {message.SessionId} from {endpoint}");
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

        public bool HandleReceiveProtocolControlMessage(IPEndPoint endpoint, ProtocolDatagram message)
        {
            if (message.OpCode == ProtocolDatagram.OpCodeCloseAll)
            {
                HandleReceiveCloseAll(endpoint);
                return true;
            }
            return false;
        }

        public AbstractPromise<VoidType> HandleSend(IPEndPoint endpoint, ProtocolDatagram message)
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
            return HandleException(NetworkSocket.HandleSend(endpoint, pdu, 0, pdu.Length));
        }

        public byte[] GenerateRawDatagram(ProtocolDatagram message)
        {
            // subclasses can implement forward error correction, expiration, maximum length validation, etc.
            byte[] rawBytes = message.ToRawDatagram(true);
            return rawBytes;
        }

        private AbstractPromise<VoidType> HandleSendCloseAll(IPEndPoint endpoint)
        {
            ProtocolDatagram pdu = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeCloseAll,
                SessionId = EndpointConfig.GenerateNullSessionId(),
            };
            // swallow any send exception.
            return HandleSend(endpoint, pdu).
                ThenCompose(_ => { HandleReceiveCloseAll(endpoint); return _voidReturnPromise; }, 
                            _ => _voidReturnPromise);
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

        private void HandleReceiveCloseAll(IPEndPoint endpoint)
        {
            var sessionHandlersSubset = new List<ISessionHandler>();
            lock (_sessionHandlerMap)
            {
                if (_sessionHandlerMap.ContainsKey(endpoint))
                {
                    sessionHandlersSubset = _sessionHandlerMap[endpoint].Values.ToList();
                    _sessionHandlerMap.Remove(endpoint);
                }
            }
            foreach (var sessionHandler in sessionHandlersSubset)
            {
                SwallowException(sessionHandler.Shutdown(null, false));
            }
        }

        public AbstractPromise<VoidType> HandleException(AbstractPromise<VoidType> promise)
        {
            return promise.Then<VoidType>(null, err =>
            {
                // log.
            });
        }

        public AbstractPromise<VoidType> SwallowException(AbstractPromise<VoidType> promise)
        {
            return promise.ThenCompose(null, err =>
            {
                // log.
                return _voidReturnPromise;
            });
        }

        public void RemoveSessionHandler(IPEndPoint endpoint, string sessionId)
        {
            lock (_sessionHandlerMap)
            {
                if (_sessionHandlerMap.ContainsKey(endpoint))
                {
                    var subDict = _sessionHandlerMap[endpoint];
                    if (subDict.ContainsKey(sessionId))
                    {
                        subDict.Remove(sessionId);
                        if (subDict.Count == 0)
                        {
                            _sessionHandlerMap.Remove(endpoint);
                        }
                    }
                }
            }
        }

        private ISessionHandler GetOrCreateSessionHandler(IPEndPoint endpoint, string sessionId)
        {
            lock (_sessionHandlerMap)
            {
                // handle case in which session handlers must always be created externally,
                // e.g. in client mode
                Dictionary<string, ISessionHandler> subDict = null;
                if (_sessionHandlerMap.ContainsKey(endpoint))
                {
                    subDict = _sessionHandlerMap[endpoint];
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
                        sessionHandler = EndpointConfig.SessionHandlerFactory.Create(endpoint, sessionId);
                    }
                    if (sessionHandler != null)
                    {
                        if (subDict == null)
                        {
                            subDict = new Dictionary<string, ISessionHandler>();
                            _sessionHandlerMap.Add(endpoint, subDict);
                        }
                        subDict.Add(sessionId, sessionHandler);
                    }
                }
                return sessionHandler;
            }
        }
    }
}
