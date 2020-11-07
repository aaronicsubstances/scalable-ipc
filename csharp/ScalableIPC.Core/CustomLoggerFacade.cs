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
        private static readonly Dictionary<string, Func<object[], CustomLogEvent>> _logEventFactories;

        static CustomLoggerFacade()
        {
            _logEventFactories = new Dictionary<string, Func<object[], CustomLogEvent>>();
        }

        public static ICustomLogger Logger { get; set; }

        public static void LogThrough(Func<CustomLogEvent> logEventSupplier)
        {
            if (Logger == null || !Logger.Enabled)
            {
                return;
            }
            var logEvent = logEventSupplier.Invoke();
            Logger.Log(logEvent);
        }

        public static void LogMessage(string id, string message)
        {
            if (Logger == null || !Logger.Enabled)
            {
                return;
            }
            var logEvent = new CustomLogEvent
            {
                Id = id,
                Message = message
            };
            Logger.Log(logEvent);
        }

        public static void Log(string id, params object[] args)
        {
            if (Logger == null || !Logger.Enabled)
            {
                return;
            }
            CustomLogEvent logEvent;
            if (_logEventFactories.ContainsKey(id))
            {
                logEvent = _logEventFactories[id](args);
            }
            else
            {
                logEvent = ApplyDefaultProcessing(id, args);
            }
            Logger.Log(logEvent);
        }

        private static CustomLogEvent ApplyDefaultProcessing(string id, object[] args)
        {
            int dataStartIdx = 0;
            string message = null;
            if (args.Length % 2 == 1)
            {
                message = (string) args[0];
                dataStartIdx++;
            }
            if (message == null)
            {
                message = "";
            }
            IDictionary<string, object> data = null;
            for (int i = dataStartIdx; i < args.Length; i+=2)
            {
                var key = (string)args[i];
                var value = args[i + 1];
                if (data == null)
                {
                    data = new Dictionary<string, object>();
                }
                // pick last of duplicate keys.
                if (!data.ContainsKey(key))
                {
                    data.Add(key, value);
                }
                else
                {
                    data[key] = value;
                }
            }
            var logEvent = new CustomLogEvent
            {
                Id = id,
                Message = message,
                Data = data
            };
            return logEvent;
        }
    }
}
