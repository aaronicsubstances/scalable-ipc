using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core.ProtocolOperation
{
    public class EndpointStructuredDatastore<T>
    {
        private readonly Dictionary<GenericNetworkIdentifier,
               Dictionary<string, T>> _backingDatastore;

        public EndpointStructuredDatastore()
        {
            _backingDatastore = new Dictionary<GenericNetworkIdentifier,
                Dictionary<string, T>>();
        }

        public T Get(GenericNetworkIdentifier remoteEndpoint, string messageId, T defaultValue)
        {
            if (remoteEndpoint != null && _backingDatastore.ContainsKey(remoteEndpoint))
            {
                var subDict = _backingDatastore[remoteEndpoint];
                if (messageId != null && subDict.ContainsKey(messageId))
                {
                    return subDict[messageId];
                }
            }
            return defaultValue;
        }

        public bool Add(GenericNetworkIdentifier remoteEndpoint, string messageId, T value)
        {
            if (remoteEndpoint == null)
            {
                throw new ArgumentNullException(nameof(remoteEndpoint));
            }
            if (messageId == null)
            {
                throw new ArgumentNullException(nameof(messageId));
            }
            Dictionary<string, T> subDict;
            if (_backingDatastore.ContainsKey(remoteEndpoint))
            {
                subDict = _backingDatastore[remoteEndpoint];
            }
            else
            {
                subDict = new Dictionary<string, T>();
                _backingDatastore.Add(remoteEndpoint, subDict);
            }
            if (subDict.ContainsKey(messageId))
            {
                return false;
            }
            subDict.Add(messageId, value);
            return true;
        }

        public bool Remove(GenericNetworkIdentifier remoteEndpoint, string messageId)
        {
            if (remoteEndpoint != null && _backingDatastore.ContainsKey(remoteEndpoint))
            {
                var subDict = _backingDatastore[remoteEndpoint];
                if (messageId != null && subDict.ContainsKey(messageId))
                {
                    subDict.Remove(messageId);
                    if (subDict.Count == 0)
                    {
                        _backingDatastore.Remove(remoteEndpoint);
                    }
                    return true;
                }
            }
            return false;
        }

        public bool RemoveAll(GenericNetworkIdentifier remoteEndpoint)
        {
            if (remoteEndpoint != null)
            {
                return _backingDatastore.Remove(remoteEndpoint);
            }
            return false;
        }

        public List<string> GetMessageIds(GenericNetworkIdentifier remoteEndpoint)
        {
            if (remoteEndpoint != null && _backingDatastore.ContainsKey(remoteEndpoint))
            {
                var messageIds = _backingDatastore[remoteEndpoint].Keys.ToList();
                return messageIds;
            }
            return new List<string>();
        }

        public int GetValueCount(GenericNetworkIdentifier remoteEndpoint)
        {
            if (remoteEndpoint != null && _backingDatastore.ContainsKey(remoteEndpoint))
            {
                return _backingDatastore[remoteEndpoint].Keys.Count;
            }
            return 0;
        }

        public List<T> GetValues(GenericNetworkIdentifier remoteEndpoint)
        {
            if (remoteEndpoint != null && _backingDatastore.ContainsKey(remoteEndpoint))
            {
                var values = _backingDatastore[remoteEndpoint].Values.ToList();
                return values;
            }
            return new List<T>();
        }

        public List<GenericNetworkIdentifier> GetEndpoints()
        {
            var endpoints = _backingDatastore.Keys.ToList();
            return endpoints;
        }

        public int GetEndpointCount()
        {
            return _backingDatastore.Keys.Count;
        }
    }
}
