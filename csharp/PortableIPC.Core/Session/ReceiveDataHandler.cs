using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PortableIPC.Core.Session
{
    public class ReceiveDataHandler: ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;
        private readonly AbstractEventLoopApi _eventLoop;

        private int _currentWindowId; // used to ignore late acknowledgment send callbacks.

        public ReceiveDataHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
            _eventLoop = sessionHandler.EndpointHandler.EventLoop;
        }

        public List<ProtocolDatagram> CurrentWindow { get; set; }

        public void Shutdown(Exception error)
        {
            // nothing to do.
        }

        public bool ProcessReceive(ProtocolDatagram message)
        {
            // check op code
            if (message.OpCode != ProtocolDatagram.OpCodeData)
            {
                return false;
            }

            ProcessDataReceipt(message);
            return true;
        }

        public bool ProcessSend(ProtocolDatagram message, PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options, 
            PromiseCompletionSource<VoidType> promiseCb)
        {
            return false;
        }

        private void ProcessDataReceipt(ProtocolDatagram message)
        {
            // check session state
            if (_sessionHandler.SessionState != SessionState.OpenedForData)
            {
                return;
            }

            if (CurrentWindow == null)
            {
                ResetCurrentWindow();
            }

            ProtocolDatagram ack;
            // check if sequence number suggests data pdu has already been processed.
            if (message.SequenceNumber >= _sessionHandler.LastMinSeqReceived && 
                message.SequenceNumber <= _sessionHandler.LastMaxSeqReceived)
            {
                // already received and passed to application layer.
                // just send back benign acknowledgement.
                ack = new ProtocolDatagram
                {
                    OpCode = ProtocolDatagram.OpCodeAck,
                    SequenceNumber = _sessionHandler.LastMaxSeqReceived,
                    SessionId = _sessionHandler.SessionId
                };
                _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, ack)
                    .Then<VoidType>(null, HandleAckSendFailure);
                return;
            }

            // ensure that sequence numbers remain valid with addition of incoming message.
            var seqNrs = new List<int>();
            int positionInWindow = message.SequenceNumber % CurrentWindow.Count;
            for (int i = 0; i < CurrentWindow.Count; i++)
            {
                if (i == positionInWindow)
                {
                    seqNrs.Add(message.SequenceNumber);
                }
                else
                {
                    var msg = CurrentWindow[i];
                    if (msg != null)
                    {
                        seqNrs.Add(msg.SequenceNumber);
                    }
                }
            }
            if (!ProtocolDatagram.ValidateSequenceNumbers(seqNrs))
            {
                _sessionHandler.DiscardReceivedMessage(message);
                return;
            }

            // before inserting new message, clear any existing message with set last_in_window option
            // and its effects.
            if (message.IsLastInDataWindow == true)
            {
                for (int i = 0; i < CurrentWindow.Count; i++)
                {
                    if (CurrentWindow[i] != null)
                    {
                        if (CurrentWindow[i].IsLastInDataWindow == true)
                        {
                            CurrentWindow[i] = null;
                        }
                        else if (i > positionInWindow)
                        {
                            CurrentWindow[i] = null;
                        }
                    }
                }
            }
            CurrentWindow[positionInWindow] = message;

            // Attempt to acknowledge each received message by the sequence number corresponding
            // to the last position in the currently filled sliding window.
            int lastPositionToAck = GetLastPositionInSlidingWindow();
            if (lastPositionToAck == -1)
            {
                // nothing to acknowledge.
                return;
            }

            // time to send back acknowledgment.
            int ackSeqNr = CurrentWindow[lastPositionToAck].SequenceNumber;
            ack = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeAck,
                SequenceNumber = ackSeqNr,
                SessionId = _sessionHandler.SessionId
            };
            int windowIdSnapshot = _currentWindowId;
            _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, ack)
                .Then(_ => HandleDataAckSendSuccess(windowIdSnapshot), HandleAckSendFailure);
        }

        private void HandleAckSendFailure(Exception error)
        {
            _sessionHandler.PostSeriallyIfNotClosed(() =>
            {
                _sessionHandler.ProcessShutdown(error, false);
            });
        }

        private VoidType HandleDataAckSendSuccess(int windowIdSnapshot)
        {
            _sessionHandler.PostSeriallyIfNotClosed(() =>
            {
                // check if ack send callback is coming in too late.
                if (CurrentWindow == null || _currentWindowId != windowIdSnapshot)
                {
                    return;
                }

                int lastEffectiveWindowPos = GetLastPositionInSlidingWindow();
                if (!IsCurrentWindowFull(lastEffectiveWindowPos))
                {
                    // window is not yet full so keep on waiting for more data.
                    return;
                }

                // Window is full.

                // save window data and bounds before reseting current window.
                _sessionHandler.LastMinSeqReceived = CurrentWindow[0].SequenceNumber;
                _sessionHandler.LastMaxSeqReceived = CurrentWindow[lastEffectiveWindowPos].SequenceNumber;

                var dataOptions = new Dictionary<string, List<string>>();
                byte[] currentWindowData = RetrieveCurrentWindowData(dataOptions);

                // invalidate subsequent ack send confirmations for this current window instance.
                CurrentWindow = null;

                // ready to pass on to application layer.
                _eventLoop.PostCallback(() => _sessionHandler.OnDataReceived(currentWindowData, dataOptions));
            });
            return VoidType.Instance;
        }

        private void ResetCurrentWindow()
        {
            _currentWindowId++;
            CurrentWindow = new List<ProtocolDatagram>();
            for (int i = 0; i < _sessionHandler.DataWindowSize; i++)
            {
                CurrentWindow.Add(null);
            }
        }

        private byte[] RetrieveCurrentWindowData(Dictionary<string, List<string>> dataOptionsReceiver)
        {
            var memoryStream = new MemoryStream();
            foreach (var msg in CurrentWindow)
            {
                if (msg.Options != null)
                {
                    foreach (var kvp in msg.Options)
                    {
                        dataOptionsReceiver.Add(kvp.Key, kvp.Value);
                    }
                }
                memoryStream.Write(msg.DataBytes, msg.DataOffset, msg.DataLength);
                if (msg.IsLastInDataWindow == true)
                {
                    break;
                }
            }
            memoryStream.Flush();
            return memoryStream.ToArray();
        }

        private int GetLastPositionInSlidingWindow()
        {
            // sliding window here means the contiguous filled window starting at index 0.
            int firstNonNullIndex = CurrentWindow.FindIndex(x => x == null);
            if (firstNonNullIndex == -1)
            {
                // meaning sliding window equal to window size.
                return CurrentWindow.Count - 1;
            }
            if (firstNonNullIndex == 0)
            {
                // meaning sliding window is empty.
                return -1;
            }
            return firstNonNullIndex - 1;
        }

        private bool IsCurrentWindowFull(int lastPosInSlidingWindow)
        {
            if (lastPosInSlidingWindow < 0)
            {
                return false;
            }
            if (lastPosInSlidingWindow == CurrentWindow.Count - 1)
            {
                return true;
            }
            return CurrentWindow[lastPosInSlidingWindow].IsLastInDataWindow == true;
        }
    }
}
