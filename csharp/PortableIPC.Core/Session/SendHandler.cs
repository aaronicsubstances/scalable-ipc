using PortableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortableIPC.Core.Session
{
    public class SendHandler: ISessionStateHandler
    {
        private readonly ISessionHandler _sessionHandler;

        private SendHandlerAssistant _currentWindowHandler;
        private int _retryCount;
        private AbstractPromiseCallback<VoidType> _pendingPromiseCallback;
        private bool _closing;
        private bool _inUseByBulkSend;

        public SendHandler(ISessionHandler sessionHandler)
        {
            _sessionHandler = sessionHandler;
        }

        protected internal List<ProtocolDatagram> CurrentWindow { get; } = new List<ProtocolDatagram>();
        protected internal bool SendInProgress { get; set; }
        protected internal int NextSeqStart { get; set; }

        public void Shutdown(Exception error, bool timeout)
        {
            _currentWindowHandler?.Cancel();
            if (SendInProgress)
            {
                // ignore error if closing.
                if (_closing)
                {
                    _pendingPromiseCallback.CompleteSuccessfully(VoidType.Instance);
                }
                else if (error == null)
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
                _pendingPromiseCallback?.CompleteExceptionally(error);

                SendInProgress = false;
            }
            CurrentWindow.Clear();
        }

        public bool ProcessErrorReceive()
        {
            return false;
        }

        public bool ProcessReceive(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            if (!SendInProgress)
            {
                return false;
            }

            if (_sessionHandler.IsOpened)
            {
                if (message.OpCode != ProtocolDatagram.OpCodeAck)
                {
                    return false;
                }
            }
            else
            {
                if (message.OpCode != ProtocolDatagram.OpCodeOpenAck)
                {
                    return false;
                }
            }
            promiseCb.CompleteSuccessfully(VoidType.Instance);
            ProcessAckReceipt(message);
            return true;
        }

        protected internal void ProcessSendWindow(AbstractPromiseCallback<VoidType> promiseCb)
        {
            _retryCount = 0;
            _currentWindowHandler = null;
            _pendingPromiseCallback = promiseCb;
            _inUseByBulkSend = true;

            RestartSend(0);
        }

        public bool ProcessSend(ProtocolDatagram message, AbstractPromiseCallback<VoidType> promiseCb)
        {
            if (SendInProgress)
            {
                promiseCb.CompleteExceptionally(new ProtocolSessionException(_sessionHandler.SessionId,
                    "Send in progress"));
                return true;
            }

            if (message.OpCode == ProtocolDatagram.OpCodeClose || message.OpCode == ProtocolDatagram.OpCodeError)
            {
                _closing = true;
            }
            else
            {
                if (_sessionHandler.IsOpened)
                {
                    if (message.OpCode != ProtocolDatagram.OpCodeData)
                    {
                        return false;
                    }
                }
                else
                {
                    if (message.OpCode != ProtocolDatagram.OpCodeOpen)
                    {
                        return false;
                    }

                    // save open parameters
                }
            }

            // reset current window.
            CurrentWindow.Clear();
            message.SequenceNumber = NextSeqStart;
            message.IsLastInWindow = true;
            CurrentWindow.Add(message);

            SendInProgress = true;
            _retryCount = 0;
            _currentWindowHandler = null;
            _pendingPromiseCallback = promiseCb;
            _inUseByBulkSend = false;

            RestartSend(0);

            return true;
        }

        public bool ProcessSendData(byte[] rawData, AbstractPromiseCallback<VoidType> promiseCb)
        {
            return false;
        }

        private void RestartSend(int startIndex)
        {
            // don't bother if window handler is already about to start sending requested PDUs
            if (_currentWindowHandler != null && _currentWindowHandler.PendingPduIndex <= startIndex)
            {
                return;
            }
            _currentWindowHandler?.Cancel();
            _currentWindowHandler = new SendHandlerAssistant(_sessionHandler, this, startIndex);
            _currentWindowHandler.Start();
        }

        private void ProcessAckReceipt(ProtocolDatagram ack)
        {
            int ackIndex = CurrentWindow.FindIndex(item => item.SequenceNumber == ack.SequenceNumber);
            if (ackIndex == -1)
            {
                // invalid ack.
                return;
            }

            // indirectly clear ack timeout.
            _sessionHandler.ResetIdleTimeout();

            if (ackIndex < CurrentWindow.Count - 1)
            {
                RestartSend(ackIndex + 1);
                return;
            }

            // getting here means assistant is done sending all in current window.
            _currentWindowHandler.Cancel();

            if (!_sessionHandler.IsOpened)
            {
                // save open ack params if possible.
                _sessionHandler.IsOpened = true;
            }

            // move window bounds
            NextSeqStart = ProtocolDatagram.ComputeNextSequenceStart(NextSeqStart, _sessionHandler.WindowSize);

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
            // treat negative max retry count as infinite retry attempts.
            if (_retryCount == _sessionHandler.MaxRetryCount)
            {
                _sessionHandler.ProcessShutdown(null, true);
            }
            else
            {
                _retryCount++;
                RestartSend(0);
            }
        }

        internal void OnWindowSendError(Exception error)
        {
            _sessionHandler.ProcessShutdown(error, false);
        }

        internal void OnWindowSendSuccess()
        {
            if (_closing)
            {
                _sessionHandler.ProcessShutdown(null, false);
            }
            else
            {
                _sessionHandler.ResetAckTimeout(_sessionHandler.AckTimeoutSecs, () => ProcessAckTimeout());
            }
        }
    }
}
