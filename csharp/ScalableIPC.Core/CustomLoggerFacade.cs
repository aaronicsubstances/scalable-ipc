using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("ScalableIPC.Tests")]

namespace ScalableIPC.Core
{
    public static class CustomLoggerFacade
    {
        public static ICustomLogger Logger { get; set; }

        public static void Log(Func<CustomLogEvent> logEventSupplier)
        {
            if (Logger == null || !Logger.Enabled)
            {
                return;
            }
            var logEvent = logEventSupplier.Invoke();
            Logger.Log(logEvent);
        }
    }
}
