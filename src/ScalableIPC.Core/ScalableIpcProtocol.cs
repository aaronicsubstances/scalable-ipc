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
    public class ScalableIpcProtocol : IScalableIpcProtocol
    {
        public const int UnconfiguredMaximumMessageLength = 65_536;
        public const int MinimumNonTerminatingPduDataSize = 512;

        private readonly EndpointStructuredDatastore<IncomingTransfer> incomingTransfers;
        private readonly EndpointStructuredDatastore<OutgoingTransfer> outgoingTransfers;
        private readonly Dictionary<GenericNetworkIdentifier, EndpointOwnerIdInfo> knownMessageDestinationIds;

        public ScalableIpcProtocol()
        {
            EndpointOwnerId = ByteUtils.GenerateUuid();
            incomingTransfers = new EndpointStructuredDatastore<IncomingTransfer>();
            outgoingTransfers = new EndpointStructuredDatastore<OutgoingTransfer>();
            knownMessageDestinationIds = new Dictionary<GenericNetworkIdentifier, EndpointOwnerIdInfo>();
        }

        public string EndpointOwnerId { get; }
        public int MaximumPduDataSize { get; set; }
        public int MaximumReceivableMessageLength { get; set; }
        public int MinRetryBackoffPeriod { get; set; }
        public int MaxRetryBackoffPeriod { get; set; }
        public int DefaultAckTimeout { get; set; }
        public int DataReceiveTimeout { get; set; }
        public int ProcessedMessageDisposalWaitTime { get; set; }
        public int KnownMessageDestinationLifeTime { get; set; }
        public bool VaryMessageSourceIds { get; set; }
        public ScalableIpcProtocolListener EventListener { get; set; }
        public TransportApi UnderlyingTransport { get; set; }
        public EventLoopApi EventLoop { get; set; }

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
                AckTimeout = DefaultAckTimeout
            };
            if (options != null && options.AckTimeout > 0)
            {
                transfer.AckTimeout = options.AckTimeout;
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
            transfer.ReceiveAckTimeoutId = EventLoop.ScheduleTimeout(transfer.AckTimeout,
                () => AbortSendTransfer(transfer, ProtocolOperationException.ErrorCodeSendTimeout));

            transfer.DataLengthToSend = Math.Min(transfer.EndOffset - transfer.StartOffset, MaximumPduDataSize);
            SendPendingPdu(transfer, true);
        }

        private void AbortSendTransfer(OutgoingTransfer transfer, int abortCode)
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
                    transfer.SendCallback(new ProtocolOperationException(abortCode));
                }
            }
            if (abortCode == ProtocolOperationException.ErrorCodeSendTimeout)
            {
                // send pending pdu with empty data to trigger early abort in receiver
                // before waiting for full timeout.
                transfer.DataLengthToSend = 0;
                SendPendingPdu(transfer, false);
            }
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
                DataLength = transfer.DataLengthToSend,
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
            if (!cancellationHandle.Cancelled) return;

            int retryBackoffPeriod = MinRetryBackoffPeriod;
            if (MaxRetryBackoffPeriod > MinRetryBackoffPeriod)
            {
                retryBackoffPeriod += MathUtils.GetRandomInt(MaxRetryBackoffPeriod - MinRetryBackoffPeriod);
            }
            transfer.RetryBackoffTimeoutId = EventLoop.ScheduleTimeout(retryBackoffPeriod,
                () => SendPendingPdu(transfer, true));
        }

        private void ProcessAckPduReceiveRequest(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram ack)
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
                    }
                    else
                    {
                        // abort transfer.
                        AbortSendTransfer(transfer, ack.ErrorCode);
                    }
                }
                else
                {
                    // abort transfer.
                    AbortSendTransfer(transfer, ack.ErrorCode);
                }
            }
            else
            {
                // successfully sent pending pdu.
                transfer.StartOffset += transfer.DataLengthToSend;

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
                    EventLoop.CancelTimeout(transfer.ReceiveAckTimeoutId);
                    transfer.ReceiveAckTimeoutId = EventLoop.ScheduleTimeout(transfer.AckTimeout,
                        () => AbortSendTransfer(transfer, ProtocolOperationException.ErrorCodeSendTimeout));

                    // prepare to send next pdu
                    transfer.DataLengthToSend = Math.Min(transfer.EndOffset - transfer.StartOffset,
                        MaximumPduDataSize);
                    transfer.PendingSequenceNumber++;
                    SendPendingPdu(transfer, true);
                }
            }
        }

        private string GetKnownMessageDestinationId(GenericNetworkIdentifier remoteEndpoint)
        {
            if (VaryMessageSourceIds)
            {
                // no need to bother then.
                return null;
            }
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
            if (VaryMessageSourceIds)
            {
                // no need to save then.
                return;
            }
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
                    knownMessageDestinationIds.Remove(remoteEndpoint));
            }
        }

        public void BeginReceive(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram pdu)
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
            if (pdu.OpCode == ProtocolDatagram.OpCodeHeader)
            {
                ProcessHeaderPduReceiveRequest(remoteEndpoint, pdu);
            }
            else if (pdu.OpCode == ProtocolDatagram.OpCodeData)
            {
                ProcessDataPduReceiveRequest(remoteEndpoint, pdu);
            }
            if (pdu.OpCode == ProtocolDatagram.OpCodeHeaderAck ||
                pdu.OpCode == ProtocolDatagram.OpCodeDataAck)
            {
                ProcessAckPduReceiveRequest(remoteEndpoint, pdu);
            }
            else
            {
                throw new Exception($"unexpected opcode: {pdu.OpCode}");
            }
        }

        private void ProcessHeaderPduReceiveRequest(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram pdu)
        {
            IncomingTransfer transfer = incomingTransfers.Get(remoteEndpoint, pdu.MessageId, null);
            if (transfer?.Processed == true)
            {
                if (transfer.MessageSrcId == pdu.MessageDestinationId && transfer.ExpectedSequenceNumber == 0)
                {
                    // send back again the last ack sent out.
                    if (transfer.LastAckSent == null)
                    {
                        transfer.LastAckSent = new ProtocolDatagram
                        {
                            OpCode = ProtocolDatagram.OpCodeHeaderAck,
                            Version = ProtocolDatagram.ProtocolVersion1_0,
                            MessageId = transfer.MessageId,
                            MessageSourceId = transfer.MessageSrcId,
                            ErrorCode = transfer.ProcessingErrorCode
                        };
                    }
                    // ignore outcome of send.
                    UnderlyingTransport.BeginSend(remoteEndpoint, transfer.LastAckSent, null);
                }
                else
                {
                    // discard
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
                    if (VaryMessageSourceIds)
                    {
                        transfer.MessageSrcId = ByteUtils.GenerateUuid();

                        // before we can tell whether pdu is worth processing,
                        // transfer may not need to be actually set up.
                        // only add transfer when varying source ids
                        incomingTransfers.Add(remoteEndpoint, pdu.MessageId, transfer);
                        transfer.ReceiveTimeoutId = EventLoop.ScheduleTimeout(DataReceiveTimeout,
                            () => AbortReceiveTransfer(transfer, ProtocolOperationException.ErrorCodeReceiveTimeout));
                    }
                }
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
                    UnderlyingTransport.BeginSend(remoteEndpoint, ack, null);
                }
                else if (transfer.ExpectedSequenceNumber != 0)
                {
                    if (transfer.ExpectedSequenceNumber == 1)
                    {
                        // send back again the last ack sent out.
                        // ignore outcome of send.
                        UnderlyingTransport.BeginSend(remoteEndpoint, transfer.LastAckSent, null);
                    }
                    else
                    {
                        // discard
                    }
                }
                else if (pdu.MessageLength > UnconfiguredMaximumMessageLength &&
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
                    UnderlyingTransport.BeginSend(remoteEndpoint, ack, null);
                }
                else
                {
                    // all is well.
                    transfer.BytesRemaining = pdu.MessageLength;

                    // ensure addition of transfer
                    incomingTransfers.Add(remoteEndpoint, pdu.MessageId, transfer);
                        
                    // reset timeout
                    EventLoop.CancelTimeout(transfer.ReceiveTimeoutId);
                    transfer.ReceiveTimeoutId = EventLoop.ScheduleTimeout(DataReceiveTimeout,
                        () => AbortReceiveTransfer(transfer, ProtocolOperationException.ErrorCodeReceiveTimeout));

                    int dataLengthToUse = Math.Min(pdu.DataLength, transfer.BytesRemaining);
                    transfer.ReceiveBuffer.Write(pdu.Data, pdu.DataOffset, dataLengthToUse);
                    transfer.BytesRemaining -= dataLengthToUse;
                    if (transfer.BytesRemaining > 0 && pdu.DataLength < MinimumNonTerminatingPduDataSize)
                    {
                        // mark as processed and failed.
                        AbortReceiveTransfer(transfer, ProtocolOperationException.ErrorCodeAbortedFromSender);
                    }
                    else
                    {
                        if (transfer.BytesRemaining == 0)
                        {
                            // mark as processed and successful.
                            AbortReceiveTransfer(transfer, 0);
                        }
                        else
                        {
                            transfer.ExpectedSequenceNumber++;
                        }
                        transfer.LastAckSent = new ProtocolDatagram
                        {
                            OpCode = ProtocolDatagram.OpCodeHeaderAck,
                            Version = ProtocolDatagram.ProtocolVersion1_0,
                            MessageId = transfer.MessageId,
                            MessageSourceId = transfer.MessageSrcId
                        };
                        // ignore outcome of send.
                        UnderlyingTransport.BeginSend(remoteEndpoint, transfer.LastAckSent, null);
                    }
                }
            }
        }

        private void ProcessDataPduReceiveRequest(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram pdu)
        {
            IncomingTransfer transfer = incomingTransfers.Get(remoteEndpoint, pdu.MessageId, null);
            if (transfer == null)
            {
                // discard
                return;
            }
            if (transfer.Processed == true)
            {
                if (transfer.MessageSrcId == pdu.MessageDestinationId && transfer.ExpectedSequenceNumber == pdu.SequenceNumber)
                {
                    // send back again the last ack sent out.
                    if (transfer.LastAckSent == null)
                    {
                        transfer.LastAckSent = new ProtocolDatagram
                        {
                            OpCode = ProtocolDatagram.OpCodeDataAck,
                            Version = ProtocolDatagram.ProtocolVersion1_0,
                            MessageId = transfer.MessageId,
                            MessageSourceId = transfer.MessageSrcId,
                            SequenceNumber = transfer.ExpectedSequenceNumber,
                            ErrorCode = transfer.ProcessingErrorCode
                        };
                    }
                    // ignore outcome of send.
                    UnderlyingTransport.BeginSend(remoteEndpoint, transfer.LastAckSent, null);
                }
                else
                {
                    // discard
                }
            }
            else
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
                    UnderlyingTransport.BeginSend(remoteEndpoint, ack, null);
                }
                else if (transfer.ExpectedSequenceNumber != pdu.SequenceNumber)
                {
                    if (transfer.ExpectedSequenceNumber == pdu.SequenceNumber + 1)
                    {
                        // send back again the last ack sent out.
                        // ignore outcome of send.
                        UnderlyingTransport.BeginSend(remoteEndpoint, transfer.LastAckSent, null);
                    }
                    else
                    {
                        // discard
                    }
                }
                else
                {
                    // all is well.

                    // reset timeout
                    EventLoop.CancelTimeout(transfer.ReceiveTimeoutId);
                    transfer.ReceiveTimeoutId = EventLoop.ScheduleTimeout(DataReceiveTimeout,
                        () => AbortReceiveTransfer(transfer, ProtocolOperationException.ErrorCodeReceiveTimeout));

                    int dataLengthToUse = Math.Min(pdu.DataLength, transfer.BytesRemaining);
                    transfer.ReceiveBuffer.Write(pdu.Data, pdu.DataOffset, dataLengthToUse);
                    transfer.BytesRemaining -= dataLengthToUse;
                    if (transfer.BytesRemaining > 0 && pdu.DataLength < MinimumNonTerminatingPduDataSize)
                    {
                        // mark as processed and failed.
                        AbortReceiveTransfer(transfer, ProtocolOperationException.ErrorCodeAbortedFromSender);
                    }
                    else
                    {
                        if (transfer.BytesRemaining == 0)
                        {
                            // mark as processed and successful.
                            AbortReceiveTransfer(transfer, 0);
                        }
                        else
                        {
                            transfer.ExpectedSequenceNumber++;
                        }
                        transfer.LastAckSent = new ProtocolDatagram
                        {
                            OpCode = ProtocolDatagram.OpCodeDataAck,
                            Version = ProtocolDatagram.ProtocolVersion1_0,
                            MessageId = transfer.MessageId,
                            MessageSourceId = transfer.MessageSrcId,
                            SequenceNumber = pdu.SequenceNumber
                        };
                        var lastAckBytes = transfer.LastAckSent.Serialize();
                        // ignore outcome of send.
                        UnderlyingTransport.BeginSend(remoteEndpoint, transfer.LastAckSent, null);
                    }
                }
            }
        }

        private void AbortReceiveTransfer(IncomingTransfer transfer, short errorCode)
        {
            if (transfer.Processed)
            {
                // ignore.
                return;
            }
            // mark as processed.
            transfer.Processed = true;
            transfer.ProcessingErrorCode = errorCode;
            EventLoop.CancelTimeout(transfer.ReceiveTimeoutId);
            if (errorCode == 0 && EventListener != null)
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
            transfer.ExpirationTimeoutId = EventLoop.ScheduleTimeout(ProcessedMessageDisposalWaitTime,
                () => incomingTransfers.Remove(transfer.RemoteEndpoint, transfer.MessageId));
        }

        public void Reset(ProtocolOperationException causeOfReset)
        {
            // cancel all outgoing transfers.
            var endpoints = outgoingTransfers.GetEndpoints();
            foreach (var endpoint in endpoints)
            {
                foreach (var transfer in outgoingTransfers.GetValues(endpoint))
                {
                    transfer.SendCancellationHandle.Cancel();
                    EventLoop.CancelTimeout(transfer.RetryBackoffTimeoutId);
                    EventLoop.CancelTimeout(transfer.ReceiveAckTimeoutId);
                    transfer.SendCallback?.Invoke(causeOfReset);
                    // send pending pdu with empty data to trigger early abort in receiver
                    // before waiting for full timeout.
                    transfer.DataLengthToSend = 0;
                    SendPendingPdu(transfer, false);
                }
                outgoingTransfers.RemoveAll(endpoint);
            }

            // cancel all receives
            endpoints = incomingTransfers.GetEndpoints();
            foreach (var endpoint in endpoints)
            {
                foreach (var transfer in incomingTransfers.GetValues(endpoint))
                {
                    if (!transfer.Processed)
                    {
                        // mark as processed.
                        transfer.Processed = true;
                        transfer.ProcessingErrorCode = ProtocolOperationException.ErrorCodeAbortedFromReceiver;
                        EventLoop.CancelTimeout(transfer.ReceiveTimeoutId);
                        transfer.ReceiveBuffer.Dispose();
                        transfer.ExpirationTimeoutId = EventLoop.ScheduleTimeout(ProcessedMessageDisposalWaitTime,
                            () => incomingTransfers.Remove(transfer.RemoteEndpoint, transfer.MessageId));
                    }
                }
            }

            // clear endpoint ownership information
            foreach (var entry in knownMessageDestinationIds.Values)
            {
                EventLoop.CancelTimeout(entry.TimeoutId);
            }
            knownMessageDestinationIds.Clear();
        }
    }
}
