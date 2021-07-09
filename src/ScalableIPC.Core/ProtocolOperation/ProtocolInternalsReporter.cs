using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.ProtocolOperation
{
    internal interface ProtocolInternalsReporter
    {

        void OnReceiveDataTimeoutPostponed(IncomingTransfer transfer);
        void OnReceiveDataAborted(IncomingTransfer transfer, short errorCode);
        void OnReceiveDataAbandoned(IncomingTransfer transfer);
    }
}
