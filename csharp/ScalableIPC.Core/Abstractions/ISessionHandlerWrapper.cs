using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface ISessionHandlerWrapper
    {
        ISessionHandler SessionHandler { get; set; }
    }
}
