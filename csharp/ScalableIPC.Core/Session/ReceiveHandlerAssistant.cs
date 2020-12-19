using System;
using System.Collections.Generic;
using System.Linq;

namespace ScalableIPC.Core.Session
{
    public class ReceiveHandlerAssistant
    {
        private readonly IReferenceSessionHandler _sessionHandler;

        private readonly List<ProtocolDatagram> _currentWindow;
        private readonly List<ProtocolDatagram> _currentWindowGroup;
        private readonly List<long> _groupedWindowIds;

        public ReceiveHandlerAssistant(IReferenceSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;

            _currentWindow = new List<ProtocolDatagram>();
            _currentWindowGroup = new List<ProtocolDatagram>();
            _groupedWindowIds = new List<long>();
        }

        public Func<List<ProtocolDatagram>, bool> SuccessCallback { get; set; }
        public bool IsComplete { get; set; } = false;

        public void Cancel()
        {
            IsComplete = true;
        }

        public void OnReceive(ProtocolDatagram datagram)
        {
            if (datagram.WindowId == _sessionHandler.LastWindowIdReceived)
            {
                // already received and passed to application layer.
                // just send back repeat acknowledgement.
                var repeatAck = new ProtocolDatagram
                {
                    SessionId = _sessionHandler.SessionId,
                    OpCode = ProtocolDatagram.OpCodeDataAck,
                    WindowId = _sessionHandler.LastWindowIdReceived,
                    SequenceNumber = _sessionHandler.LastMaxSeqReceived,
                    Options = new ProtocolDatagramOptions
                    {
                        IsWindowFull = true
                    }
                };

                _sessionHandler.Log("b68d4dc2-52c0-4ffa-a395-82a49937a838", datagram,
                    "Received datagram from last received window. Responding with ack", 
                    "ack.seqNr", _sessionHandler.LastMaxSeqReceived);
                _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint, repeatAck, e =>
                {
                    // ignore success and care only about failure.
                    if (e != null)
                    {
                        HandleAckSendFailure(datagram, e);
                    }
                });
                return;
            }
            
            if (!ProtocolDatagram.IsReceivedWindowIdValid(datagram.WindowId, _sessionHandler.LastWindowIdReceived))
            {
                _sessionHandler.Log("ea7a501f-5b11-4548-8707-4f1dd6c66698", datagram,
                    "Rejecting unexpected window id");
                _sessionHandler.DiscardReceivedDatagram(datagram);
                return;
            }

            // save datagram into current window.
            if (!AddToCurrentWindow(_currentWindow, _sessionHandler.MaxReceiveWindowSize, datagram))
            {
                _sessionHandler.Log("2524f51b-7afe-402f-a214-9a82f955b6fb", datagram,
                    "Rejecting unexpected sequence number.");
                _sessionHandler.DiscardReceivedDatagram(datagram);
                return;
            }

            _sessionHandler.Log("5507485a-7f67-411a-a33a-97a943a5cd89", datagram,
                "Successful received datagram into window.",
                "count", _currentWindow.Count);

            // determine size of sliding window
            var lastEffectiveSeqNr = GetLastPositionInSlidingWindow(_currentWindow);
            if (lastEffectiveSeqNr == -1)
            {
                _sessionHandler.Log("ee88b93c-c31d-4161-a75e-aa0522062905", datagram,
                    "Skipping ack response due to empty sliding window");
                return;
            }

            if (IsCurrentWindowFull(_currentWindow, _sessionHandler.MaxReceiveWindowSize,
                   lastEffectiveSeqNr))
            {
                UpdateWindowGroup(datagram, lastEffectiveSeqNr);
                return;
            }

            _sessionHandler.Log("83acb8c0-4252-47e2-9bb2-7af873c4dfff", datagram,
                "Window is not full. Sending ack response for sliding window", 
                "slidingWindowSize", lastEffectiveSeqNr + 1);
            var ack = new ProtocolDatagram
            {
                SessionId = datagram.SessionId,
                OpCode = ProtocolDatagram.OpCodeDataAck,
                WindowId = datagram.WindowId,
                SequenceNumber = lastEffectiveSeqNr
            };
            _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint, ack, e =>
            {
                // ignore success and care only about failure.
                if (e != null)
                {
                    HandleAckSendFailure(datagram, e);
                }
            });
        }

        private void HandleAckSendFailure(ProtocolDatagram datagram, Exception error)
        {
            _sessionHandler.TaskExecutor.PostCallback(() =>
            {
                // check if ack send callback is coming in too late.
                if (IsComplete || _groupedWindowIds.Contains(datagram.WindowId))
                {
                    _sessionHandler.Log("54823b3a-a4e2-4f91-97c8-27e658a1b07d", datagram,
                        "Ignoring ack send failure");
                }
                else
                {
                    _sessionHandler.Log("f09fd1f8-b548-428e-a59a-01534fde8f0f", datagram,
                        "Failed to send ack. Disposing...");
                    IsComplete = true;
                    _sessionHandler.InitiateDispose(new SessionDisposedException(error));
                }
            });
        }

        private void UpdateWindowGroup(ProtocolDatagram datagram, int lastEffectiveSeqNr)
        {
            _sessionHandler.Log("b3de3c42-0280-48ab-9334-a7ddac9e5100", datagram,
                "Window is full");

            // Update window group in a way we can rollback.
            _currentWindowGroup.AddRange(_currentWindow.GetRange(0, lastEffectiveSeqNr + 1));

            // Check if window group is becoming too large, and fail if it is too much
            // for max datagram size.
            int cumulativeLength = _currentWindowGroup.Sum(t => t.ExpectedDatagramLength);
            if (cumulativeLength > ProtocolDatagram.MaxDatagramSize)
            {
                _sessionHandler.Log("bff758a4-f80a-48c1-ac28-8ce7ea36589e", datagram,
                   "Window group overflow!");
                IsComplete = true;
                _sessionHandler.InitiateDispose(new SessionDisposedException(false,
                    ProtocolDatagram.AbortCodeWindowGroupOverflow));
                return;
            }

            // Only pass up if last datagram in window group has been seen
            if (_currentWindow[lastEffectiveSeqNr].Options?.IsLastInWindowGroup == true)
            {
                bool processedSuccessfully = SuccessCallback.Invoke(_currentWindowGroup);
                if (!processedSuccessfully)
                {
                    _sessionHandler.Log("c0acd44d-12d4-4651-8095-62bb7fe96a44", datagram,
                       "Window group was not processed successfully. Rejecting...");

                    // rollback
                    var rejectItemCount = lastEffectiveSeqNr + 1;
                    _currentWindowGroup.RemoveRange(_currentWindowGroup.Count - rejectItemCount, rejectItemCount);
                    return;
                }

                IsComplete = true;
                _sessionHandler.Log("89d4c052-a99a-4e49-9116-9c80553ec594", datagram,
                   "Window group is full");
            }
            else
            {
                _sessionHandler.Log("3bdb8e9c-7795-480a-97ea-29b4923a8260", datagram,
                   "Window group is not yet full. Waiting for another window");
            }

            // Reset last window bounds and current window.
            _sessionHandler.LastWindowIdReceived = _currentWindow[0].WindowId;
            _sessionHandler.LastMaxSeqReceived = lastEffectiveSeqNr;
            _currentWindow.Clear();
            _groupedWindowIds.Add(_sessionHandler.LastWindowIdReceived);

            _sessionHandler.Log("beca0da4-750f-4a3d-a5df-b23ec5f3b330", datagram,
                "Sending ack response for full window");
            var ack = new ProtocolDatagram
            {
                SessionId = datagram.SessionId,
                OpCode = ProtocolDatagram.OpCodeDataAck,
                WindowId = datagram.WindowId,
                SequenceNumber = lastEffectiveSeqNr,
                Options = new ProtocolDatagramOptions
                {
                    IsWindowFull = true
                }
            };
            // ignore any send ack errors.
            _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint, ack, e => { });
        }

        internal static bool AddToCurrentWindow(List<ProtocolDatagram> currentWindow, int maxReceiveWindowSize,
            ProtocolDatagram datagram)
        {
            // ensure minimum value of 1 for max receive window size.
            if (maxReceiveWindowSize < 1)
            {
                maxReceiveWindowSize = 1;
            }

            // ensure enough capacity of current window for new datagram.
            if (datagram.SequenceNumber >= maxReceiveWindowSize)
            {
                return false;
            }

            // if window id is different, clear all entries if newer is greater.
            long? currentWindowId = currentWindow.Find(x => x != null)?.WindowId;
            if (currentWindowId != null)
            {
                if (datagram.WindowId > currentWindowId)
                {
                    currentWindow.Clear();
                }
                else if (datagram.WindowId < currentWindowId)
                {
                    // only accept greater window ids
                    return false;
                }
            }
            while (currentWindow.Count <= datagram.SequenceNumber)
            {
                currentWindow.Add(null);
            }

            // before inserting new datagram, clear any existing datagram with set last_in_window option
            // and its effects.
            if (datagram.Options?.IsLastInWindow == true)
            {
                for (int i = 0; i < currentWindow.Count; i++)
                {
                    if (currentWindow[i] != null)
                    {
                        if (currentWindow[i].Options?.IsLastInWindow == true)
                        {
                            currentWindow[i] = null;
                        }
                        else if (i > datagram.SequenceNumber)
                        {
                            currentWindow[i] = null;
                        }
                    }
                }
            }

            currentWindow[datagram.SequenceNumber] = datagram;
            return true;
        }

        internal static int GetLastPositionInSlidingWindow(List<ProtocolDatagram> currentWindow)
        {
            // sliding window here means the contiguous filled window starting at index 0.
            if (currentWindow.Count == 0 || currentWindow[0] == null)
            {
                // meaning sliding window is empty.
                return -1;
            }
            int firstNullIndex = currentWindow.FindIndex(x => x == null);
            if (firstNullIndex == -1)
            {
                // meaning sliding window equal to window size.
                return currentWindow.Count - 1;
            }
            return firstNullIndex - 1;
        }

        internal static bool IsCurrentWindowFull(List<ProtocolDatagram> currentWindow, int maxReceiveWindowSize,
            int lastPosInSlidingWindow)
        {
            // ensure minimum value of 1 for max receive window size.
            if (maxReceiveWindowSize < 1)
            {
                maxReceiveWindowSize = 1;
            }

            if (lastPosInSlidingWindow < 0)
            {
                return false;
            }
            if (currentWindow[lastPosInSlidingWindow].Options?.IsLastInWindow == true)
            {
                return true;
            }
            if (lastPosInSlidingWindow == maxReceiveWindowSize - 1)
            {
                return true;
            }
            return false;
        }
    }
}
