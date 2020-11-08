using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Tests.ConcreteComponents
{
    class ExtraEndpointConfig
    {
        public int MinTransmissionDelayMs { get; set; }
        public int MaxTransmissionDelayMs { get; set; }
    }
}
