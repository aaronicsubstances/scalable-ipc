﻿using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.ErrorHandling;
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
        private string endpointOwnerId;
        private object lastEndpointOwnerResetTimeoutId;

        public ScalableIpcProtocol()
        {
            incomingTransfers = new EndpointStructuredDatastore<IncomingTransfer>();
            outgoingTransfers = new EndpointStructuredDatastore<OutgoingTransfer>();
            knownMessageDestinationIds = new Dictionary<GenericNetworkIdentifier, EndpointOwnerIdInfo>();
        }

        public int EndpointOwnerIdResetPeriod { get; set; }
        public int MaximumPduDataSize { get; set; } // defaults to 512
        public int MaximumReceivableMessageLength { get; set; } // defaults to no maximum.
        public int MinRetryBackoffPeriod { get; set; }
        public int MaxRetryBackoffPeriod { get; set; } // defaults to min retry
        public int DefaultReceiveAckTimeout { get; set; }
        public int DataReceiveTimeout { get; set; }
        public int KnownMessageDestinationLifeTime { get; set; }
        public ScalableIpcProtocolListener EventListener { get; set; }
        public TransportApi UnderlyingTransport { get; set; }
        public EventLoopApi EventLoop { get; set; }
        
        internal ProtocolInternalsReporter InternalsReporter { get; set; }

        public void BeginSend(GenericNetworkIdentifier remoteEndpoint, ProtocolMessage msg,
            MessageSendOptions options, Action<ProtocolException> cb)
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
            transfer.PduDataSize = Math.Max(MaximumPduDataSize, MinimumNonTerminatingPduDataSize);

            // start ack timeout
            PostponeSendDataTimeout(transfer);

            transfer.PendingDataLengthToSend = Math.Min(transfer.EndOffset - transfer.StartOffset,
                transfer.PduDataSize);
            SendPendingPdu(transfer, true);
        }

        private void AbortSendTransfer(OutgoingTransfer transfer, ProtocolErrorCode abortCode,
            ProtocolException ex = null)
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
                if (abortCode == ProtocolErrorCode.Success)
                {
                    transfer.SendCallback(null);
                }
                else
                {
                    transfer.SendCallback(ex ?? new ProtocolException(abortCode));
                }
            }
            if (ex != null || abortCode == ProtocolErrorCode.SendTimeout)
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
                OpCode = transfer.PendingSequenceNumber == 0 ?
                    ProtocolDatagram.OpCodeHeader : ProtocolDatagram.OpCodeData,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                MessageDestinationId = transfer.MessageDestinationId,
                MessageId = transfer.MessageId,
                MessageLength = transfer.EndOffset - transfer.StartOffset,
                Data = transfer.Data,
                DataOffset = transfer.StartOffset,
                DataLength = transfer.PendingDataLengthToSend,
                SequenceNumber = transfer.PendingSequenceNumber
            };
            Action<ProtocolException> sendCb = null;
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
        private void ProcessReceiveHeaderAckRequest(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram ack)
        {
            OutgoingTransfer transfer = outgoingTransfers.Get(remoteEndpoint, ack.MessageId, null);
            if (transfer == null)
            {
                // discard.
                return;
            }

            // assert that current index is 0
            if (transfer.PendingSequenceNumber != 0)
            {
                // discard.
                return;
            }

            // assert that there is no error code
            if (ack.ErrorCode != 0)
            {
                if (ack.ErrorCode == ProtocolErrorCode.InvalidDestinationEndpointId.Value)
                {
                    if (transfer.MessageDestinationId != ack.MessageSourceId)
                    {
                        transfer.MessageDestinationId = ack.MessageSourceId;

                        // save for future use.
                        UpdateKnownMessageDestinationIds(remoteEndpoint, ack.MessageSourceId);

                        // instead of waiting for retry backoff timer, send updated pdu
                        // once.
                        SendPendingPdu(transfer, false);
                    }
                    return;
                }
                if (ack.ErrorCode == ProtocolErrorCode.InvalidPduDataSizeExceeded.Value)
                {
                    if (transfer.PduDataSize > MinimumNonTerminatingPduDataSize)
                    {
                        transfer.PduDataSize = MinimumNonTerminatingPduDataSize;

                        // refragment.
                        transfer.PendingDataLengthToSend = Math.Min(transfer.EndOffset - transfer.StartOffset,
                            transfer.PduDataSize);

                        // instead of waiting for retry backoff timer, send updated pdu
                        // once.
                        SendPendingPdu(transfer, false);
                    }
                    return;
                }
                // by default abort transfer.
                AbortSendTransfer(transfer, ProtocolErrorCode.GetInstance(ack.ErrorCode) ??
                    ProtocolErrorCode.AbortedFromSender);
                return;
            }

            // successfully sent pending pdu.
            AdvanceOutgoingTransfer(transfer);
        }
        private void ProcessReceiveDataAckRequest(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram ack)
        {
            OutgoingTransfer transfer = outgoingTransfers.Get(remoteEndpoint, ack.MessageId, null);
            if (transfer == null)
            {
                // discard.
                return;
            }

            // assert that current index is greater than 0
            if (transfer.PendingSequenceNumber == 0)
            {
                // discard.
                return;
            }

            // assert that the sequence number of the ack equals the sequence number of the current pdu
            if (transfer.PendingSequenceNumber != ack.SequenceNumber)
            {
                // discard.
                return;
            }

            // assert that there is no error code
            if (ack.ErrorCode != 0)
            {
                AbortSendTransfer(transfer, ProtocolErrorCode.GetInstance(ack.ErrorCode) ??
                    ProtocolErrorCode.AbortedFromSender);
                return;
            }

            // successfully sent pending pdu.
            AdvanceOutgoingTransfer(transfer);
        }

        private void AdvanceOutgoingTransfer(OutgoingTransfer transfer)
        {
            transfer.StartOffset += transfer.PendingDataLengthToSend;

            // check if we are done.
            if (transfer.StartOffset == transfer.EndOffset)
            {
                AbortSendTransfer(transfer, ProtocolErrorCode.Success);
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
                    transfer.PduDataSize);
                transfer.PendingSequenceNumber++;
                SendPendingPdu(transfer, true);
            }
        }

        private void PostponeSendDataTimeout(OutgoingTransfer transfer)
        {
            EventLoop.CancelTimeout(transfer.ReceiveAckTimeoutId);
            transfer.ReceiveAckTimeoutId = EventLoop.ScheduleTimeout(transfer.ReceiveAckTimeout,
                () => AbortSendTransfer(transfer, ProtocolErrorCode.SendTimeout));
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
            if (endpointOwnerId == null)
            {
                ResetEndpointOwnerId();
            }
            switch (pdu.OpCode)
            {
                case ProtocolDatagram.OpCodeHeader:
                    ProcessReceiveHeaderRequest(remoteEndpoint, pdu);
                    break;
                case ProtocolDatagram.OpCodeData:
                    ProcessReceiveDataRequest(remoteEndpoint, pdu);
                    break;
                case ProtocolDatagram.OpCodeHeaderAck:
                    ProcessReceiveHeaderAckRequest(remoteEndpoint, pdu);
                    break;
                case ProtocolDatagram.OpCodeDataAck:
                    ProcessReceiveDataAckRequest(remoteEndpoint, pdu);
                    break;
            }
        }

        private void ProcessReceiveHeaderRequest(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram pdu)
        {
            // assert message id doesn't exist or expected seq nr is 0.
            IncomingTransfer transfer = incomingTransfers.Get(remoteEndpoint, pdu.MessageId, null);
            if (transfer == null || transfer.ExpectedSequenceNumber == 0)
            {
                // assertion holds.
            }
            else
            {
                // ignore, except in the case where expected seq nr is 1.
                // in that case send back the last ack sent.
                if (transfer.ExpectedSequenceNumber == 1)
                {
                    UnderlyingTransport.BeginSend(remoteEndpoint, transfer.LastAckSent, null);
                }
                return;
            }

            // assert message length.
            if (pdu.MessageLength > UnconfiguredMaximumMessageLength &&
                pdu.MessageLength > MaximumReceivableMessageLength)
            {
                var errorAck = new ProtocolDatagram
                {
                    OpCode = ProtocolDatagram.OpCodeHeaderAck,
                    Version = ProtocolDatagram.ProtocolVersion1_0,
                    MessageId = pdu.MessageId,
                    MessageSourceId = endpointOwnerId,
                    ErrorCode = ProtocolErrorCode.MessageTooLarge.Value
                };
                UnderlyingTransport.BeginSend(remoteEndpoint, errorAck, null);
                return;
            }

            // assert pdu data size.
            bool dataSizeValid = pdu.DataLength <= MaximumPduDataSize;
            // if pdu isn't the last pdu of message, also assert that the data size is at least 512.
            if (pdu.DataLength < transfer.BytesRemaining)
            {
                dataSizeValid = dataSizeValid && pdu.DataLength >= MinimumNonTerminatingPduDataSize;
            }
            if (!dataSizeValid)
            {
                var errorAck = new ProtocolDatagram
                {
                    OpCode = ProtocolDatagram.OpCodeHeaderAck,
                    Version = ProtocolDatagram.ProtocolVersion1_0,
                    MessageId = pdu.MessageId,
                    MessageSourceId = endpointOwnerId,
                    ErrorCode = ProtocolErrorCode.InvalidPduDataSizeExceeded.Value
                };
                UnderlyingTransport.BeginSend(remoteEndpoint, errorAck, null);
                return;
            }

            // assert that message destination id matches endpoint owner id.
            if (pdu.MessageDestinationId != endpointOwnerId)
            {
                var errorAck = new ProtocolDatagram
                {
                    OpCode = ProtocolDatagram.OpCodeHeaderAck,
                    Version = ProtocolDatagram.ProtocolVersion1_0,
                    MessageId = pdu.MessageId,
                    MessageSourceId = endpointOwnerId,
                    ErrorCode = ProtocolErrorCode.InvalidDestinationEndpointId.Value
                };
                UnderlyingTransport.BeginSend(remoteEndpoint, errorAck, null);
                return;
            }

            // if message id is already processed, then send back the last ack sent 
            // (or construct one for aborted cases).
            if (transfer != null)
            {
                // assert that transfer is processed at this stage.
                if (!transfer.Processed)
                {
                    throw new Exception("expected incoming transfer to be processed at this stage");
                }
                transfer.EnsureLastAckSentExists();
                UnderlyingTransport.BeginSend(remoteEndpoint, transfer.LastAckSent, null);
                return;
            }

            // all is well.

            transfer = new IncomingTransfer
            {
                RemoteEndpoint = remoteEndpoint,
                MessageId = pdu.MessageId,
                MessageSrcId = endpointOwnerId,
                ReceiveBuffer = new MemoryStream(),
                BytesRemaining = pdu.MessageLength,
                ExpectedSequenceNumber = 0
            };
            incomingTransfers.Add(remoteEndpoint, pdu.MessageId, transfer);

            // transfer data from pdu to buffer
            int dataLengthToUse = Math.Min(pdu.DataLength, transfer.BytesRemaining);
            transfer.ReceiveBuffer.Write(pdu.Data, pdu.DataOffset, dataLengthToUse);
            transfer.BytesRemaining -= dataLengthToUse;

            if (transfer.BytesRemaining == 0)
            {
                AbortReceiveTransfer(transfer, ProtocolErrorCode.Success);
            }
            else
            {
                transfer.ExpectedSequenceNumber++;
                // set timeout
                PostponeReceiveDataTimeout(transfer);
            }
            transfer.LastAckSent = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeHeaderAck,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                MessageId = transfer.MessageId,
                MessageSourceId = transfer.MessageSrcId
            };
            UnderlyingTransport.BeginSend(remoteEndpoint, transfer.LastAckSent, null);
        }

        private void ProcessReceiveDataRequest(GenericNetworkIdentifier remoteEndpoint,
            ProtocolDatagram pdu)
        {
            IncomingTransfer transfer = incomingTransfers.Get(remoteEndpoint, pdu.MessageId, null);
            if (transfer == null)
            {
                // ignore
                return;
            }

            // assert pdu seq nr matches expected seq nr.
            if (transfer.ExpectedSequenceNumber != pdu.SequenceNumber)
            {
                // ignore, except in the case where expected seq nr is 1 more than pdu seq nr.
                // in that case send back the last ack sent.
                if (transfer.ExpectedSequenceNumber == pdu.SequenceNumber + 1)
                {
                    UnderlyingTransport.BeginSend(remoteEndpoint, transfer.LastAckSent, null);
                }
                return;
            }

            // assert pdu data size.
            bool dataSizeValid = pdu.DataLength <= MaximumPduDataSize;
            if (!dataSizeValid)
            {
                var errorAck = new ProtocolDatagram
                {
                    OpCode = ProtocolDatagram.OpCodeDataAck,
                    Version = ProtocolDatagram.ProtocolVersion1_0,
                    MessageId = pdu.MessageId,
                    MessageSourceId = endpointOwnerId,
                    ErrorCode = ProtocolErrorCode.InvalidPduDataSizeExceeded.Value,
                    SequenceNumber = pdu.SequenceNumber
                };
                UnderlyingTransport.BeginSend(remoteEndpoint, errorAck, null);
                return;
            }

            // assert that message destination id matches endpoint owner id.
            if (pdu.MessageDestinationId != endpointOwnerId)
            {
                var errorAck = new ProtocolDatagram
                {
                    OpCode = ProtocolDatagram.OpCodeDataAck,
                    Version = ProtocolDatagram.ProtocolVersion1_0,
                    MessageId = pdu.MessageId,
                    MessageSourceId = endpointOwnerId,
                    ErrorCode = ProtocolErrorCode.InvalidDestinationEndpointId.Value,
                    SequenceNumber = pdu.SequenceNumber
                };
                UnderlyingTransport.BeginSend(remoteEndpoint, errorAck, null);
                return;
            }

            // if message id is already processed, then send back the last ack sent 
            // (or construct one for aborted cases).
            if (transfer.Processed)
            {
                transfer.EnsureLastAckSentExists();
                UnderlyingTransport.BeginSend(remoteEndpoint, transfer.LastAckSent, null);
                return;
            }

            // if pdu isn't the last pdu of message, and data size is less than 512, 
            // interpret that as intention by sender to abort transfer.
            if (pdu.DataLength < transfer.BytesRemaining)
            {
                if (pdu.DataLength < MinimumNonTerminatingPduDataSize)
                {
                    AbortReceiveTransfer(transfer, ProtocolErrorCode.AbortedFromSender);
                    return;
                }
            }

            // all is well.

            int dataLengthToUse = Math.Min(pdu.DataLength, transfer.BytesRemaining);
            transfer.ReceiveBuffer.Write(pdu.Data, pdu.DataOffset, dataLengthToUse);
            transfer.BytesRemaining -= dataLengthToUse;

            if (transfer.BytesRemaining == 0)
            {
                // mark as processed and successful.
                AbortReceiveTransfer(transfer, ProtocolErrorCode.Success);
            }
            else
            {
                transfer.ExpectedSequenceNumber++;
                // reset timeout
                PostponeReceiveDataTimeout(transfer);
            }
            transfer.LastAckSent = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeDataAck,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                MessageId = transfer.MessageId,
                MessageSourceId = transfer.MessageSrcId,
                SequenceNumber = pdu.SequenceNumber
            };
            // ignore outcome of send.
            UnderlyingTransport.BeginSend(remoteEndpoint, transfer.LastAckSent, null);
        }

        private void PostponeReceiveDataTimeout(IncomingTransfer transfer)
        {
            EventLoop.CancelTimeout(transfer.ReceiveDataTimeoutId);
            transfer.ReceiveDataTimeoutId = EventLoop.ScheduleTimeout(DataReceiveTimeout,
                () => AbortReceiveTransfer(transfer, ProtocolErrorCode.ReceiveTimeout));
        }

        private void AbortReceiveTransfer(IncomingTransfer transfer, ProtocolErrorCode abortCode)
        {
            if (transfer.Processed)
            {
                // ignore.
                return;
            }
            // mark as processed.
            transfer.Processed = true;
            if (abortCode.Value < 0)
            {
                transfer.ProcessingErrorCode = ProtocolErrorCode.ProcessingError.Value;
            }
            else {
                transfer.ProcessingErrorCode = abortCode.Value;
            }
            EventLoop.CancelTimeout(transfer.ReceiveDataTimeoutId);
            if (abortCode == ProtocolErrorCode.Success && EventListener != null)
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

        public void Reset(ProtocolException causeOfReset)
        {
            EventLoop.PostCallback(() =>
            {
                // cancel all outgoing transfers.
                var endpoints = outgoingTransfers.GetEndpoints();
                foreach (var endpoint in endpoints)
                {
                    foreach (var transfer in outgoingTransfers.GetValues(endpoint))
                    {
                        AbortSendTransfer(transfer, causeOfReset.ErrorCode, causeOfReset);
                    }
                }
                outgoingTransfers.Clear();

                // cancel all receives
                endpoints = incomingTransfers.GetEndpoints();
                foreach (var endpoint in endpoints)
                {
                    foreach (var transfer in incomingTransfers.GetValues(endpoint))
                    {
                        AbortReceiveTransfer(transfer, causeOfReset.ErrorCode);
                    }
                }
                incomingTransfers.Clear();

                // clear endpoint ownership information
                foreach (var entry in knownMessageDestinationIds.Values)
                {
                    EventLoop.CancelTimeout(entry.TimeoutId);
                }
                knownMessageDestinationIds.Clear();

                // cancel, but don't reset endpoint owner id
                endpointOwnerId = null;
                EventLoop.CancelTimeout(lastEndpointOwnerResetTimeoutId);
                lastEndpointOwnerResetTimeoutId = null;
            });
        }

        private void ResetEndpointOwnerId()
        {
            endpointOwnerId = ByteUtils.GenerateUuid();
            EventLoop.CancelTimeout(lastEndpointOwnerResetTimeoutId);
            lastEndpointOwnerResetTimeoutId = EventLoop.ScheduleTimeout(EndpointOwnerIdResetPeriod,
                () => ResetEndpointOwnerId());

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
            InternalsReporter?.OnEndpointOwnerIdReset(endpointOwnerId);
        }
    }
}
