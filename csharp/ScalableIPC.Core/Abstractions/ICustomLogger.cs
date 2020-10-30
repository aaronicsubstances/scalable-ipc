using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public interface ICustomLogger
    {
        bool Enabled { get; }
        void Log(CustomLogEvent logEvent);
    }
}
