using ScalableIPC.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.UnitTests.Helpers
{
    public class ProtocolDatagramComparer : IEqualityComparer<ProtocolDatagram>
    {
        public static readonly ProtocolDatagramComparer Default = new ProtocolDatagramComparer();

        public bool Equals(ProtocolDatagram x, ProtocolDatagram y)
        {
            if (x == y)
            {
                return true;
            }
            if (x == null || y == null)
            {
                return false;
            }
            if (x.ExpectedDatagramLength != y.ExpectedDatagramLength)
            {
                return false;
            }
            if (x.SessionId != y.SessionId)
            {
                return false;
            }
            if (x.WindowId != y.WindowId)
            {
                return false;
            }
            if (x.SequenceNumber != y.SequenceNumber)
            {
                return false;
            }
            if (x.OpCode != y.OpCode)
            {
                return false;
            }
            if (!ProtocolDatagramOptionsComparer.Default.Equals(x.Options, y.Options))
            {
                return false;
            }
            if (x.DataOffset != y.DataOffset)
            {
                return false;
            }
            if (x.DataLength != y.DataLength)
            {
                return false;
            }
            if (x.DataBytes != y.DataBytes)
            {
                if (x.DataBytes == null || y.DataBytes == null)
                {
                    return false;
                }
                if (!x.DataBytes.SequenceEqual(y.DataBytes))
                {
                    return false;
                }
            }
            return true;
        }

        public int GetHashCode(ProtocolDatagram obj)
        {
            return 1;
        }
    }
}
