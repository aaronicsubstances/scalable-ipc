using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class MessageReceivedEventArgs: EventArgs
    {
        public ProtocolDatagram Message { get; set; }
    }
}
