using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class SessionDisposingEventArgs: EventArgs
    {
        public SessionDisposedException Cause { get; set; }
    }
}
