using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Helpers
{
    public interface ICustomLogger
    {
        bool LogEnabled { get; }
        void Log(CustomLogEvent logEvent);
        bool TestLogEnabled { get; }
        void TestLog(CustomLogEvent logEvent);
        void WriteToStdOut(string message, Exception ex);
    }
}
