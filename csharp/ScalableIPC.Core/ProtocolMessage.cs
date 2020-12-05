using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core
{
    public class ProtocolMessage
    {
        public string SessionId { get; set; }

        public Dictionary<string, List<string>> Attributes { get; set; }

        public byte[] DataBytes { get; set; }
        public int DataOffset { get; set; }
        public int DataLength { get; set; }
    }
}
