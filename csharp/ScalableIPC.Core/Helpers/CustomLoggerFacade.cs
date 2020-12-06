using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("ScalableIPC.Tests")]

namespace ScalableIPC.Core.Helpers
{
    public static class CustomLoggerFacade
    {
        public static ICustomLogger Logger { get; set; }
        public static bool IgnoreLogFailures { get; set; }

        public static void Log(Func<CustomLogEvent> logEventSupplier)
        {
            try
            {
                if (Logger == null || !Logger.Enabled)
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
    }
}
