using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ScalableIPC.Core.Session
{
    public class ReceiveHandlerAssistant: IReceiveHandlerAssistant
    {
        private readonly IStandardSessionHandler _sessionHandler;
        private readonly List<long> _groupedWindowIds;

        public ReceiveHandlerAssistant(IStandardSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
            _groupedWindowIds = new List<long>();
        }

        public List<ProtocolDatagram> CurrentWindow { get; } = new List<ProtocolDatagram>();
        public List<ProtocolDatagram> CurrentWindowGroup { get; } = new List<ProtocolDatagram>();

        public Action<List<ProtocolDatagram>> DataCallback { get; set; }
        public Action<ProtocolOperationException> ErrorCallback { get; set; }
        public bool IsComplete { get; private set; } = false;

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

                /* fire and forget */
                _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint, 
                    _sessionHandler.LastAck, null, null);
                return;
            }

            // Reject unexpected window id
            if (!ProtocolDatagram.IsReceivedWindowIdValid(datagram.WindowId, _sessionHandler.LastWindowIdReceived))
            {
               _sessionHandler.OnDatagramDiscarded(datagram);
                return;
            }

            // save datagram into current window or reject unexpected sequence number.
            if (!AddToCurrentWindow(CurrentWindow, _sessionHandler.MaxWindowSize, datagram))
            {
                _sessionHandler.OnDatagramDiscarded(datagram);
                return;
            }

            // Successful received datagram into window.

            // determine size of sliding window
            var lastEffectiveSeqNr = GetLastPositionInSlidingWindow(CurrentWindow);
            if (lastEffectiveSeqNr == -1)
            {
                // empty sliding window. skip ack response
                return;
            }

            if (IsCurrentWindowFull(CurrentWindow, _sessionHandler.MaxWindowSize,
                   lastEffectiveSeqNr))
            {
                UpdateWindowGroup(datagram, lastEffectiveSeqNr);
                return;
            }

            // Window is not full, so send ack response for sliding window
            var ack = new ProtocolDatagram
            {
                SessionId = datagram.SessionId,
                OpCode = ProtocolDatagram.OpCodeDataAck,
                WindowId = datagram.WindowId,
                SequenceNumber = lastEffectiveSeqNr
            };
            /* fire and forget */
            _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint, ack, null, null);
        }

        private void UpdateWindowGroup(ProtocolDatagram datagram, int lastEffectiveSeqNr)
        {
            // Window is full

            // Update window group.
            CurrentWindowGroup.AddRange(CurrentWindow.GetRange(0, lastEffectiveSeqNr + 1));

            ProtocolOperationException processingError = null;

            // Check if window group is becoming too large, and fail if it is too much
            // for max datagram size.
            int cumulativeLength = CurrentWindowGroup.Sum(t => t.ExpectedDatagramLength);
            if (cumulativeLength > ProtocolDatagram.MaxDatagramSize)
            {
                IsComplete = true;
                processingError = new ProtocolOperationException(false,
                    ProtocolOperationException.ErrorCodeWindowGroupOverflow);
            }
            else
            {
                // Only pass up if last datagram in window group has been seen
                if (CurrentWindow[lastEffectiveSeqNr].Options?.IsLastInWindowGroup == true)
                {
                    // Window group is full
                    IsComplete = true;
                    try
                    {
                        DataCallback.Invoke(CurrentWindowGroup);
                    }
                    catch (ProtocolOperationException ex)
                    {
                        processingError = ex;
                    }
                    catch (Exception ex)
                    {
                        processingError = new ProtocolOperationException(ex);
                    }
                }
                else
                {
                    // Window group is not yet full, so wait for another window
                }
            }

            // Reset last window bounds and current window.
            _sessionHandler.LastWindowIdReceived = CurrentWindow[0].WindowId;
            CurrentWindow.Clear();
            _groupedWindowIds.Add(_sessionHandler.LastWindowIdReceived);

            // finally send ack response for full window
            // ignore any send ack errors.
            _sessionHandler.LastAck = new ProtocolDatagram
            {
                SessionId = datagram.SessionId,
                OpCode = ProtocolDatagram.OpCodeDataAck,
                WindowId = datagram.WindowId,
                SequenceNumber = lastEffectiveSeqNr,
                Options = new ProtocolDatagramOptions
                {
                    IsWindowFull = true,
                    MaxWindowSize = _sessionHandler.MaxWindowSize,
                    ErrorCode = processingError?.ErrorCode
                }
            };
            _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint, 
                _sessionHandler.LastAck, null, null);

            if (processingError != null)
            {
                ErrorCallback.Invoke(processingError);
            }
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
