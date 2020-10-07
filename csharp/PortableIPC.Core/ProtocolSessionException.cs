using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core
{
    public class ProtocolSessionException: Exception
    {
        public ProtocolSessionException(string sessionId, string message) :
            base(message)
        {
            SessionId = sessionId;
        }

        public string SessionId { get; }
    }
}
