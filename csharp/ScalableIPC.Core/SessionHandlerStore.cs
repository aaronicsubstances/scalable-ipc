using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace ScalableIPC.Core
{
    public class SessionHandlerStore
    {
        private readonly Dictionary<IPEndPoint, Dictionary<string, ISessionHandlerWrapper>> _sessionHandlerMap;

        public SessionHandlerStore()
        {
            _sessionHandlerMap = new Dictionary<IPEndPoint, Dictionary<string, ISessionHandlerWrapper>>();
        }

        public ISessionHandlerWrapper Get(IPEndPoint remoteEndpoint, string sessionId)
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
        
        public void Add(IPEndPoint remoteEndpoint, string sessionId, ISessionHandlerWrapper value)
        {
            Dictionary<string, ISessionHandlerWrapper> subDict;
            if (_sessionHandlerMap.ContainsKey(remoteEndpoint))
            {
                subDict = _sessionHandlerMap[remoteEndpoint];
            }
            else
            {
                subDict = new Dictionary<string, ISessionHandlerWrapper>();
                _sessionHandlerMap.Add(remoteEndpoint, subDict);
            }
            if (subDict.ContainsKey(sessionId))
            {
                throw new Exception($"Session {sessionId} is already present with a handler");
            }
            subDict.Add(sessionId, value);
        }

        public bool Remove(IPEndPoint remoteEndpoint, string sessionId)
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

        public bool Remove(IPEndPoint remoteEndpoint)
        {
            if (_sessionHandlerMap.ContainsKey(remoteEndpoint))
            {
                _sessionHandlerMap.Remove(remoteEndpoint);
                return true;
            }
            return false;
        }

        public List<string> GetSessionIds(IPEndPoint remoteEndpoint)
        {
            if (!_sessionHandlerMap.ContainsKey(remoteEndpoint))
            {
                return new List<string>();
            }
            var sessionIds = _sessionHandlerMap[remoteEndpoint].Keys.ToList();
            return sessionIds;
        }

        public int GetSessionCount(IPEndPoint remoteEndpoint)
        {
            if (!_sessionHandlerMap.ContainsKey(remoteEndpoint))
            {
                return 0;
            }
            return _sessionHandlerMap[remoteEndpoint].Keys.Count;
        }

        public List<ISessionHandlerWrapper> GetSessionHandlers(IPEndPoint remoteEndpoint)
        {
            if (!_sessionHandlerMap.ContainsKey(remoteEndpoint))
            {
                return new List<ISessionHandlerWrapper>();
            }
            var sessionHandlers = _sessionHandlerMap[remoteEndpoint].Values.ToList();
            return sessionHandlers;
        }

        public List<IPEndPoint> GetEndpoints()
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
