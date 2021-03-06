﻿using ScalableIPC.Core.ErrorHandling;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.ProtocolOperation
{
    internal interface ProtocolMonitor
    {
        void OnEndpointOwnerIdReset(string endpointOwnerId);
        void OnReceiveDataAdded(IncomingTransfer transfer);
        void OnReceiveDataAborted(IncomingTransfer transfer, ProtocolErrorCode abortCode);
        void OnReceivedDataEvicted(IncomingTransfer transfer);
        void OnSendDataAdded(OutgoingTransfer transfer);
        void OnSendDataAborted(OutgoingTransfer transfer, ProtocolErrorCode abortCode);
        void OnReset(ProtocolException causeOfReset);
    }
}
