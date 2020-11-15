using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core
{
    public class SessionHandlerWrapper
    {
        public SessionHandlerWrapper(ISessionHandler sessionHandler)
        {
            SessionHandler = sessionHandler;
        }

        public ISessionHandler SessionHandler { get; }
    }
}
