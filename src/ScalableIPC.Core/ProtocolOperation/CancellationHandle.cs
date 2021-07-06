using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.ProtocolOperation
{
    public class CancellationHandle
    {
        public void Cancel()
        {
            Cancelled = true;
        }

        public bool Cancelled { get; private set; }
    }
}
