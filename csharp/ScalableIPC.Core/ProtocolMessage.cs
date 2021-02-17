using ScalableIPC.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core
{
    public class ProtocolMessage
    {
        public Dictionary<string, List<string>> Attributes { get; set; }

        public byte[] DataBytes { get; set; }
        public int DataOffset { get; set; }
        public int DataLength { get; set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(nameof(ProtocolMessage)).Append("{");
            AppendToStringCommon(sb);
            sb.Append("}");
            return sb.ToString();
        }

        protected void AppendToStringCommon(StringBuilder sb)
        {
            sb.Append(nameof(Attributes)).Append("=").Append(StringUtilities.StringifyOptions(Attributes));
            sb.Append(", ");
            sb.Append(nameof(DataOffset)).Append("=").Append(DataOffset);
            sb.Append(", ");
            sb.Append(nameof(DataLength)).Append("=").Append(DataLength);
            sb.Append(", ");
            sb.Append(nameof(DataBytes)).Append("=").Append(StringUtilities.StringifyByteArray(DataBytes));
        }
    }

    public class ReceivedProtocolMessage: ProtocolMessage
    {
        public string SessionId { get; set; }
        public List<long> WindowIds { get; set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(nameof(ReceivedProtocolMessage)).Append("{");
            sb.Append(nameof(SessionId)).Append("=").Append(SessionId);
            sb.Append(", ");
            sb.Append(nameof(WindowIds)).Append("=[").Append(string.Join(",", WindowIds));
            sb.Append("], ");
            AppendToStringCommon(sb);
            sb.Append("}");
            return sb.ToString();
        }
    }
}
