using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SessionDisposedEventArgs: EventArgs
    {
        public SessionDisposedException Cause { get; set; }
    }
}
