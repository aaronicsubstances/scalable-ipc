using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Tests.Helpers
{
    public class OptionsComparer : IEqualityComparer<Dictionary<string, List<string>>>
    {
        public static readonly OptionsComparer Default = new OptionsComparer();

        public bool Equals(Dictionary<string, List<string>> x, Dictionary<string, List<string>> y)
        {
            if (x == y)
            {
                return true;
            }
            if (x == null || y == null)
            {
                return false;
            }
            var xKeys = x.Keys.ToList();
            var yKeys = y.Keys.ToList();
            if (xKeys.Count != yKeys.Count)
            {
                return false;
            }
            for (int i = 0; i < xKeys.Count; i++)
            {
                // ensure order of keys.
                if (xKeys[i] != yKeys[i])
                {
                    return false;
                }
                // check for equality of values.
                var listFromX = x[xKeys[i]];
                var listFromY = y[yKeys[i]];
                if (!listFromX.SequenceEqual(listFromY))
                {
                    return false;
                }
            }
            return true;
        }

        public int GetHashCode(Dictionary<string, List<string>> obj)
        {
            return 1;
        }
    }
}
