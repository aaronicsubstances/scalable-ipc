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

        public AbstractPromise<VoidType> HandleReceive(IPEndPoint endpoint, byte[] rawBytes, int offset, int length)
        {
            lock (_disposeLock)
            {
                if (_isDisposing)
                {
                    return PromiseApi.Reject(new Exception("endpoint handler is shutting down"));
                }
            }

            // process data from datagram socket.
            ProtocolDatagram message;
            try
            {
                message = ProtocolDatagram.Parse(rawBytes, offset, length);
            }
            catch (SessionDatagramParseException ex)
            {
                return HandleErrorReceive(endpoint, ex.SessionId);
            }
            catch (Exception ex)
            {
                return PromiseApi.Reject(ex);
            }
            var returnPromise = HandleReceiveProtocolControlMessage(endpoint, message);
            if (returnPromise != null)
            {
                return returnPromise;
            }
            ISessionHandler sessionHandler = GetOrCreateSessionHandler(endpoint, message.SessionId);
            if (sessionHandler != null)
            {
                return sessionHandler.ProcessReceive(message);
            }
            else
            {
                return PromiseApi.Reject(new Exception($"Could not allocate handler for session {message.SessionId} from {endpoint}"));
            }
        }

        public AbstractPromise<VoidType> HandleReceiveProtocolControlMessage(IPEndPoint endpoint, ProtocolDatagram message)
        {
            if (message.OpCode == ProtocolDatagram.OpCodeCloseAll)
            {
                return HandleReceiveCloseAll(endpoint);
            }
            return null;
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

            // send through datagram socket.
            byte[] pdu;
            try
            {
                pdu = message.ToRawDatagram();
            }
            catch (Exception ex)
            {
                return PromiseApi.Reject(ex);
            }
            return HandleException(NetworkSocket.HandleSend(endpoint, pdu, 0, pdu.Length));
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
                ThenCompose(_ => HandleReceiveCloseAll(endpoint), _ => _voidReturnPromise);
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

        private AbstractPromise<VoidType> HandleReceiveCloseAll(IPEndPoint endpoint)
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
            AbstractPromise<VoidType> retResult = _voidReturnPromise;
            foreach (var sessionHandler in sessionHandlersSubset)
            {
                var nextResult = SwallowException(sessionHandler.Close(null, false));
                retResult = retResult.ThenCompose(_ => nextResult);
            }
            return retResult;
        }

        private AbstractPromise<VoidType> HandleErrorReceive(IPEndPoint endpoint, string sessionId)
        {
            ISessionHandler sessionHandler = null;
            lock (_sessionHandlerMap)
            {
                if (_sessionHandlerMap.ContainsKey(endpoint))
                {
                    var subDict = _sessionHandlerMap[endpoint];
                    if (subDict.ContainsKey(sessionId))
                    {
                        sessionHandler = subDict[sessionId];
                    }
                }
            }
            return sessionHandler?.ProcessErrorReceive() ?? _voidReturnPromise;
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
