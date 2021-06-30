using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Helpers
{
    public static class MathUtils
    {
        private static readonly Random RandNumGen = new Random();

        public static int GetRandomInt(int max)
        {
            return RandNumGen.Next(max);
        }
    }
}
