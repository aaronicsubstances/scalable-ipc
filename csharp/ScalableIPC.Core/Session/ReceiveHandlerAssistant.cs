using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace ScalableIPC.Core.Session
{
    public class ReceiveHandlerAssistant
    {
        private readonly ISessionHandler _sessionHandler;
        private readonly AbstractPromise<VoidType> _voidReturnPromise;

        private readonly List<ProtocolDatagram> _currentWindow;
        private long? _currentWindowId;
        private bool _isComplete;

        public ReceiveHandlerAssistant(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
            _voidReturnPromise = _sessionHandler.EndpointHandler.PromiseApi.Resolve(VoidType.Instance);

            _currentWindow = new List<ProtocolDatagram>();
            _currentWindowId = null;
            _isComplete = false;
        }

        public byte AckOpCode { get; set; }
        public Action<List<ProtocolDatagram>> SuccessCallback { get; set; }

        public void OnReceive(ProtocolDatagram message)
        {
            if (message.WindowId == _sessionHandler.LastWindowIdReceived)
            {
                // already received and passed to application layer.
                // just send back benign acknowledgement.
                var ack = new ProtocolDatagram
                {
                    SessionId = _sessionHandler.SessionId,
                    OpCode = AckOpCode,
                    WindowId = _sessionHandler.LastWindowIdReceived,
                    SequenceNumber = _sessionHandler.LastMaxSeqReceived,
                    IsWindowFull = true
                };
                // ignore success and care only about failure.
                _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.RemoteEndpoint, ack)
                    .CatchCompose(HandleAckSendFailure);
                return;
            }
            else if (!ProtocolDatagram.IsReceivedWindowIdValid(message.WindowId, _sessionHandler.LastWindowIdReceived))
            {
                _sessionHandler.DiscardReceivedMessage(message);
                return;
            }

            // save message.
            if (!AddToCurrentWindow(message))
            {
                _sessionHandler.DiscardReceivedMessage(message);
                return;
            }

            // send back ack
            var lastEffectiveSeqNr = GetLastPositionInSlidingWindow();
            var isWindowFull = IsCurrentWindowFull(lastEffectiveSeqNr);
            if (lastEffectiveSeqNr != -1)
            {
                var ack = new ProtocolDatagram
                {
                    SessionId = message.SessionId,
                    OpCode = AckOpCode,
                    WindowId = message.WindowId,
                    SequenceNumber = lastEffectiveSeqNr,
                    IsWindowFull = isWindowFull
                };
                _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.RemoteEndpoint, ack)
                    .ThenOrCatchCompose(HandleAckSendSuccess, HandleAckSendFailure);
            }
        }

        private AbstractPromise<VoidType> HandleAckSendFailure(Exception error)
        {
            _sessionHandler.PostIfNotClosed(() =>
            {
                if (!_isComplete)
                {
                    _isComplete = true;
                    _sessionHandler.ProcessShutdown(error, false);
                }
            });
            return _voidReturnPromise;
        }

        private AbstractPromise<VoidType> HandleAckSendSuccess(VoidType _)
        {
            _sessionHandler.PostIfNotClosed(() =>
            {
                // check if ack send callback is coming in too late.
                if (_isComplete)
                {
                    return;
                }

                int lastEffectiveSeqNr = GetLastPositionInSlidingWindow();
                if (!IsCurrentWindowFull(lastEffectiveSeqNr))
                {
                    // window is not yet full so keep on waiting for more data.
                    return;
                }

                // Window is full.
                // invalidate subsequent ack send confirmations for this current window instance.
                _isComplete = true;

                // Reset last window bounds.
                _sessionHandler.LastWindowIdReceived = _currentWindow[0].WindowId;
                _sessionHandler.LastMaxSeqReceived = lastEffectiveSeqNr;

                SuccessCallback.Invoke(_currentWindow);
            });
            return _voidReturnPromise;
        }

        private bool AddToCurrentWindow(ProtocolDatagram message)
        {
            // if window id is different, clear all entries.
            if (_currentWindowId != null && _currentWindowId != message.WindowId)
            {
                _currentWindow.Clear();
            }

            // ensure enough capacity of current window for new message.
            if (message.SequenceNumber >= _sessionHandler.MaxReceiveWindowSize)
            {
                return false;
            }
            while (_currentWindow.Count <= message.SequenceNumber)
            {
                _currentWindow.Add(null);
            }

            // before inserting new message, clear any existing message with set last_in_window option
            // and its effects.
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
                        else if (i > message.SequenceNumber)
                        {
                            _currentWindow[i] = null;
                        }
                    }
                }
            }

            _currentWindow[message.SequenceNumber] = message;
            _currentWindowId = message.WindowId;
            return true;
        }

        private int GetLastPositionInSlidingWindow()
        {
            // sliding window here means the contiguous filled window starting at index 0.
            if (_currentWindow.Count == 0 || _currentWindow[0] == null)
            {
                // meaning sliding window is empty.
                return -1;
            }
            int firstNullIndex = _currentWindow.FindIndex(x => x == null);
            if (firstNullIndex == -1)
            {
                // meaning sliding window equal to window size.
                return _currentWindow.Count - 1;
            }
            return firstNullIndex - 1;
        }

        private bool IsCurrentWindowFull(int lastPosInSlidingWindow)
        {
            if (lastPosInSlidingWindow < 0)
            {
                return false;
            }
            if (_currentWindow[lastPosInSlidingWindow].IsLastInWindow == true)
            {
                return true;
            }
            if (lastPosInSlidingWindow == _sessionHandler.MaxReceiveWindowSize - 1)
            {
                return true;
            }
            return false;
        }
    }
}
