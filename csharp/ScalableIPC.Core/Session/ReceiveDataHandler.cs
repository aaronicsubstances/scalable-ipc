using ScalableIPC.Core.Session.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScalableIPC.Core.Session
{
    public class ReceiveDataHandler : ISessionStateHandler
    {
        private readonly IStandardSessionHandler _sessionHandler;

        public ReceiveDataHandler(IStandardSessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public bool SendInProgress => false;

        public List<ProtocolDatagram> CurrentWindow { get; } = new List<ProtocolDatagram>();
        public List<ProtocolDatagram> CurrentWindowGroup { get; } = new List<ProtocolDatagram>();
        public bool AutomaticTransitionToOpenStateBlocked { get; set; }

        public void Dispose(ProtocolOperationException cause)
        {
            // nothing to do.
        }

        public bool ProcessReceive(ProtocolDatagram datagram)
        {
            if (datagram.OpCode != ProtocolDatagram.OpCodeData)
            {
                return false;
            }

            OnReceiveRequest(datagram);
            return true;
        }

        private void OnReceiveRequest(ProtocolDatagram datagram)
        {
            // reject quickly if forbidden to receive in opening state.
            if (_sessionHandler.State == SessionState.Opening &&
                _sessionHandler.ReceiveDataForbiddenDuringOpeningState)
            {
                // ignore any send ack errors.
                var rejectionAck = new ProtocolDatagram
                {
                    SessionId = datagram.SessionId,
                    OpCode = ProtocolDatagram.OpCodeDataAck,
                    WindowId = datagram.WindowId,
                    SequenceNumber = datagram.SequenceNumber,
                    Options = new ProtocolDatagramOptions
                    {
                        ErrorCode = ProtocolOperationException.ErrorCodeReceiveForbiddenInOpeningState
                    }
                };
                _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint,
                    rejectionAck, null, null);
                return;
            }

            // Reject unexpected window id
            if (!ProtocolDatagram.IsReceivedWindowIdValid(datagram.WindowId, _sessionHandler.LastWindowIdReceived))
            {
                // before rejecting very important: check if datagram is for last processed window.
                if (_sessionHandler.LastAck != null && datagram.WindowId == _sessionHandler.LastAck.WindowId)
                {
                    // already received and passed to application layer.
                    // just send back repeat acknowledgement.

                    /* fire and forget */
                    _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint,
                        _sessionHandler.LastAck, null, null);
                }
                else
                {
                    _sessionHandler.OnDatagramDiscarded(datagram);
                }
                return;
            }

            // validate session state in which PDU has been received.
            // but after validating window id, so that acks can be repeatedly returned
            // for incoming PDUs which have already been processed, regardless of state.
            if (_sessionHandler.State >= SessionState.Closing)
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

            // Successfully received datagram into window.
            _sessionHandler.LastWindowIdReceived = datagram.WindowId;

            // determine whether to transition to opened state automatically.
            if (_sessionHandler.State == SessionState.Opening)
            {
                if (!AutomaticTransitionToOpenStateBlocked)
                {
                    // fetch special send datagram option, and determine whether to
                    // proceed or skip transition to open state.
                    if (datagram.Options?.SkipDataExchangeProhibitionsDueToOpeningState == true)
                    {
                        TransitionToOpenState();
                    }
                    else
                    {
                        AutomaticTransitionToOpenStateBlocked = true;
                    }
                }
            }

            // reset current window if requested.
            if (datagram.Options?.IsFirstInWindowGroup == true)
            {
                CurrentWindowGroup.Clear();
            }

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
                UpdateWindowGroup(lastEffectiveSeqNr);
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

        private void TransitionToOpenState()
        {
            _sessionHandler.State = SessionState.Opened;
            _sessionHandler.OpenedByReceive = true;
            _sessionHandler.CancelOpenTimeout();
            _sessionHandler.ScheduleEnquireLinkEvent(true);
            _sessionHandler.OnOpenSuccess(true);
        }

        private void UpdateWindowGroup(int lastEffectiveSeqNr)
        {
            // Window is full

            var lastDatagramInWindow = CurrentWindow[lastEffectiveSeqNr];

            ProtocolOperationException processingError = null;

            // Prevent a window group from being received in opening state. 
            // This enables networks to abort any sessions in opening state if necessary, without
            // fear of leaving application layer side effects around.

            // NB: IsLastInWindowGroup option only applies if IsLastInWindow was set.
            // IsLastInWindow may not be set if the remote's window size is larger than
            // the local window size.

            // Ensure that window group contains more than 1 window in opening state.
            if (_sessionHandler.State == SessionState.Opening &&
                lastDatagramInWindow.Options?.IsLastInWindow == true &&
                lastDatagramInWindow.Options?.IsLastInWindowGroup == true &&
                CurrentWindowGroup.Count == 0)
            {
                processingError = new ProtocolOperationException(ProtocolOperationException.ErrorCodeWindowGroupNotReceivableInOpeningState);
            }

            // Only pass up if last datagram in window group has been seen
            if (processingError == null)
            {
                // Update window group.
                CurrentWindowGroup.AddRange(CurrentWindow.GetRange(0, lastEffectiveSeqNr + 1));

                // Check if window group is becoming too large, and fail if it is too much
                // for max datagram size.
                int cumulativeLength = CurrentWindowGroup.Sum(t => t.ExpectedDatagramLength);
                if (cumulativeLength > ProtocolDatagram.MaxDatagramSize)
                {
                    processingError = new ProtocolOperationException(ProtocolOperationException.ErrorCodeWindowGroupOverflow);
                    _sessionHandler.OnReceiveError(processingError);

                    // reset current window group.
                    CurrentWindowGroup.Clear();
                }
            }

            if (processingError == null)
            {
                if (lastDatagramInWindow.Options?.IsLastInWindow == true &&
                    lastDatagramInWindow.Options?.IsLastInWindowGroup == true)
                {
                    // Window group is full.

                    try
                    {
                        ProcessAndPassCurrentWindowGroupToApplicationLayer();
                    }
                    catch (Exception ex)
                    {
                        if (ex is ProtocolOperationException protEx)
                        {
                            processingError = protEx;
                            _sessionHandler.OnReceiveError(protEx);
                        }
                        else
                        {
                            processingError = new ProtocolOperationException(ex);
                            _sessionHandler.OnReceiveError(processingError);
                        }
                    }
                    finally
                    {
                        // reset current window group.
                        CurrentWindowGroup.Clear();
                    }
                }
                else
                {
                    // Window group is not yet full, so wait for another window
                }
            }

            // reset blocked state on error, so as to determine decision to transition afresh
            // for next window.
            if (_sessionHandler.State == SessionState.Opening && processingError != null)
            {
                AutomaticTransitionToOpenStateBlocked = false;
            }

            // Reset current window.
            CurrentWindow.Clear();

            // finally send ack response for full window
            // ignore any send ack errors.
            _sessionHandler.LastAck = new ProtocolDatagram
            {
                SessionId = lastDatagramInWindow.SessionId,
                OpCode = ProtocolDatagram.OpCodeDataAck,
                WindowId = lastDatagramInWindow.WindowId,
                SequenceNumber = lastEffectiveSeqNr,
                Options = new ProtocolDatagramOptions
                {
                    ErrorCode = processingError?.ErrorCode
                }
            };
            if (processingError == null)
            {
                _sessionHandler.LastAck.Options.IsWindowFull = true;
                _sessionHandler.LastAck.Options.MaxWindowSize = _sessionHandler.MaxWindowSize;
            }
            _sessionHandler.NetworkApi.RequestSend(_sessionHandler.RemoteEndpoint,
                _sessionHandler.LastAck, null, null);
        }

        private void ProcessAndPassCurrentWindowGroupToApplicationLayer()
        {
            ProtocolDatagram windowGroupAsMessage = ProtocolDatagram.CreateMessageOutOfWindow(CurrentWindowGroup);

            // now create message for application layer, and decode any long options present.
            ProtocolMessage messageForApp = new ProtocolMessage
            {
                DataBytes = windowGroupAsMessage.DataBytes,
                DataOffset = windowGroupAsMessage.DataOffset,
                DataLength = windowGroupAsMessage.DataLength
            };
            if (windowGroupAsMessage.Options != null)
            {
                messageForApp.Attributes = new Dictionary<string, List<string>>();
                foreach (var option in windowGroupAsMessage.Options.AllOptions)
                {
                    if (option.Key.StartsWith(ProtocolDatagramFragmenter.EncodedOptionNamePrefix))
                    {
                        // NB: long option decoding could result in errors.
                        try
                        {
                            var originalOption = ProtocolDatagramFragmenter.DecodeLongOption(option.Value);
                            messageForApp.Attributes.Add(originalOption[0], new List<string> { originalOption[1] });
                        }
                        catch (Exception ex)
                        {
                            throw new ProtocolOperationException(
                                ProtocolOperationException.ErrorCodeOptionDecodingError, ex);
                        }
                    }
                    else
                    {
                        messageForApp.Attributes.Add(option.Key, option.Value);
                    }
                }
            }

            // ready to pass on to application layer.
            ProcessCurrentWindowOptions(windowGroupAsMessage.Options);

            // transition to open state if no errors occured in keeping with rule
            // that first received window group ends opening state.
            if (_sessionHandler.State == SessionState.Opening)
            {
                TransitionToOpenState();
            }
            _sessionHandler.OnMessageReceived(messageForApp);
        }

        private void ProcessCurrentWindowOptions(ProtocolDatagramOptions windowOptions)
        {
            if (windowOptions?.IdleTimeout != null)
            {
                _sessionHandler.RemoteIdleTimeout = windowOptions.IdleTimeout;
                _sessionHandler.ResetIdleTimeout();
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
