using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScalableIPC.Core.Transports
{
    public class IntraProcessTransport : TransportApi
    {
        public class Connection
        {
            public IntraProcessTransport ConnectedTransport { get; set; }
            public Func<SendConfig> SendBehaviour { get; set; }
        }

        public class SendConfig
        {
            public int SendDelay { get; set; }
            public ProtocolOperationException SendError { get; set; }
            public int[] DuplicateTransmissionDelays { get; set; }
        }

        public IntraProcessTransport()
        {
            Connections = new Dictionary<GenericNetworkIdentifier, Connection>();
            EventLoop = new UnsynchronizedEventLoopApi();
        }

        public GenericNetworkIdentifier LocalEndpoint { get; set; }
        public TransportApiCallbacks Callbacks { get; set; }
        public Dictionary<GenericNetworkIdentifier, Connection> Connections { get; }
        internal EventLoopApi EventLoop { get; set; }

        public void BeginSend(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram pdu, Action<ProtocolOperationException> cb)
        {
            // ensure connected transport for target endpoint.
            var remoteConnection = Connections[remoteEndpoint];
            var sendConfig = remoteConnection.SendBehaviour();

            // simulate sending out datagram
            if (cb != null)
            {
                var sendDelay = sendConfig?.SendDelay ?? 0;
                var sendError = sendConfig?.SendError;
                EventLoop.ScheduleTimeout(sendDelay, () => cb.Invoke(sendError));
            }

            // simulate transmission behaviour of delays and duplication of
            // datagrams by physical networks
            var transmissionDelays = sendConfig?.DuplicateTransmissionDelays ?? new int[] { 0 };
            foreach (int transmissionDelay in transmissionDelays)
            {
                remoteConnection.ConnectedTransport.SimulateReceive(transmissionDelay, LocalEndpoint,
                    pdu);
            }
        }

        public void SimulateReceive(int delay, GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram pdu)
        {
            try
            {
                EventLoop.ScheduleTimeout(delay, () => Callbacks.BeginReceive(
                    remoteEndpoint, pdu));
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }
}
