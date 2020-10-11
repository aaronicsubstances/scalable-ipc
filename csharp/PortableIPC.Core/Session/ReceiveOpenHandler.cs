using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PortableIPC.Core.Session
{
    /// <summary>
    /// Implemented with stop-and-wait flow control.
    /// </summary>
    public class ReceiveOpenHandler : ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;
        private readonly AbstractEventLoopApi _eventLoop;

        private bool _sendAckInProgress;
        private Exception _closeEx;

        public ReceiveOpenHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
            _eventLoop = sessionHandler.EndpointHandler.EventLoop;
        }

        public List<ProtocolDatagram> OpenRequestBuffer { get; } = new List<ProtocolDatagram>();

        public void Shutdown(Exception error)
        {
            _closeEx = error;
        }

        public bool ProcessReceive(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            // check opcode.
            if (message.OpCode != ProtocolDatagram.OpCodeOpen)
            {
                return false;
            }

            // check handler state
            if (_sendAckInProgress)
            {
                return false;
            }

            ProcessReceiveOpen(message, promiseCb);
            return true;
        }

        public bool ProcessSend(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options, 
            AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }

        private void ProcessReceiveOpen(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            ProtocolDatagram ack;
            if (_sessionHandler.SessionState == SessionState.Opening)
            {
                // check if sequence number suggests OPEN pdu has already been processed.
                if (message.SequenceNumber == _sessionHandler.LastMaxSeqReceived)
                {
                    // already received and passed to application layer.
                    // just send back benign acknowledgement.
                    ack = new ProtocolDatagram
                    {
                        OpCode = ProtocolDatagram.OpCodeOpenAck,
                        SequenceNumber = _sessionHandler.LastMaxSeqReceived,
                        SessionId = _sessionHandler.SessionId
                    };
                    _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, ack)
                        .Then(_ => HandleNoOpAckSuccess(promiseCb), error => HandleAckSendFailure(error, promiseCb));
                    return;
                }
            }
            else if (_sessionHandler.SessionState != SessionState.NotStarted)
            {
                return;
            }

            // check max open request buffer count.
            if (_sessionHandler.EndpointHandler.EndpointConfig.MaxOpenRequestPduCount > 0 &&
                OpenRequestBuffer.Count >= _sessionHandler.EndpointHandler.EndpointConfig.MaxOpenRequestPduCount)
            {
                var error = new Exception("Maximum limit on PDUs constituting an OPEN request has been reached");
                promiseCb.CompleteExceptionally(error);
                _sessionHandler.ProcessShutdown(error, false);
                return;
            }

            OpenRequestBuffer.Add(message);

            // time to send back acknowledgment.
            int ackSeqNr = message.SequenceNumber;
            ack = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeOpenAck,
                SequenceNumber = ackSeqNr,
                SessionId = _sessionHandler.SessionId
            };
            _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, ack)
                .Then(_ => HandleOpenAckSendSuccess(promiseCb),
                      error => HandleAckSendFailure(error, promiseCb));
            _sendAckInProgress = true;
        }

        private void HandleAckSendFailure(Exception error, AbstractPromiseCallback<VoidType> promiseCb)
        {
            _sessionHandler.PostSerially(() =>
            {
                promiseCb.CompleteExceptionally(error);
                _sessionHandler.ProcessShutdown(error, false);
            });
        }

        private VoidType HandleNoOpAckSuccess(AbstractPromiseCallback<VoidType> promiseCb)
        {
            promiseCb.CompleteSuccessfully(VoidType.Instance);
            return VoidType.Instance;
        }

        private VoidType HandleOpenAckSendSuccess(AbstractPromiseCallback<VoidType> promiseCb)
        {
            _sessionHandler.PostSerially(() =>
            {
                _sendAckInProgress = false;
                promiseCb.CompleteSuccessfully(VoidType.Instance);

                // check if handler is shutdown.
                if (_closeEx != null)
                {
                    return;
                }

                // save last sequence. NB: window size = 1.
                _sessionHandler.LastMaxSeqReceived = OpenRequestBuffer[OpenRequestBuffer.Count - 1].SequenceNumber;
                _sessionHandler.LastMinSeqReceived = _sessionHandler.LastMinSeqReceived;

                _sessionHandler.SessionState = SessionState.Opening;

                if (OpenRequestBuffer[OpenRequestBuffer.Count - 1].IsLastInOpenRequest != true)
                {
                    // keep waiting for last open request PDU.
                    return;
                }

                // Last OPEN pdu has arrived, so process session layer options.
                try
                {
                    ProcessOptions();

                    var openOptions = new Dictionary<string, List<string>>();
                    byte[] openData = RetrieveCurrentBufferData(openOptions);

                    _sessionHandler.SessionState = SessionState.OpenedForData;

                    // ready to pass on to application layer.
                    _eventLoop.PostCallback(() => _sessionHandler.OnOpenRequest(openData, openOptions));
                }
                catch (Exception error)
                {
                    _sessionHandler.ProcessShutdown(error, false);
                }
            });
            return VoidType.Instance;
        }

        private bool ProcessOptions()
        {
            // options are maximum pdu size, idle timeout, ack timeout, data window size, maximum retry count

            // TODO:
            // create dedicated class with endpoint config, session handler and openRequestBuffer.
            // to deal with options which must be supported regardless of endpoint configuration.

            var endPointConfig = _sessionHandler.EndpointHandler.EndpointConfig;
            var maxPduSizes = OpenRequestBuffer.Where(x => x.MaxPduSize != null)
                .SelectMany(x => x.MaxPduSize);
            var idleTimeouts = OpenRequestBuffer.Where(x => x.IdleTimeoutSecs != null)
                .SelectMany(x => x.IdleTimeoutSecs).ToList();
            var ackTimeouts = OpenRequestBuffer.Where(x => x.AckTimeoutSecs != null)
                .SelectMany(x => x.AckTimeoutSecs).ToList();
            var dataWindowSizes = OpenRequestBuffer.Where(x => x.DataWindowSize != null)
                .SelectMany(x => x.DataWindowSize).ToList();
            var maxRetryCounts = OpenRequestBuffer.Where(x => x.RetryCount != null)
                .SelectMany(x => x.RetryCount).ToList();

            throw new NotImplementedException();
        }

        private byte[] RetrieveCurrentBufferData(Dictionary<string, List<string>> optionsReceiver)
        {
            var memoryStream = new MemoryStream();
            foreach (var msg in OpenRequestBuffer)
            {
                if (msg.Options != null)
                {
                    foreach (var kvp in msg.Options)
                    {
                        optionsReceiver.Add(kvp.Key, kvp.Value);
                    }
                }
                memoryStream.Write(msg.DataBytes, msg.DataOffset, msg.DataLength);
            }
            memoryStream.Flush();
            return memoryStream.ToArray();
        }
    }
}
