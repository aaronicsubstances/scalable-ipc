using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ScalableIPC.Core.Transports
{
    public class SessionHandlerStore
    {
        private readonly Dictionary<GenericNetworkIdentifier, 
            Dictionary<string, SessionHandlerWrapper>> _sessionHandlerMap;

        public SessionHandlerStore()
        {
            _sessionHandlerMap = new Dictionary<GenericNetworkIdentifier, 
                Dictionary<string, SessionHandlerWrapper>>();
        }

        public SessionHandlerWrapper Get(GenericNetworkIdentifier remoteEndpoint, string sessionId)
        {
            if (_sessionHandlerMap.ContainsKey(remoteEndpoint))
            {
                var subDict = _sessionHandlerMap[remoteEndpoint];
                if (subDict.ContainsKey(sessionId))
                {
                    return subDict[sessionId];
                }
            }
            return null;
        }
        
        public void Add(GenericNetworkIdentifier remoteEndpoint, string sessionId, SessionHandlerWrapper value)
        {
            Dictionary<string, SessionHandlerWrapper> subDict;
            if (_sessionHandlerMap.ContainsKey(remoteEndpoint))
            {
                subDict = _sessionHandlerMap[remoteEndpoint];
            }
            else
            {
                subDict = new Dictionary<string, SessionHandlerWrapper>();
                _sessionHandlerMap.Add(remoteEndpoint, subDict);
            }
            if (subDict.ContainsKey(sessionId))
            {
                throw new Exception($"Session {sessionId} at {remoteEndpoint} is already present with a handler");
            }
            subDict.Add(sessionId, value);
        }

        public bool Remove(GenericNetworkIdentifier remoteEndpoint, string sessionId)
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
                    return false;
                }
            }
            return false;
        }

        public bool Remove(GenericNetworkIdentifier remoteEndpoint)
        {
            if (_sessionHandlerMap.ContainsKey(remoteEndpoint))
            {
                _sessionHandlerMap.Remove(remoteEndpoint);
                return true;
            }
            return false;
        }

        public List<string> GetSessionIds(GenericNetworkIdentifier remoteEndpoint)
        {
            if (!_sessionHandlerMap.ContainsKey(remoteEndpoint))
            {
                return new List<string>();
            }
            var sessionIds = _sessionHandlerMap[remoteEndpoint].Keys.ToList();
            return sessionIds;
        }

        public int GetSessionCount(GenericNetworkIdentifier remoteEndpoint)
        {
            if (!_sessionHandlerMap.ContainsKey(remoteEndpoint))
            {
                return 0;
            }
            return _sessionHandlerMap[remoteEndpoint].Keys.Count;
        }

        public List<SessionHandlerWrapper> GetSessionHandlers(GenericNetworkIdentifier remoteEndpoint)
        {
            if (!_sessionHandlerMap.ContainsKey(remoteEndpoint))
            {
                return new List<SessionHandlerWrapper>();
            }
            var sessionHandlers = _sessionHandlerMap[remoteEndpoint].Values.ToList();
            return sessionHandlers;
        }

        public List<GenericNetworkIdentifier> GetEndpoints()
        {
            var endpoints = _sessionHandlerMap.Keys.ToList();
            return endpoints;
        }

        public int GetEndpointCount()
        {
            return _sessionHandlerMap.Keys.Count;
        }
    }
}
