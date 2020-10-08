using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PortableIPC.Core.Session
{
    public class ReceiveHandler: ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;
        private readonly AbstractEventLoopApi _eventLoop;

        private List<ProtocolDatagram> _currentWindow;
        private int _lastMinSeqUsed = -1, _lastMaxSeqUsed = -1;
        private int _expectedMinSeq = 0;
        private bool _seqExpectedInMinRange = true;

        private AbstractPromiseCallback<VoidType> _pendingPromiseCb;
        private bool _waitingForAckSendConfirmation = false;

        public ReceiveHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
            _eventLoop = sessionHandler.EndpointHandler.EventLoop;
        }

        public void Shutdown(Exception error, bool timeout)
        {
            if (_waitingForAckSendConfirmation)
            {
                if (error == null)
                {
                    if (timeout)
                    {
                        error = new Exception("Session timed out");
                    }
                    else
                    {
                        error = new Exception("Session closed");
                    }
                }
                _pendingPromiseCb?.CompleteExceptionally(error);
                _waitingForAckSendConfirmation = false;
            }
            _currentWindow?.Clear();
        }

        public bool ProcessErrorReceive()
        {
            // deal with error receipts only when current window is defined.
            if (_sessionHandler.IsOpened)
            {
                // get first of unfilled positions.
                int firstNullIndex = _currentWindow.FindIndex(x => x == null);
                if (firstNullIndex != -1)
                {
                    // send negative ack for position just before first unfilled.
                    if (firstNullIndex > 0)
                    {
                        int nackSeq = _currentWindow[firstNullIndex - 1].SequenceNumber;
                        var ack = new ProtocolDatagram
                        {
                            OpCode = ProtocolDatagram.OpCodeAck,
                            SequenceNumber = nackSeq,
                            SessionId = _sessionHandler.SessionId
                        };
                        // only care about failures.
                        _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, ack)
                            .Then<VoidType>(null, HandleAckSendFailure);
                    }
                }
            }
            return true;
        }

        public bool ProcessReceive(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            if (message.SequenceNumber < _expectedMinSeq)
            {
                return false;
            }
            if (_seqExpectedInMinRange && message.SequenceNumber >= ProtocolDatagram.SequenceFirstCrossOverLimit)
            {
                return false;
            }
            if (message.OpCode == ProtocolDatagram.OpCodeClose || message.OpCode == ProtocolDatagram.OpCodeError)
            {
                Exception error = null;
                if (message.OpCode == ProtocolDatagram.OpCodeError)
                {
                    error = new Exception(message.GetFormattedErrorDescription());
                }
                _sessionHandler.ProcessShutdown(error, false);
                _pendingPromiseCb.CompleteSuccessfully(VoidType.Instance);
                return true;
            }
            if (_waitingForAckSendConfirmation)
            {
                return false;
            }
            if (_sessionHandler.IsOpened)
            {
                if (message.OpCode == ProtocolDatagram.OpCodeData)
                {
                    ProcessDataReceipt(message, promiseCb);
                    return true;
                }
                return false;
            }
            else
            {
                if (message.OpCode == ProtocolDatagram.OpCodeOpen)
                {
                    ProcessOpenRequest(message, promiseCb);
                    return true;
                }
                return false;
            }
        }

        public bool ProcessSend(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }

        public bool ProcessSendData(byte[] rawData, AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }

        private void ProcessOpenRequest(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            var ack = new ProtocolDatagram
            {
                OpCode = ProtocolDatagram.OpCodeOpenAck,
                SequenceNumber = message.SequenceNumber,
                SessionId = _sessionHandler.SessionId
            };

            // check if sequence number suggests data pdu has already been processed.
            if (message.SequenceNumber >= _lastMinSeqUsed && message.SequenceNumber <= _lastMaxSeqUsed)
            {
                // already received and passed to application layer.
                // just send back benign acknowledgement.
                _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, ack)
                    .Then(HandleNoOpAckSuccess, HandleAckSendFailure);
                _waitingForAckSendConfirmation = true;
                _pendingPromiseCb = promiseCb;
                return;
            }

            // examine open request parameters and set in ack if they can be honoured.

            // time to send back acknowledgment.
            _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, ack)
                .Then(_ => HandleOpenAckSendSuccess(message), HandleAckSendFailure);
            _waitingForAckSendConfirmation = true;
            _pendingPromiseCb = promiseCb;
        }

        private VoidType HandleOpenAckSendSuccess(ProtocolDatagram openRequest)
        {
            _sessionHandler.PostSeriallyIfNotClosed(() =>
            {
                _waitingForAckSendConfirmation = false;

                // reset current window and remember old window bounds
                _lastMinSeqUsed = _lastMaxSeqUsed = openRequest.SequenceNumber;

                ResetCurrentWindow();
                _expectedMinSeq = _lastMaxSeqUsed + 1;
                _seqExpectedInMinRange = false;

                _sessionHandler.IsOpened = true;

                _pendingPromiseCb.CompleteSuccessfully(VoidType.Instance);

                // ready to pass on to application layer.
                _eventLoop.PostCallback(() => _sessionHandler.OnOpenReceived(openRequest));
            });
            return VoidType.Instance;
        }

        private void ProcessDataReceipt(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            // check if sequence number suggests data pdu has already been processed.
            if (message.SequenceNumber >= _lastMinSeqUsed && message.SequenceNumber <= _lastMaxSeqUsed)
            {
                // already received and passed to application layer.
                // just send back benign acknowledgement.
                var ack = new ProtocolDatagram
                {
                    OpCode = ProtocolDatagram.OpCodeAck,
                    SequenceNumber = _lastMaxSeqUsed,
                    SessionId = _sessionHandler.SessionId
                };
                _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, ack)
                    .Then(HandleNoOpAckSuccess, HandleAckSendFailure);
                _waitingForAckSendConfirmation = true;
                _pendingPromiseCb = promiseCb;
                return;
            }

            // ensure that if incoming message is added to current window,
            // sequence numbers will still be valid.
            var seqNrs = new List<int>();
            int positionInWindow = message.SequenceNumber % _currentWindow.Count;
            for (int i = 0; i < _currentWindow.Count; i++)
            {
                if (i == positionInWindow)
                {
                    seqNrs.Add(message.SequenceNumber);
                }
                else
                {
                    var msg = _currentWindow[i];
                    if (msg != null)
                    {
                        seqNrs.Add(msg.SequenceNumber);
                    }
                }
            }
            if (!ProtocolDatagram.ValidateSequenceNumbers(seqNrs))
            {
                _sessionHandler.DiscardReceivedMessage(message, promiseCb);
                return;
            }

            // insert new message and deal with last_in_window option by
            // removing any previous last_in_window
            if (message.IsLastInWindow == true)
            {
                for (int i = 0; i < _currentWindow.Count; i++)
                {
                    if (_currentWindow[i] != null)
                    {
                        if (_currentWindow[i].IsLastInWindow == true)
                        {
                            _currentWindow[i] = null;
                        }
                        else if (i > positionInWindow)
                        {
                            _currentWindow[i] = null;
                        }
                    }
                }
            }
            _currentWindow[positionInWindow] = message;

            if (IsCurrentWindowFull())
            {
                // time to send back acknowledgment.
                int lastEffectivePosition = GetLastEffectiveWindowPosition();
                int maxSeqNr = _currentWindow[lastEffectivePosition].SequenceNumber;
                var ack = new ProtocolDatagram
                {
                    OpCode = ProtocolDatagram.OpCodeAck,
                    SequenceNumber = maxSeqNr,
                    SessionId = _sessionHandler.SessionId
                };
                _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, ack)
                    .Then(HandleDataAckSendSuccess, HandleAckSendFailure);
                _waitingForAckSendConfirmation = true;
                _pendingPromiseCb = promiseCb;
            }
            else
            {
                promiseCb.CompleteSuccessfully(VoidType.Instance);
            }
        }

        private void HandleAckSendFailure(Exception error)
        {
            _sessionHandler.PostSeriallyIfNotClosed(() =>
            {
                _sessionHandler.ProcessShutdown(error, false);
            });
        }

        private VoidType HandleNoOpAckSuccess(VoidType _)
        {
            _sessionHandler.PostSeriallyIfNotClosed(() =>
            {
                _waitingForAckSendConfirmation = false;
                _pendingPromiseCb.CompleteSuccessfully(VoidType.Instance);
            });
            return VoidType.Instance;
        }

        private VoidType HandleDataAckSendSuccess(VoidType _)
        {
            _sessionHandler.PostSeriallyIfNotClosed(() =>
            {
                _waitingForAckSendConfirmation = false;

                byte[] currentWindowData = RetrieveCurrentWindowData();

                // reset current window and remember old window bounds
                _lastMinSeqUsed = _currentWindow[0].SequenceNumber;
                int lastEffectivePosition = GetLastEffectiveWindowPosition();
                _lastMaxSeqUsed = _currentWindow[lastEffectivePosition].SequenceNumber;

                ResetCurrentWindow();
                if (_lastMaxSeqUsed >= ProtocolDatagram.SequenceSecondCrossOverLimit)
                {
                    _expectedMinSeq = 0;
                    _seqExpectedInMinRange = true;
                }
                else
                {
                    _expectedMinSeq = _lastMaxSeqUsed + 1;
                    _seqExpectedInMinRange = false;
                }

                _pendingPromiseCb.CompleteSuccessfully(VoidType.Instance);

                // ready to pass on to application layer.
                _eventLoop.PostCallback(() => _sessionHandler.OnDataReceived(currentWindowData, 0,
                    currentWindowData.Length));
            });
            return VoidType.Instance;
        }

        private void ResetCurrentWindow()
        {
            _currentWindow = new List<ProtocolDatagram>();
            for (int i = 0; i < _sessionHandler.WindowSize; i++)
            {
                _currentWindow.Add(null);
            }
        }

        private bool IsCurrentWindowFull()
        {
            bool isFull = true;
            foreach (var msg in _currentWindow)
            {
                if (msg == null)
                {
                    isFull = false;
                    break;
                }
                if (msg.IsLastInWindow == true)
                {
                    break;
                }
            }
            return isFull;
        }

        private byte[] RetrieveCurrentWindowData()
        {
            var memoryStream = new MemoryStream();
            foreach (var msg in _currentWindow)
            {
                memoryStream.Write(msg.DataBytes, msg.DataOffset, msg.DataLength);
                if (msg.IsLastInWindow == true)
                {
                    break;
                }
            }
            memoryStream.Flush();
            return memoryStream.ToArray();
        }

        private int GetLastEffectiveWindowPosition()
        {
            int lastEffectivePosition = _currentWindow.FindIndex(x => x?.IsLastInWindow == true);
            if (lastEffectivePosition == -1)
            {
                lastEffectivePosition = _currentWindow.Count - 1;
            }
            return lastEffectivePosition;
        }
    }
}
