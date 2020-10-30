using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Abstractions
{
    public class CustomLogEvent
    {
        public string Id { get; set; }
        public string Message { get; set; }
        public IDictionary<string, object> Data { get; set; }
    }
}
