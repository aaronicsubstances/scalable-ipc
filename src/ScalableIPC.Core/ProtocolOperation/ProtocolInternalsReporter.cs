using ScalableIPC.Core.ErrorHandling;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.ProtocolOperation
{
    internal interface ProtocolInternalsReporter
    {
        void OnEndpointOwnerIdReset(string endpointOwnerId);
        void OnKnownMessageDestinatonInfoAbandoned(GenericNetworkIdentifier remoteEndpoint);
        void OnReceiveDataAborted(IncomingTransfer transfer, ProtocolErrorCode abortCode);
        void OnSendDataAborted(OutgoingTransfer transfer, ProtocolErrorCode abortCode);
    }
}
