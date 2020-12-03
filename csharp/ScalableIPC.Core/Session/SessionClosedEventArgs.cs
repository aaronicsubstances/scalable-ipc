using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SessionClosedEventArgs: EventArgs
    {
        public SessionCloseException Cause { get; set; }
    }
}
