using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;

namespace PortableIPC.Core
{
    public class EndpointConfig
    {
        private int _sessionIdSuffixCounter = 0;
        private readonly string _startTime = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        public IPEndPoint Endpoint { get; set; }
        public int IdleTimeoutSecs { get; set; }
        public int MinIdleTimeoutSecs { get; set; }
        public int MaxIdleTimeoutSecs { get; set; }
        public int AckTimeoutSecs { get; set; }
        public int MininumIdleTimeoutSecs { get; set; }
        public int MaximumIdleTimeoutSecs { get; set; }
        public int MaxOpenRequestPduCount { get; set; }
        public ISessionHandlerFactory SessionHandlerFactory { get; set; }

        public string GenerateSessionId()
        {
            var v = Interlocked.Increment(ref _sessionIdSuffixCounter);
            var sessionId = (v + _startTime).PadLeft(ProtocolDatagram.SessionIdLength, '0');
            return sessionId;
        }
        public string GenerateNullSessionId()
        {
            var sessionId = "".PadLeft(ProtocolDatagram.SessionIdLength, '0');
            return sessionId;
        }
    }
}
