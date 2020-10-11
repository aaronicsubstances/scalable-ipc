using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Abstractions
{
    public enum SessionState
    {
        NotStarted = 0,
        Opening = 1,
        OpenedForData = 2,
        Closing = 3,
        Closed = 4
    }
}
