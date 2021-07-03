using ScalableIPC.Core.Abstractions;
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
        }

        public GenericNetworkIdentifier LocalEndpoint { get; set; }
        public TransportProcessorApi EndpointDataProcessor { get; set; }
        public Dictionary<GenericNetworkIdentifier, Connection> Connections { get; }
        public EventLoopApi EventLoop { get; set; }

        public void BeginSend(GenericNetworkIdentifier remoteEndpoint,
            byte[] data, int offset, int length, Action<ProtocolOperationException> cb)
        {
            try
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
                        data, offset, length);
                }
            }
            catch (Exception ex)
            {
                if (cb == null)
                {
                    throw ex;
                }
                else
                {
                    cb.Invoke(new ProtocolOperationException(ex));
                }
            }
        }

        public void SimulateReceive(int delay, GenericNetworkIdentifier remoteEndpoint, byte[] data, int offset, int length)
        {
            EventLoop.ScheduleTimeout(delay, () => EndpointDataProcessor.BeginReceive(
                remoteEndpoint, data, offset, length, null));
        }
    }
}
