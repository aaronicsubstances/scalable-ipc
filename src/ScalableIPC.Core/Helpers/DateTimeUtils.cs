using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Helpers
{
    public static class DateTimeUtils
    {
        public static long UnixTimeMillis
        {
            get
            {
                DateTime foo = DateTime.UtcNow;
                long unixTime = ((DateTimeOffset)foo).ToUnixTimeMilliseconds();
                return unixTime;
            }
        }
    }
}
