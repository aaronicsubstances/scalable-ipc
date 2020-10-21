using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Abstractions
{
    public enum SessionState
    {
        Opening = 0,
        OpenedForData = 1,
        Closed = 2
    }
}
