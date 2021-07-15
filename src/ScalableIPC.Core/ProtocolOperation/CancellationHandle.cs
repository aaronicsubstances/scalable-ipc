using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.ProtocolOperation
{
    public class CancellationHandle
    {
        private readonly Action cancelCb;

        public CancellationHandle(Action cancelCb)
        {
            this.cancelCb = cancelCb;
        }

        public void Cancel()
        {
            Cancelled = true;
            cancelCb?.Invoke();
        }

        public bool Cancelled { get; private set; }
    }
}
