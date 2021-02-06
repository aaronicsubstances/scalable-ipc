using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public static class SessionState
    {
        public static readonly int Opening = 1;
        public static readonly int Opened = 3;
        public static readonly int Closing = 5;
        public static readonly int DisposeAwaiting = 7;
        public static readonly int Disposed = 9;
    }
}
