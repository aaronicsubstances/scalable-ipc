using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace PortableIPC.Core.Session
{
    public class ReceiveDataHandler: ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;
        private long _currentWindowId = -1; // used to ignore late acknowledgment send callbacks.

        public ReceiveDataHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        public List<ProtocolDatagram> CurrentWindow { get; set; }

        public void Shutdown(Exception error)
        {
            // nothing to do.
        }

        public bool ProcessReceive(ProtocolDatagram message)
        {
            // assert expected op code
            if (message.OpCode != ProtocolDatagram.OpCodeData)
            {
                return false;
            }

            OnReceiveData(message);
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

        private void OnReceiveData(ProtocolDatagram message)
        {
            // validate state
            if (_sessionHandler.SessionState != SessionState.OpenedForData)
            {
                _sessionHandler.DiscardReceivedMessage(message);
                return;
            }

            if (message.WindowId == _sessionHandler.LastWindowIdReceived)
            {
                // already received and passed to application layer.
                // just send back benign acknowledgement.
                var ack = new ProtocolDatagram
                {
                    SessionId = _sessionHandler.SessionId,
                    OpCode = ProtocolDatagram.OpCodeAck,
                    WindowId = _sessionHandler.LastWindowIdReceived,
                    SequenceNumber = _sessionHandler.LastMaxSeqReceived,
                    IsLastInWindow = true
                };
                _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, ack)
                    .Then<VoidType>(null, HandleAckSendFailure);
                return;
            }
            else if (message.WindowId < _sessionHandler.LastWindowIdReceived)
            {
                // allow 0 if last is not 0.
                if (message.WindowId != 0 || _sessionHandler.LastWindowIdReceived == 0)
                {
                    _sessionHandler.DiscardReceivedMessage(message);
                    return;
                }
            }

            if (message.SequenceNumber >= _sessionHandler.MaxReceiveWindowSize)
            {
                _sessionHandler.DiscardReceivedMessage(message);
                return;
            }

            // save message.
            AddToCurrentWindow(message);

            // send back ack
            var lastEffectiveSeqNr = GetLastPositionInSlidingWindow();
            var isWindowFull = IsCurrentWindowFull(lastEffectiveSeqNr);
            if (lastEffectiveSeqNr != -1)
            {
                var ack = new ProtocolDatagram
                {
                    SessionId = message.SessionId,
                    OpCode = ProtocolDatagram.OpCodeAck,
                    WindowId = message.WindowId,
                    SequenceNumber = lastEffectiveSeqNr,
                    IsLastInWindow = isWindowFull
                };
                long windowIdSnapshot = _currentWindowId;
                _sessionHandler.EndpointHandler.HandleSend(_sessionHandler.ConnectedEndpoint, ack)
                    .Then(_ => HandleAckSendSuccess(windowIdSnapshot), HandleAckSendFailure);
            }
        }

        private void HandleAckSendFailure(Exception error)
        {
            _sessionHandler.PostSeriallyIfNotClosed(() =>
            {
                _sessionHandler.ProcessShutdown(error, false);
            });
        }

        private VoidType HandleAckSendSuccess(long callbackId)
        {
            _sessionHandler.PostSeriallyIfNotClosed(() =>
            {
                // check if ack send callback is coming in too late.
                if (_currentWindowId != callbackId)
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

                // save window data and bounds before reseting current window.
                _sessionHandler.LastWindowIdReceived = CurrentWindow[0].WindowId;
                _sessionHandler.LastMaxSeqReceived = lastEffectiveSeqNr;

                var windowOptions = new Dictionary<string, List<string>>();
                byte[] windowData = ProtocolDatagram.RetrieveData(CurrentWindow, windowOptions);

                // invalidate subsequent ack send confirmations for this current window instance.
                _currentWindowId = -1;

                // ready to pass on to application layer.
                _sessionHandler.PostNonSerially(() => _sessionHandler.OnDataReceived(windowData,
                    windowOptions));
            });
            return VoidType.Instance;
        }

        private void AddToCurrentWindow(ProtocolDatagram message)
        {
            // if window id is different, clear all entries.
            if (_currentWindowId != message.WindowId)
            {
                CurrentWindow?.Clear();
                for (int i = 0; i < _sessionHandler.MaxReceiveWindowSize; i++)
                {
                    CurrentWindow.Add(null);
                }
            }

            // before inserting new message, clear any existing message with set last_in_window option
            // and its effects.
            else if (message.IsLastInWindow == true)
            {
                for (int i = 0; i < CurrentWindow.Count; i++)
                {
                    if (CurrentWindow[i] != null)
                    {
                        if (CurrentWindow[i].IsLastInWindow == true)
                        {
                            CurrentWindow[i] = null;
                        }
                        else if (i > message.SequenceNumber)
                        {
                            CurrentWindow[i] = null;
                        }
                    }
                }
            }

            CurrentWindow[message.SequenceNumber] = message;
            _currentWindowId = CurrentWindow[message.SequenceNumber].WindowId;
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
            return CurrentWindow[lastPosInSlidingWindow].IsLastInWindow == true;
        }
    }
}
