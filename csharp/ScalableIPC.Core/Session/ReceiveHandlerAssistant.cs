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

                _sessionHandler.Log("b68d4dc2-52c0-4ffa-a395-82a49937a838", message,
                    "Received message from last received window. Responding with ack", 
                    "ack.seqNr", _sessionHandler.LastMaxSeqReceived);
                // ignore success and care only about failure.
                _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.RemoteEndpoint, ack)
                    .CatchCompose(e => HandleAckSendFailure(message, e));
                return;
            }
            else if (!ProtocolDatagram.IsReceivedWindowIdValid(message.WindowId, _sessionHandler.LastWindowIdReceived))
            {
                _sessionHandler.Log("ea7a501f-5b11-4548-8707-4f1dd6c66698", message,
                    "Rejecting unexpected window id");
                _sessionHandler.DiscardReceivedMessage(message);
                return;
            }

            // save message.
            if (!AddToCurrentWindow(message))
            {
                _sessionHandler.Log("2524f51b-7afe-402f-a214-9a82f955b6fb", message,
                    "Rejecting unexpected sequence number.");
                _sessionHandler.DiscardReceivedMessage(message);
                return;
            }

            _sessionHandler.Log("5507485a-7f67-411a-a33a-97a943a5cd89", message,
                "Successful received message into window.",
                "count", _currentWindow.Count);

            // send back ack
            var lastEffectiveSeqNr = GetLastPositionInSlidingWindow();
            var isWindowFull = IsCurrentWindowFull(lastEffectiveSeqNr);
            if (lastEffectiveSeqNr == -1)
            {
                _sessionHandler.Log("ee88b93c-c31d-4161-a75e-aa0522062905", message,
                    "Skipping ack response due to empty sliding window");
            }
            else
            {
                _sessionHandler.Log("83acb8c0-4252-47e2-9bb2-7af873c4dfff", message,
                    "Sending ack response for sliding window", 
                    "slidingWindowSize", lastEffectiveSeqNr + 1,
                    "windowFull", isWindowFull);
                var ack = new ProtocolDatagram
                {
                    SessionId = message.SessionId,
                    OpCode = AckOpCode,
                    WindowId = message.WindowId,
                    SequenceNumber = lastEffectiveSeqNr,
                    IsWindowFull = isWindowFull
                };
                _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.RemoteEndpoint, ack)
                    .ThenOrCatchCompose(_ => HandleAckSendSuccess(message), 
                        error => HandleAckSendFailure(message, error));
            }
        }

        private AbstractPromise<VoidType> HandleAckSendFailure(ProtocolDatagram message, Exception error)
        {
            _sessionHandler.PostIfNotClosed(() =>
            {
                if (_isComplete)
                {
                    _sessionHandler.Log("54823b3a-a4e2-4f91-97c8-27e658a1b07d", message,
                        "Ignoring ack send failure");
                    return;
                }
                else
                {
                    _sessionHandler.Log("f09fd1f8-b548-428e-a59a-01534fde8f0f", message,
                        "Failed to send ack. Shutting down...");
                    _isComplete = true;
                    _sessionHandler.ProcessShutdown(error, false);
                }
            });
            return _voidReturnPromise;
        }

        private AbstractPromise<VoidType> HandleAckSendSuccess(ProtocolDatagram message)
        {
            _sessionHandler.PostIfNotClosed(() =>
            {
                // check if ack send callback is coming in too late.
                if (_isComplete)
                {
                    _sessionHandler.Log("e36c8f41-1b0d-4c0c-9acd-2d7761c260c1", message,
                        "Ignoring ack send success callback");
                    return;
                }

                _sessionHandler.Log("fefce993-238f-499f-88a0-dff73e0bc5b7", message,
                    "About to process ack send success callback");

                int lastEffectiveSeqNr = GetLastPositionInSlidingWindow();
                if (!IsCurrentWindowFull(lastEffectiveSeqNr))
                {
                    _sessionHandler.Log("aca0970d-6032-4c9e-ab18-c89586cd6d2b", message,
                        "Window is not full so nothing to process");
                    // window is not yet full so keep on waiting for more data.
                    return;
                }

                _sessionHandler.Log("b3de3c42-0280-48ab-9334-a7ddac9e5100", message,
                    "Window is full");

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
