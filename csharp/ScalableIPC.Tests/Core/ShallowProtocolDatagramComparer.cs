using ScalableIPC.Core;
using System.Collections.Generic;

namespace ScalableIPC.Tests.Core
{
    class ShallowProtocolDatagramComparer : IEqualityComparer<ProtocolDatagram>
    {
        public bool Equals(ProtocolDatagram x, ProtocolDatagram y)
        {
            if (x == null && y == null)
            {
                return true;
            }
            if (!(x != null && y != null))
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
            if (x.Options?.IdleTimeoutSecs != y.Options?.IdleTimeoutSecs)
            {
                return false;
            }
            if (x.Options?.IsLastInWindow != y.Options?.IsLastInWindow)
            {
                return false;
            }
            if (x.Options?.IsWindowFull != y.Options?.IsWindowFull)
            {
                return false;
            }
            if (x.Options?.AbortCode != y.Options?.AbortCode)
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
            // From here onwards intention is to check that two options or dataBytes
            // are equal, if both equal to null 
            // options and dataBytes are expected to be null for this comparer.
            if (x.Options?.AllOptions != y.Options?.AllOptions)
            {
                return false;
            }
            if (x.DataBytes != y.DataBytes)
            {
                return false;
            }
            return true;
        }

        public int GetHashCode(ProtocolDatagram obj)
        {
            return 1;
        }
    }
}