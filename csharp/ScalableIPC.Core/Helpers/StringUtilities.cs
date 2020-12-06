using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Helpers
{
    public static class StringUtilities
    {
        public static string StringifyOptions(Dictionary<string, List<string>> options)
        {
            if (options == null)
            {
                return "";
            }
            var sb = new StringBuilder();
            sb.Append("{");
            bool loopEntered = false;
            foreach (var kvp in options)
            {
                if (loopEntered)
                {
                    sb.Append(", ");
                }
                sb.Append(kvp.Key);
                sb.Append("=[");
                sb.Append(string.Join(", ", kvp.Value));
                sb.Append("]");
                loopEntered = true;
            }
            sb.Append("}");
            return sb.ToString();
        }

        public static string StringifyByteArray(byte[] data, int offset, int length)
        {
            if (data == null)
            {
                return "";
            }
            var sb = new StringBuilder();
            sb.Append("[");
            bool loopEntered = false;
            for (int i = 0; i < length; i++)
            {
                if (loopEntered)
                {
                    sb.Append(", ");
                }
                sb.AppendFormat("0x{0:X2}", data[offset + i]);
                loopEntered = true;
            }
            sb.Append("]");
            return sb.ToString();
        }
    }
}
