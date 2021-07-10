using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Helpers;
using ScalableIPC.Core.ProtocolOperation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("ScalableIPC.Core.UnitTests")]
[assembly: InternalsVisibleTo("ScalableIPC.IntegrationTests")]

namespace ScalableIPC.Core
{
    public class ScalableIpcProtocol : TransportApiCallbacks
    {
        public const int UnconfiguredMaximumMessageLength = 65_536;
        public const int MinimumNonTerminatingPduDataSize = 512;

        private readonly EndpointStructuredDatastore<IncomingTransfer> incomingTransfers;
        private readonly EndpointStructuredDatastore<OutgoingTransfer> outgoingTransfers;
        private readonly Dictionary<GenericNetworkIdentifier, EndpointOwnerIdInfo> knownMessageDestinationIds;
        private object lastEndpointOwnerResetTimeoutId;

        public ScalableIpcProtocol()
        {
            incomingTransfers = new EndpointStructuredDatastore<IncomingTransfer>();
            outgoingTransfers = new EndpointStructuredDatastore<OutgoingTransfer>();
            knownMessageDestinationIds = new Dictionary<GenericNetworkIdentifier, EndpointOwnerIdInfo>();
        }

        public string EndpointOwnerId { get; private set; }
        public int EndpointOwnerIdResetPeriod { get; set; }
        public int MaximumPduDataSize { get; set; }
        public int MaximumReceivableMessageLength { get; set; }
        public int MinRetryBackoffPeriod { get; set; }
        public int MaxRetryBackoffPeriod { get; set; }
        public int DefaultReceiveAckTimeout { get; set; }
        public int DataReceiveTimeout { get; set; }
        public int KnownMessageDestinationLifeTime { get; set; }
        public ScalableIpcProtocolListener EventListener { get; set; }
        public TransportApi UnderlyingTransport { get; set; }
        public EventLoopApi EventLoop { get; set; }
        
        internal ProtocolInternalsReporter InternalsReporter { get; set; }

        public void BeginSend(GenericNetworkIdentifier remoteEndpoint, ProtocolMessage msg,
            MessageSendOptions options, Action<ProtocolOperationException> cb)
        {
            // validate transfer
            if (msg.Offset < 0)
            {
                throw new Exception();
            }
            if (msg.Length < 0)
            {
                throw new Exception();
            }
            if (msg.Offset + msg.Length > msg.Data.Length)
            {
                throw new Exception();
            }
            msg.Id = ByteUtils.GenerateUuid();
            var transfer = new OutgoingTransfer
            {
                MessageId = msg.Id,
                RemoteEndpoint = remoteEndpoint,
                Data = msg.Data,
                StartOffset = msg.Offset,
                EndOffset = msg.Offset + msg.Length,
                SendCallback = cb,
                ReceiveAckTimeout = DefaultReceiveAckTimeout
            };
            if (options != null && options.Timeout > 0)
            {
                transfer.ReceiveAckTimeout = options.Timeout;
            }
            EventLoop.PostCallback(() =>
            {
                ProcessMessageSendRequest(transfer);
            });
        }

        private void ProcessMessageSendRequest(OutgoingTransfer transfer)
        {
            outgoingTransfers.Add(transfer.RemoteEndpoint, transfer.MessageId, transfer);
            transfer.MessageDestinationId = GetKnownMessageDestinationId(transfer.RemoteEndpoint) ??
                ByteUtils.GenerateUuid();

            // start ack timeout
            PostponeSendDataTimeout(transfer);

            transfer.PendingDataLengthToSend = Math.Min(transfer.EndOffset - transfer.StartOffset, MaximumPduDataSize);
            SendPendingPdu(transfer, true);
        }

        private void AbortSendTransfer(OutgoingTransfer transfer, int abortCode,
            ProtocolOperationException ex = null)
        {
            if (!outgoingTransfers.Remove(transfer.RemoteEndpoint, transfer.MessageId))
            {
                // ignore
                return;
            }
            transfer.SendCancellationHandle.Cancel();
            EventLoop.CancelTimeout(transfer.RetryBackoffTimeoutId);
            EventLoop.CancelTimeout(transfer.ReceiveAckTimeoutId);
            if (transfer.SendCallback != null)
            {
                if (abortCode == 0)
                {
                    transfer.SendCallback(null);
                }
                else
                {
                    transfer.SendCallback(ex ?? new ProtocolOperationException(abortCode));
                }
            }
            if (abortCode == ProtocolOperationException.ErrorCodeSendTimeout ||
                abortCode == ProtocolOperationException.ErrorCodeAbortedFromSender)
            {
                // send pending pdu with empty data to trigger early abort in receiver
                // before waiting for full timeout.
                transfer.PendingDataLengthToSend = 0;
                SendPendingPdu(transfer, false);
            }
            InternalsReporter?.OnSendDataAborted(transfer, abortCode);
        }

        private void SendPendingPdu(OutgoingTransfer transfer, bool scheduleRetry)
        {
            var pdu = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeHeader,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                MessageDestinationId = transfer.MessageDestinationId,
                MessageId = transfer.MessageId,
                MessageLength = transfer.EndOffset - transfer.StartOffset,
                Data = transfer.Data,
                DataOffset = transfer.StartOffset,
                DataLength = transfer.PendingDataLengthToSend,
                SequenceNumber = transfer.PendingSequenceNumber
            };
            if (transfer.PendingSequenceNumber > 0)
            {
                pdu.OpCode = ProtocolDatagram.OpCodeData;
            }
            Action<ProtocolOperationException> sendCb = null;
            if (scheduleRetry)
            {
                // disregard success or failure result. just interested in waiting.
                var cancellationHandle = new CancellationHandle();
                transfer.SendCancellationHandle = cancellationHandle;
                sendCb = _ =>
                {
                    EventLoop.PostCallback(() => ProcessSendPduOutcome(
                        transfer, cancellationHandle));
                };
            }
            UnderlyingTransport.BeginSend(transfer.RemoteEndpoint, pdu, sendCb);
        }

        private void ProcessSendPduOutcome(OutgoingTransfer transfer, CancellationHandle cancellationHandle)
        {
            if (cancellationHandle.Cancelled) return;

            int retryBackoffPeriod = MinRetryBackoffPeriod;
            if (MaxRetryBackoffPeriod > MinRetryBackoffPeriod)
            {
                retryBackoffPeriod += MathUtils.GetRandomInt(MaxRetryBackoffPeriod - MinRetryBackoffPeriod);
            }
            transfer.RetryBackoffTimeoutId = EventLoop.ScheduleTimeout(retryBackoffPeriod,
                () => SendPendingPdu(transfer, true));
        }

        private void ProcessReceiveAckRequest(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram ack)
        {
            OutgoingTransfer transfer = outgoingTransfers.Get(remoteEndpoint, ack.MessageId, null);
            if (transfer == null)
            {
                // discard.
                return;
            }
            if (ack.OpCode == ProtocolDatagram.OpCodeHeaderAck)
            {
                if (transfer.PendingSequenceNumber != 0)
                {
                    // discard.
                    return;
                }
            }
            if (ack.OpCode == ProtocolDatagram.OpCodeDataAck)
            {
                if (transfer.PendingSequenceNumber == 0 ||
                    ack.SequenceNumber != transfer.PendingSequenceNumber)
                {
                    // discard.
                    return;
                }
            }
            if (ack.ErrorCode > 0)
            {
                if (ack.ErrorCode == ProtocolOperationException.ErrorCodeInvalidDestinationEndpointId)
                {
                    // save for future use.
                    UpdateKnownMessageDestinationIds(remoteEndpoint, ack.MessageSourceId);

                    if (ack.OpCode == ProtocolDatagram.OpCodeHeaderAck)
                    {
                        if (transfer.MessageDestinationId != ack.MessageSourceId)
                        {
                            transfer.MessageDestinationId = ack.MessageSourceId;
                            SendPendingPdu(transfer, false);
                        }
                        return;
                    }
                }
                // by default abort transfer.
                AbortSendTransfer(transfer, ack.ErrorCode);
                return;
            }

            // successfully sent pending pdu.
            transfer.StartOffset += transfer.PendingDataLengthToSend;

            // check if we are done.
            if (transfer.StartOffset == transfer.EndOffset)
            {
                AbortSendTransfer(transfer, 0);
            }
            else
            {
                // not done.
                transfer.SendCancellationHandle.Cancel();
                EventLoop.CancelTimeout(transfer.RetryBackoffTimeoutId);

                // reset ack timeout
                PostponeSendDataTimeout(transfer);

                // prepare to send next pdu
                transfer.PendingDataLengthToSend = Math.Min(transfer.EndOffset - transfer.StartOffset,
                    MaximumPduDataSize);
                transfer.PendingSequenceNumber++;
                SendPendingPdu(transfer, true);
            }
        }

        private void PostponeSendDataTimeout(OutgoingTransfer transfer)
        {
            EventLoop.CancelTimeout(transfer.ReceiveAckTimeoutId);
            transfer.ReceiveAckTimeoutId = EventLoop.ScheduleTimeout(transfer.ReceiveAckTimeout,
                () => AbortSendTransfer(transfer, ProtocolOperationException.ErrorCodeSendTimeout));
        }

        private string GetKnownMessageDestinationId(GenericNetworkIdentifier remoteEndpoint)
        {
            if (knownMessageDestinationIds.ContainsKey(remoteEndpoint))
            {
                return knownMessageDestinationIds[remoteEndpoint].Id;
            }
            else
            {
                return null;
            }
        }

        private void UpdateKnownMessageDestinationIds(GenericNetworkIdentifier remoteEndpoint, string messageSourceId)
        {
            if (knownMessageDestinationIds.ContainsKey(remoteEndpoint))
            {
                knownMessageDestinationIds[remoteEndpoint].Id = messageSourceId;
            }
            else
            {
                var newEntry = new EndpointOwnerIdInfo
                {
                    Id = messageSourceId
                };
                knownMessageDestinationIds.Add(remoteEndpoint, newEntry);
                // use absolute expiration to limit size of dictionary.
                newEntry.TimeoutId = EventLoop.ScheduleTimeout(KnownMessageDestinationLifeTime, () =>
                {
                    knownMessageDestinationIds.Remove(remoteEndpoint);
                    InternalsReporter?.OnKnownMessageDestinatonInfoAbandoned(remoteEndpoint);
                });
            }
        }

        public void BeginReceive(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram pdu)
        {
            EventLoop.PostCallback(() =>
            {
                pdu.Validate();
                ProcessPduReceiveRequest(remoteEndpoint, pdu);
            });
        }

        private void ProcessPduReceiveRequest(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram pdu)
        {
            if (EndpointOwnerId == null)
            {
                ResetEndpointOwnerId();
            }
            if (pdu.OpCode == ProtocolDatagram.OpCodeHeader ||
                pdu.OpCode == ProtocolDatagram.OpCodeData)
            {
                ProcessReceiveDataRequest(remoteEndpoint, pdu);
            }
            if (pdu.OpCode == ProtocolDatagram.OpCodeHeaderAck ||
                pdu.OpCode == ProtocolDatagram.OpCodeDataAck)
            {
                ProcessReceiveAckRequest(remoteEndpoint, pdu);
            }
        }

        private void ProcessReceiveDataRequest(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram pdu)
        {
            IncomingTransfer transfer = incomingTransfers.Get(remoteEndpoint, pdu.MessageId, null);
            if (transfer?.Processed == true)
            {
                HandleAlreadyProcessedMessageId(transfer, pdu);
                return;
            }

            if (pdu.OpCode == ProtocolDatagram.OpCodeData)
            {
                if (transfer == null || !IsDataPduValidMessagePart(transfer, pdu))
                {
                    return;
                }
            }
            else
            {
                if (transfer == null)
                {
                    transfer = new IncomingTransfer
                    {
                        RemoteEndpoint = remoteEndpoint,
                        MessageId = pdu.MessageId,
                        MessageSrcId = EndpointOwnerId,
                        ReceiveBuffer = new MemoryStream()
                    };
                }
                if (!IsHeaderPduValidMessagePart(transfer, pdu))
                {
                    return;
                }
                incomingTransfers.Add(remoteEndpoint, pdu.MessageId, transfer);
            }

            // all is well
            transfer.BytesRemaining = pdu.MessageLength;

            int dataLengthToUse = Math.Min(pdu.DataLength, transfer.BytesRemaining);
            transfer.ReceiveBuffer.Write(pdu.Data, pdu.DataOffset, dataLengthToUse);
            transfer.BytesRemaining -= dataLengthToUse;
            if (transfer.BytesRemaining > 0 && pdu.DataLength < MinimumNonTerminatingPduDataSize)
            {
                // mark as processed and failed.
                AbortReceiveTransfer(transfer, ProtocolOperationException.ErrorCodeAbortedFromSender);
                return;
            }

            if (transfer.BytesRemaining == 0)
            {
                // mark as processed and successful.
                AbortReceiveTransfer(transfer, 0);
            }
            else
            {
                transfer.ExpectedSequenceNumber++;
                // reset timeout
                PostponeReceiveDataTimeout(transfer);
            }
            transfer.LastAckSent = new ProtocolDatagram
            {
                OpCode = pdu.OpCode == ProtocolDatagram.OpCodeHeader ?
                    ProtocolDatagram.OpCodeHeaderAck : ProtocolDatagram.OpCodeDataAck,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                MessageId = transfer.MessageId,
                MessageSourceId = transfer.MessageSrcId
            };
            // ignore outcome of send.
            UnderlyingTransport.BeginSend(remoteEndpoint, transfer.LastAckSent, null);
        }

        private void HandleAlreadyProcessedMessageId(IncomingTransfer transfer, ProtocolDatagram pdu)
        {
            bool replyWithLastAck = transfer.MessageSrcId == pdu.MessageDestinationId;
            if (pdu.OpCode == ProtocolDatagram.OpCodeHeader)
            {
                replyWithLastAck = replyWithLastAck && transfer.ExpectedSequenceNumber == 0;
            }
            else
            {
                replyWithLastAck = replyWithLastAck && transfer.ExpectedSequenceNumber == pdu.SequenceNumber;
            }
            if (replyWithLastAck)
            {
                // send back again the last ack sent out.
                if (transfer.LastAckSent == null)
                {
                    transfer.LastAckSent = new ProtocolDatagram
                    {
                        OpCode = transfer.ExpectedSequenceNumber == 0 ?
                            ProtocolDatagram.OpCodeHeaderAck : ProtocolDatagram.OpCodeDataAck,
                        Version = ProtocolDatagram.ProtocolVersion1_0,
                        MessageId = transfer.MessageId,
                        MessageSourceId = transfer.MessageSrcId,
                        ErrorCode = transfer.ProcessingErrorCode
                    };
                }
                // ignore outcome of send.
                UnderlyingTransport.BeginSend(transfer.RemoteEndpoint, transfer.LastAckSent, null);
            }
            else
            {
                // discard
            }
        }

        private bool IsHeaderPduValidMessagePart(IncomingTransfer transfer, ProtocolDatagram pdu)
        {
            if (transfer.MessageSrcId != pdu.MessageDestinationId)
            {
                var ack = new ProtocolDatagram
                {
                    OpCode = ProtocolDatagram.OpCodeHeaderAck,
                    Version = ProtocolDatagram.ProtocolVersion1_0,
                    MessageId = transfer.MessageId,
                    MessageSourceId = transfer.MessageSrcId,
                    ErrorCode = ProtocolOperationException.ErrorCodeInvalidDestinationEndpointId
                };
                // ignore outcome of send.
                UnderlyingTransport.BeginSend(transfer.RemoteEndpoint, ack, null);
                return false;
            }
            if (transfer.ExpectedSequenceNumber != 0)
            {
                if (transfer.ExpectedSequenceNumber == 1)
                {
                    // send back again the last ack sent out.
                    // ignore outcome of send.
                    UnderlyingTransport.BeginSend(transfer.RemoteEndpoint, transfer.LastAckSent, null);
                }
                else
                {
                    // discard
                }
                return false;
            }
            if (pdu.MessageLength > UnconfiguredMaximumMessageLength &&
                pdu.MessageLength > MaximumReceivableMessageLength)
            {
                var ack = new ProtocolDatagram
                {
                    OpCode = ProtocolDatagram.OpCodeHeaderAck,
                    Version = ProtocolDatagram.ProtocolVersion1_0,
                    MessageId = transfer.MessageId,
                    MessageSourceId = transfer.MessageSrcId,
                    ErrorCode = ProtocolOperationException.ErrorCodeMessageTooLarge
                };
                // ignore outcome of send.
                UnderlyingTransport.BeginSend(transfer.RemoteEndpoint, ack, null);
                return false;
            }
            if (pdu.DataLength < MinimumNonTerminatingPduDataSize &&
                pdu.MessageLength != pdu.DataLength)
            {
                // discard
                return false;
            }
            return true;
        }

        private bool IsDataPduValidMessagePart(IncomingTransfer transfer, ProtocolDatagram pdu)
        {
            if (transfer.MessageSrcId != pdu.MessageDestinationId)
            {
                var ack = new ProtocolDatagram
                {
                    OpCode = ProtocolDatagram.OpCodeDataAck,
                    Version = ProtocolDatagram.ProtocolVersion1_0,
                    MessageId = transfer.MessageId,
                    MessageSourceId = transfer.MessageSrcId,
                    SequenceNumber = pdu.SequenceNumber,
                    ErrorCode = ProtocolOperationException.ErrorCodeInvalidDestinationEndpointId
                };
                // ignore outcome of send.
                UnderlyingTransport.BeginSend(transfer.RemoteEndpoint, ack, null);
                return false;
            }
            if (transfer.ExpectedSequenceNumber != pdu.SequenceNumber)
            {
                if (transfer.ExpectedSequenceNumber == pdu.SequenceNumber + 1)
                {
                    // send back again the last ack sent out.
                    // ignore outcome of send.
                    UnderlyingTransport.BeginSend(transfer.RemoteEndpoint, transfer.LastAckSent, null);
                }
                else
                {
                    // discard
                }
                return false;
            }
            return true;
        }

        private void PostponeReceiveDataTimeout(IncomingTransfer transfer)
        {
            EventLoop.CancelTimeout(transfer.ReceiveDataTimeoutId);
            transfer.ReceiveDataTimeoutId = EventLoop.ScheduleTimeout(DataReceiveTimeout,
                () => AbortReceiveTransfer(transfer, ProtocolOperationException.ErrorCodeReceiveTimeout));
        }

        private void AbortReceiveTransfer(IncomingTransfer transfer, int abortCode)
        {
            if (transfer.Processed)
            {
                // ignore.
                return;
            }
            // mark as processed.
            transfer.Processed = true;
            if (abortCode < 0 || abortCode > short.MaxValue)
            {
                transfer.ProcessingErrorCode = ProtocolOperationException.ErrorCodeProcessingError;
            }
            else {
                transfer.ProcessingErrorCode = (short)abortCode;
            }
            EventLoop.CancelTimeout(transfer.ReceiveDataTimeoutId);
            if (abortCode == 0 && EventListener != null)
            {
                // success. inform event listener.
                var messageBytes = transfer.ReceiveBuffer.ToArray();
                var message = new ProtocolMessage
                {
                    Id = transfer.MessageId,
                    Data = messageBytes,
                    Length = messageBytes.Length
                };
                EventListener.OnMessageReceived(transfer.RemoteEndpoint, message);
            }
            transfer.ReceiveBuffer.Dispose();
            InternalsReporter?.OnReceiveDataAborted(transfer, abortCode);
        }

        public void Reset(ProtocolOperationException causeOfReset)
        {
            EventLoop.PostCallback(() =>
            {
                // cancel all outgoing transfers.
                var endpoints = outgoingTransfers.GetEndpoints();
                foreach (var endpoint in endpoints)
                {
                    foreach (var transfer in outgoingTransfers.GetValues(endpoint))
                    {
                        AbortSendTransfer(transfer, ProtocolOperationException.ErrorCodeAbortedFromSender,
                            causeOfReset);
                    }
                }

                // cancel all receives
                endpoints = incomingTransfers.GetEndpoints();
                foreach (var endpoint in endpoints)
                {
                    foreach (var transfer in incomingTransfers.GetValues(endpoint))
                    {
                        AbortReceiveTransfer(transfer, ProtocolOperationException.ErrorCodeAbortedFromReceiver);
                    }
                }

                // clear endpoint ownership information
                foreach (var entry in knownMessageDestinationIds.Values)
                {
                    EventLoop.CancelTimeout(entry.TimeoutId);
                }
                knownMessageDestinationIds.Clear();

                // reset for message source ids
                ResetEndpointOwnerId();
            });
        }

        private void ResetEndpointOwnerId()
        {
            EventLoop.CancelTimeout(lastEndpointOwnerResetTimeoutId);
            lastEndpointOwnerResetTimeoutId = EventLoop.ScheduleTimeout(EndpointOwnerIdResetPeriod,
                () => ResetEndpointOwnerId());
            EndpointOwnerId = ByteUtils.GenerateUuid();

            // eject all processed receives
            var endpoints = incomingTransfers.GetEndpoints();
            foreach (var endpoint in endpoints)
            {
                foreach (var transfer in incomingTransfers.GetValues(endpoint))
                {
                    if (transfer.Processed)
                    {
                        incomingTransfers.Remove(endpoint, transfer.MessageId);
                    }
                }
            }
            InternalsReporter?.OnEndpointReset(EndpointOwnerId);
        }
    }
}
