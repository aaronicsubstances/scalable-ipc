using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public enum SessionState
    {
        Opening = 0,
        OpenedForData = 1,
        Closed = 2
    }
}
