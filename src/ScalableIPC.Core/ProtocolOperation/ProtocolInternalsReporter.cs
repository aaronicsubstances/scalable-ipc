using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.ProtocolOperation
{
    internal interface ProtocolInternalsReporter
    {
        void OnEndpointReset(string endpointOwnerId);
        void OnKnownMessageDestinatonInfoAbandoned(GenericNetworkIdentifier remoteEndpoint);
        void OnReceiveDataAborted(IncomingTransfer transfer, int abortCode);
        void OnSendDataAborted(OutgoingTransfer transfer, int abortCode);
    }
}
