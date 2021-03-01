using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public static class SessionState
    {
        public static readonly int Opening = 1;
        public static readonly int Opened = 2;
        public static readonly int Closing = 3;
        public static readonly int Closed = 4;
        public static readonly int Disposing = 5;
        public static readonly int Disposed = 6;
    }
}
