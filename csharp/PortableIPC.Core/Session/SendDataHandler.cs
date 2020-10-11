﻿using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Session
{
    public class SendDataHandler: ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;

        private SendHandlerAssistant _currentWindowHandler;
        private int _retryCount;
        private AbstractPromiseCallback<VoidType> _pendingPromiseCallback;
        private bool _inUseByBulkSend;
        private bool _initialWindowSendFinished;
        private int _retryIndex;

        public SendDataHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        protected internal List<ProtocolDatagram> CurrentWindow { get; } = new List<ProtocolDatagram>();
        protected internal bool SendInProgress { get; set; }

        public void Shutdown(Exception error)
        {
            _currentWindowHandler?.Cancel();
            if (SendInProgress)
            {
                _pendingPromiseCallback.CompleteExceptionally(error);
                SendInProgress = false;
            }
        }

        public bool ProcessReceive(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            if (!SendInProgress)
            {
                return false;
            }

            if (message.OpCode != ProtocolDatagram.OpCodeAck)
            {
                return false;
            }

            promiseCb.CompleteSuccessfully(VoidType.Instance);
            ProcessAckReceipt(message);
            return true;
        }

        public bool ProcessSend(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            if (_sessionHandler.SessionState != SessionState.OpenedForData)
            {
                return false;
            }

            if (SendInProgress)
            {
                promiseCb.CompleteExceptionally(new Exception("Send in progress"));
                return true;
            }

            if (message.OpCode != ProtocolDatagram.OpCodeData)
            {
                return false;
            }

            // reset current window.
            CurrentWindow.Clear();
            message.SequenceNumber = _sessionHandler.NextSendSeqStart;
            message.IsLastInWindow = true;
            CurrentWindow.Add(message);

            SendInProgress = true;
            ProcessSendWindow(promiseCb);

            return true;
        }

        public bool ProcessSend(int opCode, byte[] data, Dictionary<string, List<string>> options, 
            AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }

        protected internal void ProcessSendWindow(AbstractPromiseCallback<VoidType> promiseCb)
        {
            _retryCount = 0;
            _currentWindowHandler = null;
            _pendingPromiseCallback = promiseCb;
            _inUseByBulkSend = true;

            _initialWindowSendFinished = false;
            _retryIndex = 0;
            RetrySend();
        }

        private void RetrySend()
        {
            // don't bother if window handler is already about to start sending requested PDUs
            if (_currentWindowHandler != null && _retryIndex >= _currentWindowHandler.PendingPduIndex &&
                _retryIndex < _currentWindowHandler.EndIndex)
            {
                return;
            }
            _currentWindowHandler?.Cancel();
            _currentWindowHandler = new SendHandlerAssistant(_sessionHandler)
            {
                CurrentWindow = CurrentWindow,
                FailureCallback = OnWindowSendError,
                SuccessCallback = OnWindowSendSuccess,
                PendingPduIndex = _retryIndex,
                EndIndex = CurrentWindow.Count
            };

            // after initial bulk window send, switch to sending one at a time.
            if (_initialWindowSendFinished)
            {
                _currentWindowHandler.EndIndex = _retryIndex + 1;
            }
            _currentWindowHandler.Start();
        }

        private void ProcessAckReceipt(ProtocolDatagram ack)
        {
            // Receipt of an ack is interpreted as reception of message with ack's sequence numbers,
            // and all preceding messages in window as well.
            int ackIndex = CurrentWindow.FindIndex(item => item.SequenceNumber == ack.SequenceNumber);
            if (ackIndex == -1)
            {
                // invalid ack.
                return;
            }

            // Indirectly clear ack timeout.
            _sessionHandler.ResetIdleTimeout();

            // Once it is not last message which has not been acknowledged, sending is not
            // complete.
            if (ackIndex < CurrentWindow.Count - 1)
            {
                // Ignore retry if initial bulk send is not complete.
                if (_initialWindowSendFinished)
                {
                    _retryIndex = ackIndex + 1;
                    RetrySend();
                }
                else
                {
                    // let ack timeout trigger retries after initial bulk send.
                }
                return;
            }

            // getting here means assistant is done sending all in current window.
            _currentWindowHandler.Cancel();

            // move window bounds
            _sessionHandler.NextSendSeqStart = ProtocolDatagram.ComputeNextSequenceStart(
                _sessionHandler.NextSendSeqStart, _sessionHandler.WindowSize);

            // if bulk sending, let bulk send handler be the one to determine when to stop
            // sending.
            if (!_inUseByBulkSend)
            {
                SendInProgress = false;
            }

            // complete pending promise.
            _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
        }

        private void ProcessAckTimeout()
        {
            if (_retryCount >= _sessionHandler.MaxRetryCount)
            {
                _sessionHandler.ProcessShutdown(null, true);
            }
            else
            {
                _retryCount++;

                _initialWindowSendFinished = true;
                RetrySend();
            }
        }

        internal void OnWindowSendError(Exception error)
        {
            _sessionHandler.PostSeriallyIfNotClosed(() =>
            {
                _sessionHandler.ProcessShutdown(error, false);
            });
        }

        internal void OnWindowSendSuccess()
        {
            _sessionHandler.PostSeriallyIfNotClosed(() =>
            {
                _sessionHandler.ResetAckTimeout(_sessionHandler.AckTimeoutSecs, () => ProcessAckTimeout());
            });
        }
    }
}