using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.ProtocolOperation
{
    public class EndpointOwnerIdInfo
    {
        public string Id { get; set; }
        public object TimeoutId { get; set; }
    }
}
