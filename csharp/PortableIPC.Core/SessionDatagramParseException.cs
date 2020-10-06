using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core
{
    public class SessionDatagramParseException: Exception
    {
        public SessionDatagramParseException(string sessionId, string message) :
            base(message)
        {
            SessionId = sessionId;
        }

        public string SessionId { get; }
    }
}
