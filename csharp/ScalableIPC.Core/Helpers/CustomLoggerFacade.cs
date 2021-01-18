using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("ScalableIPC.UnitTests")]
[assembly: InternalsVisibleTo("ScalableIPC.IntegrationTests")]

namespace ScalableIPC.Core.Helpers
{
    public static class CustomLoggerFacade
    {
        public static ICustomLogger Logger { get; set; }
        public static bool IgnoreLogFailures { get; set; }
        public static bool IgnoreTestLogFailures { get; set; }

        public static void Log(Func<CustomLogEvent> logEventSupplier)
        {
            try
            {
                if (Logger == null || !Logger.LogEnabled)
                {
                    return;
                }
                var logEvent = logEventSupplier.Invoke();
                Logger.Log(logEvent);
            }
            catch (Exception ex)
            {
                if (!IgnoreLogFailures)
                {
                    throw ex;
                }
            }
        }

        public static void TestLog(Func<CustomLogEvent> logEventSupplier)
        {
            try
            {
                if (Logger == null || !Logger.TestLogEnabled)
                {
                    return;
                }
                var logEvent = logEventSupplier.Invoke();
                Logger.TestLog(logEvent);
            }
            catch (Exception ex)
            {
                if (!IgnoreTestLogFailures)
                {
                    throw ex;
                }
            }
        }

        public static void WriteToStdOut(bool important, string message, Exception ex)
        {
            Logger?.WriteToStdOut(important, message, ex);
        }
    }
}
