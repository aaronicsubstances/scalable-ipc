using Dapper.Contrib.Extensions;
using ScalableIPC.Core.Helpers;
using System;
using System.Collections.Generic;

namespace ScalableIPC.IntegrationTests.Helpers
{
    class TestLogRecord
    {
        public int Id { get; set; }
        public DateTime LoggedAt { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public string Logger { get; set; }
        public string Properties { get; set; }
        public string Callsite { get; set; }
        public string Exception { get; set; }

        [Computed]
        public Dictionary<string, object> ParsedProperties { get; set; }


        public string GetStrProp(string propNme)
        {
            if (ParsedProperties.ContainsKey(propNme))
            {
                return (string)ParsedProperties[propNme];
            }
            return null;
        }

        public long? GetIntProp(string propName)
        {
            if (ParsedProperties.ContainsKey(propName))
            {
                return (long)ParsedProperties[propName];
            }
            return null;
        }

        public string GetLogPositionId()
        {
            return GetStrProp(CustomLogEvent.LogDataKeyLogPositionId);
        }

        public string GetCurrentLogicalThreadId()
        {
            return GetStrProp(CustomLogEvent.LogDataKeyCurrentLogicalThreadId);
        }
    }
}