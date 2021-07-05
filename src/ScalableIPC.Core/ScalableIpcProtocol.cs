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
    public class ScalableIpcProtocol: IScalableIpcProtocol
    {
        public const int MinimumMessageSizeLimit = 65_536;
        public const int MinimumPduSizeLimit = 512;

        private readonly EndpointStructuredDatastore<IncomingTransfer> incomingTransfers;

        public ScalableIpcProtocol()
        {
            EndpointOwnerId = ByteUtils.GenerateUuid();
            incomingTransfers = new EndpointStructuredDatastore<IncomingTransfer>();
        }

        public string EndpointOwnerId { get; }
        public int PduSizeLimit { get; set; }
        public int MessageSizeLimit { get; set; }
        public int MinRetryBackoffPeriod { get; set; }
        public int MaxRetryBackoffPeriod { get; set; }
        public int AckReceiveTimeout { get; set; }
        public int DataReceiveTimeout { get; set; }
        public int ProcessedMessageDisposalWaitTime { get; set; }
        public bool VaryMessageSourceIds { get; set; }
        public ScalableIpcProtocolListener EventListener { get; set; }
        public TransportApi UnderlyingTransport { get; set; }
        public EventLoopApi EventLoop { get; set; }

        public string BeginSend(GenericNetworkIdentifier remoteEndpoint,
            byte[] data, int offset, int length, Action<ProtocolOperationException> cb)
        {
            string messageId = ByteUtils.GenerateUuid();
            return messageId;
        }

        public void BeginReceive(GenericNetworkIdentifier remoteEndpoint,
            byte[] data, int offset, int length)
        {
            EventLoop.PostCallback(WrapCallbackForEventLoop(() =>
            {
                ProtocolDatagram pdu = ProtocolDatagram.Deserialize(data, offset, length);
                ValidatePdu(pdu);
                if (pdu.OpCode == ProtocolDatagram.OpCodeHeader)
                {
                    ProcessReceivedMessageHeaderPart(remoteEndpoint, pdu);
                }
                else if (pdu.OpCode == ProtocolDatagram.OpCodeData)
                {
                    ProcessReceivedMessageDataPart(remoteEndpoint, pdu);
                }
                else
                {
                    throw new Exception($"unexpected opcode: {pdu.OpCode}");
                }
            }));
        }

        public static void ValidatePdu(ProtocolDatagram pdu)
        {
            if (pdu.Version == 0)
            {
                throw new Exception();
            }
            if (pdu.MessageId.Trim('0').Length == 0)
            {
                throw new Exception();
            }
            if (pdu.MessageDestinationId != null && pdu.MessageDestinationId.Trim('0').Length == 0)
            {
                throw new Exception();
            }
            if (pdu.MessageSourceId != null && pdu.MessageSourceId.Trim('0').Length == 0)
            {
                throw new Exception();
            }
            if (pdu.Data != null && pdu.DataLength == 0)
            {
                throw new Exception();
            }
            if (pdu.OpCode == ProtocolDatagram.OpCodeHeader && pdu.MessageLength < 0)
            {
                throw new Exception();
            }
            if (pdu.OpCode == ProtocolDatagram.OpCodeData && pdu.SequenceNumber <= 0)
            {
                throw new Exception();
            }
        }

        private Action WrapCallbackForEventLoop(Action cb)
        {
            return () =>
            {
                try
                {
                    cb();
                }
                catch (Exception ex)
                {
                    EventListener?.OnProcessingError(ex);
                }
            };
        }

        private void ProcessReceivedMessageHeaderPart(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram pdu)
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
                    var lastAckBytes = transfer.LastAckSent.Serialize();
                    // ignore outcome of send.
                    UnderlyingTransport.BeginSend(remoteEndpoint, lastAckBytes, 0, lastAckBytes.Length, null);
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
                    }
                }
                incomingTransfers.Add(remoteEndpoint, pdu.MessageId, transfer);
                transfer.ReceiveTimeout = EventLoop.ScheduleTimeout(DataReceiveTimeout,
                    WrapCallbackForEventLoop(() => ProcessReceivedMessagePartTimeout(
                        remoteEndpoint, pdu.MessageId)));
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
                    var ackBytes = ack.Serialize();
                    // ignore outcome of send.
                    UnderlyingTransport.BeginSend(remoteEndpoint, ackBytes, 0, ackBytes.Length, null);
                }
                else if (transfer.ExpectedSequenceNumber != 0)
                {
                    if (transfer.ExpectedSequenceNumber == 1)
                    {
                        // send back again the last ack sent out.
                        var lastAckBytes = transfer.LastAckSent.Serialize();
                        // ignore outcome of send.
                        UnderlyingTransport.BeginSend(remoteEndpoint, lastAckBytes, 0, lastAckBytes.Length, null);
                    }
                    else
                    {
                        // discard
                    }
                }
                else if (pdu.MessageLength > MessageSizeLimit)
                {
                    var ack = new ProtocolDatagram
                    {
                        OpCode = ProtocolDatagram.OpCodeHeaderAck,
                        Version = ProtocolDatagram.ProtocolVersion1_0,
                        MessageId = transfer.MessageId,
                        MessageSourceId = transfer.MessageSrcId,
                        ErrorCode = ProtocolOperationException.ErrorCodeMessageTooLarge
                    };
                    var ackBytes = ack.Serialize();
                    // ignore outcome of send.
                    UnderlyingTransport.BeginSend(remoteEndpoint, ackBytes, 0, ackBytes.Length, null);
                }
                else
                {
                    // all is well.
                    transfer.BytesRemaining = pdu.MessageLength;
                        
                    // reset timeout
                    EventLoop.CancelTimeout(transfer.ReceiveTimeout);
                    transfer.ReceiveTimeout = EventLoop.ScheduleTimeout(DataReceiveTimeout,
                        WrapCallbackForEventLoop(() => ProcessReceivedMessagePartTimeout(
                            remoteEndpoint, pdu.MessageId)));

                    int dataLengthToUse = Math.Min(pdu.DataLength, transfer.BytesRemaining);
                    transfer.ReceiveBuffer.Write(pdu.Data, pdu.DataOffset, dataLengthToUse);
                    transfer.BytesRemaining -= dataLengthToUse;
                    if (transfer.BytesRemaining == 0)
                    {
                        // mark as processed and successful.
                        transfer.Processed = true;
                        EventLoop.CancelTimeout(transfer.ReceiveTimeout);
                        var messageBytes = transfer.ReceiveBuffer.ToArray();
                        transfer.ReceiveBuffer.Dispose();
                        EventListener?.OnMessageReceived(remoteEndpoint, pdu.MessageId,
                            messageBytes, 0, messageBytes.Length);
                        transfer.ExpirationTimeout = EventLoop.ScheduleTimeout(ProcessedMessageDisposalWaitTime,
                            WrapCallbackForEventLoop(() => ExpireProcessedMessageId(
                                remoteEndpoint, pdu.MessageId)));
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
                    var lastAckBytes = transfer.LastAckSent.Serialize();
                    // ignore outcome of send.
                    UnderlyingTransport.BeginSend(remoteEndpoint, lastAckBytes, 0, lastAckBytes.Length, null);
                }
            }
        }

        private void ProcessReceivedMessageDataPart(GenericNetworkIdentifier remoteEndpoint, ProtocolDatagram pdu)
        {
            IncomingTransfer transfer = incomingTransfers.Get(remoteEndpoint, pdu.MessageId, null);
            if (transfer == null)
            {
                throw new Exception("could not find existing incoming transfer from " +
                    $"{remoteEndpoint} for message {pdu.MessageId}");
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
                    var lastAckBytes = transfer.LastAckSent.Serialize();
                    // ignore outcome of send.
                    UnderlyingTransport.BeginSend(remoteEndpoint, lastAckBytes, 0, lastAckBytes.Length, null);
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
                    var ackBytes = ack.Serialize();
                    // ignore outcome of send.
                    UnderlyingTransport.BeginSend(remoteEndpoint, ackBytes, 0, ackBytes.Length, null);
                }
                else if (transfer.ExpectedSequenceNumber != pdu.SequenceNumber)
                {
                    if (transfer.ExpectedSequenceNumber == pdu.SequenceNumber + 1)
                    {
                        // send back again the last ack sent out.
                        var lastAckBytes = transfer.LastAckSent.Serialize();
                        // ignore outcome of send.
                        UnderlyingTransport.BeginSend(remoteEndpoint, lastAckBytes, 0, lastAckBytes.Length, null);
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
                    EventLoop.CancelTimeout(transfer.ReceiveTimeout);
                    transfer.ReceiveTimeout = EventLoop.ScheduleTimeout(DataReceiveTimeout,
                        WrapCallbackForEventLoop(() => ProcessReceivedMessagePartTimeout(
                            remoteEndpoint, pdu.MessageId)));

                    int dataLengthToUse = Math.Min(pdu.DataLength, transfer.BytesRemaining);
                    transfer.ReceiveBuffer.Write(pdu.Data, pdu.DataOffset, dataLengthToUse);
                    transfer.BytesRemaining -= dataLengthToUse;
                    if (transfer.BytesRemaining == 0)
                    {
                        // mark as processed and successful.
                        transfer.Processed = true;
                        EventLoop.CancelTimeout(transfer.ReceiveTimeout);
                        var messageBytes = transfer.ReceiveBuffer.ToArray();
                        transfer.ReceiveBuffer.Dispose();
                        EventListener?.OnMessageReceived(remoteEndpoint, pdu.MessageId,
                            messageBytes, 0, messageBytes.Length);
                        transfer.ExpirationTimeout = EventLoop.ScheduleTimeout(ProcessedMessageDisposalWaitTime,
                            WrapCallbackForEventLoop(() => ExpireProcessedMessageId(
                                remoteEndpoint, pdu.MessageId)));
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
                    UnderlyingTransport.BeginSend(remoteEndpoint, lastAckBytes, 0, lastAckBytes.Length, null);
                }
            }
        }

        private void ProcessReceivedMessagePartTimeout(GenericNetworkIdentifier remoteEndpoint, string messageId)
        {
            IncomingTransfer transfer = incomingTransfers.Get(remoteEndpoint, messageId, null);
            if (transfer == null || transfer.Processed) return;

            // mark as processed and failed.
            transfer.Processed = true;
            transfer.ProcessingErrorCode = ProtocolOperationException.ErrorCodeReceiveTimeout;
            EventLoop.CancelTimeout(transfer.ReceiveTimeout);
            transfer.ReceiveBuffer.Dispose();
            transfer.ExpirationTimeout = EventLoop.ScheduleTimeout(ProcessedMessageDisposalWaitTime,
                WrapCallbackForEventLoop(() => ExpireProcessedMessageId(
                    remoteEndpoint, messageId)));
        }

        private void ExpireProcessedMessageId(GenericNetworkIdentifier remoteEndpoint, string messageId)
        {
            incomingTransfers.Remove(remoteEndpoint, messageId);
        }

        public void Reset(ProtocolOperationException causeOfReset)
        {

        }
    }
}
