using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Concurrency;
using ScalableIPC.Core.ErrorHandling;
using ScalableIPC.Core.Helpers;
using ScalableIPC.Core.ProtocolOperation;
using System;
using System.IO;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ScalableIPC.Core.UnitTests")]
[assembly: InternalsVisibleTo("ScalableIPC.IntegrationTests")]

namespace ScalableIPC.Core
{
    public class ScalableIpcProtocol : TransportApiCallbacks
    {
        public const int DefaultMaximumMessageLength = 65_536;
        public const int DefaultMaximumPduDataSize = 512;

        private readonly EndpointStructuredDatastore<IncomingTransfer> incomingTransfers;
        private readonly EndpointStructuredDatastore<OutgoingTransfer> outgoingTransfers;
        private string endpointOwnerId;
        private CancellationHandle lastEndpointOwnerResetTimeoutId;

        public ScalableIpcProtocol()
        {
            incomingTransfers = new EndpointStructuredDatastore<IncomingTransfer>();
            outgoingTransfers = new EndpointStructuredDatastore<OutgoingTransfer>();
            EndpointInfoDatastore = new DefaultEndpointInfoDatastore(-1);
            EventLoop = new DefaultEventLoopApi();
        }

        public int ReceiveTimeout { get; set; } // absolutely required.
        public int EndpointOwnerIdResetPeriod { get; set; } // defaults to receive timeout.
        public int MaximumPduDataSize { get; set; } // defaults to 512
        public int MaximumReceivableMessageLength { get; set; } // defaults to 65,536.
        public int MinRetryBackoffPeriod { get; set; } // defaults to fraction of receive timeout.
        public int MaxRetryBackoffPeriod { get; set; } // defaults to min retry
        public ScalableIpcProtocolListener EventListener { get; set; } // recommended but not required.
        internal ProtocolMonitor MonitoringAgent { get; set; } // used for testing only
        public TransportApi UnderlyingTransport { get; set; } // absolutely required
        public EventLoopApi EventLoop { get; set; } // absolutely required
        public IEndpointInfoDatastore EndpointInfoDatastore { get; set; } // recommended but not required.

        public void BeginSend(GenericNetworkIdentifier remoteEndpoint, ProtocolMessage msg,
            MessageSendOptions options, Action<ProtocolException> cb)
        {
            // validate transfer
            if (remoteEndpoint == null)
            {
                throw new ArgumentNullException(nameof(remoteEndpoint));
            }
            if (msg.Offset < 0)
            {
                throw new Exception("negative start offset");
            }
            if (msg.Length < 0)
            {
                throw new Exception("negative length");
            }
            if (msg.Offset + msg.Length > msg.Data.Length)
            {
                throw new Exception("invalid end offset");
            }
            msg.Id = ByteUtils.GenerateUuid();
            var transfer = new OutgoingTransfer
            {
                MessageId = msg.Id,
                RemoteEndpoint = remoteEndpoint,
                Data = msg.Data,
                StartOffset = msg.Offset,
                EndOffset = msg.Offset + msg.Length,
                MessageSendCallback = cb,
                ReceiveAckTimeout = ReceiveTimeout
            };
            if (options != null && options.Timeout > 0)
            {
                transfer.ReceiveAckTimeout = options.Timeout;
            }
            PostCallbackSafely(() =>
            {
                ProcessMessageSendRequest(transfer);
            });
        }

        private void ProcessMessageSendRequest(OutgoingTransfer transfer)
        {
            outgoingTransfers.Add(transfer.RemoteEndpoint, transfer.MessageId, transfer);
            MonitoringAgent?.OnSendDataAdded(transfer);

            transfer.MessageDestinationId = EndpointInfoDatastore?.Get(transfer.RemoteEndpoint) ??
                ByteUtils.GenerateUuid();
            transfer.PduDataSize = MaximumPduDataSize < 1 ? DefaultMaximumPduDataSize : MaximumPduDataSize;

            // start ack timeout
            PostponeSendDataTimeout(transfer);
            // kickstart sending
            transfer.PendingDataLengthToSend = Math.Min(transfer.EndOffset - transfer.StartOffset,
                transfer.PduDataSize);
            SendPendingPdu(transfer, true);
        }

        private void AbortSendTransfer(OutgoingTransfer transfer, ProtocolErrorCode abortCode)
        {
            if (!outgoingTransfers.Remove(transfer.RemoteEndpoint, transfer.MessageId))
            {
                // ignore
                return;
            }
            transfer.SendCancellationHandle?.Cancel();
            transfer.RetryBackoffTimeoutId?.Cancel();
            transfer.ReceiveAckTimeoutId?.Cancel();
            if (transfer.MessageSendCallback != null)
            {
                if (abortCode == ProtocolErrorCode.Success)
                {
                    transfer.MessageSendCallback(null);
                }
                else
                {
                    transfer.MessageSendCallback(new ProtocolException(abortCode));
                }
            }
            if (abortCode == ProtocolErrorCode.SendTimeout)
            {
                // send pending pdu with empty data to trigger early abort in receiver
                // before waiting for full timeout.
                transfer.PendingDataLengthToSend = 0;
                SendPendingPdu(transfer, false);
            }
            MonitoringAgent?.OnSendDataAborted(transfer, abortCode);
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
                var cancellationHandle = new CancellationHandle(null);
                transfer.SendCancellationHandle = cancellationHandle;
                sendCb = _ =>
                {
                    PostCallbackSafely(() => ProcessSendPduOutcome(
                        transfer, cancellationHandle));
                };
            }
            UnderlyingTransport.BeginSend(transfer.RemoteEndpoint, pdu, sendCb);
        }

        private void ProcessSendPduOutcome(OutgoingTransfer transfer, CancellationHandle cancellationHandle)
        {
            if (cancellationHandle.Cancelled) return;

            int retryBackoffPeriod = MinRetryBackoffPeriod;
            if (retryBackoffPeriod < 1)
            {
                // arbitrarily set default backoff period to be a tenth of receive timeout.
                retryBackoffPeriod = Math.Max(ReceiveTimeout / 10, 1);
            }

            if (MaxRetryBackoffPeriod > retryBackoffPeriod)
            {
                retryBackoffPeriod += MathUtils.GetRandomInt(MaxRetryBackoffPeriod - retryBackoffPeriod);
            }
            transfer.RetryBackoffTimeoutId = ScheduleTimeoutSafely(retryBackoffPeriod,
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
                    string expectedMsgDestId = ByteUtils.ConvertBytesToHex(ack.Data, ack.DataOffset,
                        ack.DataLength);
                    if (expectedMsgDestId.Length == ProtocolDatagram.MsgIdStrLength &&
                        transfer.MessageDestinationId != expectedMsgDestId)
                    {
                        transfer.MessageDestinationId = expectedMsgDestId;

                        // save for future use.
                        EndpointInfoDatastore?.Update(remoteEndpoint, transfer.MessageDestinationId);

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
                transfer.SendCancellationHandle?.Cancel();
                transfer.RetryBackoffTimeoutId?.Cancel();

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
            transfer.ReceiveAckTimeoutId?.Cancel();
            transfer.ReceiveAckTimeoutId = ScheduleTimeoutSafely(transfer.ReceiveAckTimeout,
                () => AbortSendTransfer(transfer, ProtocolErrorCode.SendTimeout));
        }

        public void BeginReceive(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram pdu)
        {
            if (remoteEndpoint == null)
            {
                throw new ArgumentNullException(nameof(remoteEndpoint));
            }
            if (pdu == null)
            {
                throw new ArgumentNullException(nameof(pdu));
            }
            PostCallbackSafely(() =>
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
            int effectiveMaxMsgLength = MaximumReceivableMessageLength < 1 ?
                DefaultMaximumMessageLength : MaximumReceivableMessageLength;
            if (pdu.MessageLength > effectiveMaxMsgLength)
            {
                SendReplyAck(remoteEndpoint, pdu.MessageId, 0,
                    ProtocolErrorCode.MessageTooLarge, null);
                return;
            }

            // assert that message destination id matches endpoint owner id.
            if (pdu.MessageDestinationId != endpointOwnerId)
            {
                SendReplyAck(remoteEndpoint, pdu.MessageId, 0,
                    ProtocolErrorCode.InvalidDestinationEndpointId,
                    ByteUtils.ConvertHexToBytes(endpointOwnerId));
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
            MonitoringAgent?.OnReceiveDataAdded(transfer);

            // transfer data from pdu to buffer
            int dataLengthToUse = Math.Min(transfer.BytesRemaining, pdu.DataLength);
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
            // in any case send back ack
            transfer.LastAckSent = SendReplyAck(transfer.RemoteEndpoint,
                transfer.MessageId, 0, null, null);
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

            // assert that message destination id matches endpoint owner id.
            if (pdu.MessageDestinationId != endpointOwnerId)
            {
                SendReplyAck(remoteEndpoint, pdu.MessageId, pdu.SequenceNumber,
                    ProtocolErrorCode.InvalidDestinationEndpointId,
                    ByteUtils.ConvertHexToBytes(transfer.MessageSrcId));
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

            // if pdu isn't the last pdu of message, and data size is empty, 
            // interpret that as intention by sender to abort transfer.
            if (pdu.DataLength < transfer.BytesRemaining)
            {
                if (pdu.DataLength  == 0)
                {
                    AbortReceiveTransfer(transfer, ProtocolErrorCode.AbortedFromSender);
                    return;
                }
            }

            // all is well.

            int dataLengthToUse = Math.Min(transfer.BytesRemaining, pdu.DataLength);
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
            // in any case send back ack
            transfer.LastAckSent = SendReplyAck(transfer.RemoteEndpoint,
                transfer.MessageId, pdu.SequenceNumber, null, null);
        }

        private ProtocolDatagram SendReplyAck(GenericNetworkIdentifier remoteEndpoint, string messageId,
            int seqNr, ProtocolErrorCode errorCode, byte[] data)
        {
            var ack = new ProtocolDatagram
            {
                OpCode = seqNr == 0 ? ProtocolDatagram.OpCodeHeaderAck :
                    ProtocolDatagram.OpCodeDataAck,
                Version = ProtocolDatagram.ProtocolVersion1_0,
                MessageId = messageId,
                SequenceNumber = seqNr,
                ErrorCode = errorCode?.Value ?? 0,
                Data = data,
                DataLength = data?.Length ?? 0
            };
            UnderlyingTransport.BeginSend(remoteEndpoint, ack, null);
            return ack;
        }

        private void PostponeReceiveDataTimeout(IncomingTransfer transfer)
        {
            transfer.ReceiveDataTimeoutId?.Cancel();
            transfer.ReceiveDataTimeoutId = ScheduleTimeoutSafely(ReceiveTimeout,
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
            transfer.ProcessedAt = EventLoop.CurrentTimestamp;
            if (abortCode.Value < 0)
            {
                transfer.ProcessingErrorCode = ProtocolErrorCode.ProcessingError.Value;
            }
            else {
                transfer.ProcessingErrorCode = abortCode.Value;
            }
            transfer.ReceiveDataTimeoutId?.Cancel();
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
            MonitoringAgent?.OnReceiveDataAborted(transfer, abortCode);
        }

        public void Reset(ProtocolException causeOfReset)
        {
            if (causeOfReset == null)
            {
                throw new ArgumentNullException(nameof(causeOfReset));
            }
            PostCallbackSafely(() =>
            {
                // cancel all outgoing transfers.
                var endpoints = outgoingTransfers.GetEndpoints();
                foreach (var endpoint in endpoints)
                {
                    foreach (var transfer in outgoingTransfers.GetValues(endpoint))
                    {
                        transfer.SendCancellationHandle?.Cancel();
                        transfer.RetryBackoffTimeoutId?.Cancel();
                        transfer.ReceiveAckTimeoutId?.Cancel();
                        transfer.MessageSendCallback?.Invoke(causeOfReset);
                    }
                }
                outgoingTransfers.Clear();

                // cancel all receives
                endpoints = incomingTransfers.GetEndpoints();
                foreach (var endpoint in endpoints)
                {
                    foreach (var transfer in incomingTransfers.GetValues(endpoint))
                    {
                        transfer.ReceiveDataTimeoutId?.Cancel();
                        transfer.ReceiveBuffer.Dispose();
                    }
                }
                incomingTransfers.Clear();

                // clear endpoint ownership information
                EndpointInfoDatastore?.Clear();

                // cancel, but don't reset endpoint owner id
                endpointOwnerId = null;
                lastEndpointOwnerResetTimeoutId?.Cancel();
                lastEndpointOwnerResetTimeoutId = null;

                MonitoringAgent?.OnReset(causeOfReset);
            });
        }

        private void ResetEndpointOwnerId()
        {
            endpointOwnerId = ByteUtils.GenerateUuid();

            int resetPeriod = EndpointOwnerIdResetPeriod;
            if (resetPeriod < 1) resetPeriod = ReceiveTimeout;

            lastEndpointOwnerResetTimeoutId?.Cancel();
            lastEndpointOwnerResetTimeoutId = ScheduleTimeoutSafely(
                resetPeriod, () => ResetEndpointOwnerId());

            // eject all receives which have been in processed state for at
            // least receive timeout
            var endpoints = incomingTransfers.GetEndpoints();
            foreach (var endpoint in endpoints)
            {
                foreach (var transfer in incomingTransfers.GetValues(endpoint))
                {
                    var timeSpent = EventLoop.CurrentTimestamp - transfer.ProcessedAt;
                    if (transfer.Processed && timeSpent >= ReceiveTimeout)
                    {
                        incomingTransfers.Remove(endpoint, transfer.MessageId);
                        MonitoringAgent?.OnReceivedDataEvicted(transfer);
                    }
                }
            }
            MonitoringAgent?.OnEndpointOwnerIdReset(endpointOwnerId);
        }

        private void PostCallbackSafely(Action cb)
        {
            try
            {
                EventLoop.PostCallback(() =>
                {
                    try
                    {
                        cb.Invoke();
                    }
                    catch (Exception ex)
                    {
                        EventListener?.OnProcessingError("142ada19-cf8a-4f03-803c-d66a65bbf79c: " +
                            "error from executing unscheduled callback", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                EventListener?.OnProcessingError("50dbcfba-1148-41b0-838a-c255970880a6: " +
                    "error from submitting callback", ex);
            }
        }

        private CancellationHandle ScheduleTimeoutSafely(int millis, Action cb)
        {
            // unlike post callback, let error due to scheduling timeout
            // bubble up to caller.
            object timeoutId = EventLoop.ScheduleTimeout(millis, () =>
            {
                try
                {
                    cb.Invoke();
                }
                catch (Exception ex)
                {
                    EventListener?.OnProcessingError("903a2d64-b374-45c7-9c00-4b9bfb1433f2: " +
                        "error from executing scheduled callback", ex);
                }
            });
            var cancellationHandle = new CancellationHandle(() =>
            {
                try
                {
                    EventLoop.CancelTimeout(timeoutId);
                }
                catch (Exception ex)
                {
                    EventListener?.OnProcessingError("d30d0f3b-b0fd-4897-b3cd-3d3eaf6f35c3: " +
                        "error from cancelling scheduled callback", ex);
                }
            });
            return cancellationHandle;
        }
    }
}
