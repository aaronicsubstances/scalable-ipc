using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.ProtocolOperation
{
    public class DefaultEndpointInfoDatastore : IEndpointInfoDatastore
    {
        class Entry
        {
            public string EndpointOwnerId { get; set; }
            public int IndexInLruQueue { get; set; }
        }

        private readonly Dictionary<GenericNetworkIdentifier, Entry> endpointInfoEntries;
        private readonly List<GenericNetworkIdentifier> lruQueue;

        public DefaultEndpointInfoDatastore(int maxSize)
        {
            endpointInfoEntries = new Dictionary<GenericNetworkIdentifier, Entry>();
            lruQueue = new List<GenericNetworkIdentifier>();
            MaximumSize = maxSize;
        }

        public int MaximumSize { get; set; }

        public void Clear()
        {
            endpointInfoEntries.Clear();
            lruQueue.Clear();
        }

        public string Get(GenericNetworkIdentifier remoteEndpoint)
        {
            if (endpointInfoEntries.ContainsKey(remoteEndpoint))
            {
                return endpointInfoEntries[remoteEndpoint].EndpointOwnerId;
            }
            return null;
        }

        public void Update(GenericNetworkIdentifier remoteEndpoint, string endpointOwnerId)
        {
            int targetIndexInLruQueue; 
            if (endpointInfoEntries.ContainsKey(remoteEndpoint))
            {
                targetIndexInLruQueue = endpointInfoEntries[remoteEndpoint].IndexInLruQueue;
            }
            else
            {
                targetIndexInLruQueue = lruQueue.Count;
                endpointInfoEntries.Add(remoteEndpoint, new Entry
                {
                    IndexInLruQueue = targetIndexInLruQueue,
                    EndpointOwnerId = endpointOwnerId
                });
                lruQueue.Add(remoteEndpoint);
            }
            // update lru queue.
            if (targetIndexInLruQueue > 0)
            {
                // swap with next one above.
                // over time, will ensure frequently used ones get to the top.
                var temp = lruQueue[targetIndexInLruQueue - 1];
                lruQueue[targetIndexInLruQueue - 1] = remoteEndpoint;
                lruQueue[targetIndexInLruQueue] = temp;

                // update dictionary
                endpointInfoEntries[remoteEndpoint].IndexInLruQueue = targetIndexInLruQueue - 1;
                endpointInfoEntries[temp].IndexInLruQueue = targetIndexInLruQueue;
            }
            // trim entries.
            int maxQueueSize = MaximumSize;
            if (maxQueueSize < 1)
            {
                // arbitarily set max size default to 1,000.
                maxQueueSize = 1_000;
            }
            if (lruQueue.Count > maxQueueSize)
            {
                // evict the last one as the least recently used.
                var lruEndpoint = lruQueue[lruQueue.Count - 1];
                lruQueue.RemoveAt(lruQueue.Count - 1);
                endpointInfoEntries.Remove(lruEndpoint);
            }
        }
    }
}
