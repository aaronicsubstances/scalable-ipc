using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class MessageReceivedEventArgs: EventArgs
    {
        public ProtocolMessage Message { get; set; }
    }
}
